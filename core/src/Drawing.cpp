// STAWN CAD Pro - drawing views, sheet format and exporters.
#include "stawn/Drawing.hpp"

#include <BRepAdaptor_Curve.hxx>
#include <BRepLib.hxx>
#include <BRepBndLib.hxx>
#include <Bnd_Box.hxx>
#include <GCPnts_TangentialDeflection.hxx>
#include <HLRAlgo_Projector.hxx>
#include <HLRBRep_Algo.hxx>
#include <HLRBRep_HLRToShape.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>

#include <algorithm>
#include <cmath>
#include <ctime>
#include <fstream>
#include <sstream>

namespace stawn {

const char* toString(ViewOrientation v) {
    switch (v) {
        case ViewOrientation::Front: return "Front";
        case ViewOrientation::Top: return "Top";
        case ViewOrientation::Bottom: return "Bottom";
        case ViewOrientation::Right: return "Right";
        case ViewOrientation::Left: return "Left";
        case ViewOrientation::Back: return "Back";
        case ViewOrientation::Isometric: return "Isometric";
    }
    return "?";
}

Paper paperFromName(const std::string& n) {
    if (n == "A4") return {"A4", 297, 210};
    if (n == "A2") return {"A2", 594, 420};
    if (n == "A1") return {"A1", 841, 594};
    if (n == "A0") return {"A0", 1189, 841};
    return {"A3", 420, 297};
}

namespace {

// Viewing frame: direction towards the viewer and the sheet's horizontal axis.
gp_Ax2 viewFrame(ViewOrientation v) {
    switch (v) {
        case ViewOrientation::Front: return gp_Ax2(gp_Pnt(), gp_Dir(0, 0, 1), gp_Dir(1, 0, 0));
        case ViewOrientation::Back: return gp_Ax2(gp_Pnt(), gp_Dir(0, 0, -1), gp_Dir(-1, 0, 0));
        case ViewOrientation::Top: return gp_Ax2(gp_Pnt(), gp_Dir(0, 1, 0), gp_Dir(1, 0, 0));
        case ViewOrientation::Bottom: return gp_Ax2(gp_Pnt(), gp_Dir(0, -1, 0), gp_Dir(1, 0, 0));
        case ViewOrientation::Right: return gp_Ax2(gp_Pnt(), gp_Dir(1, 0, 0), gp_Dir(0, 0, -1));
        case ViewOrientation::Left: return gp_Ax2(gp_Pnt(), gp_Dir(-1, 0, 0), gp_Dir(0, 0, 1));
        case ViewOrientation::Isometric:
            return gp_Ax2(gp_Pnt(), gp_Dir(1, 1, 1), gp_Dir(1, 0, -1));
    }
    return gp_Ax2();
}

struct ProjectedView {
    std::vector<Polyline2D> lines;
    double minX = 1e300, minY = 1e300, maxX = -1e300, maxY = -1e300;
};

void addCompound(const TopoDS_Shape& compound, Layer layer, ProjectedView& out) {
    if (compound.IsNull()) return;
    BRepLib::BuildCurves3d(compound);
    for (TopExp_Explorer ex(compound, TopAbs_EDGE); ex.More(); ex.Next()) {
        TopoDS_Edge e = TopoDS::Edge(ex.Current());
        BRepAdaptor_Curve c(e);
        GCPnts_TangentialDeflection pts(c, 0.1, 0.005);
        Polyline2D pl;
        pl.layer = layer;
        for (int i = 1; i <= pts.NbPoints(); ++i) {
            gp_Pnt p = pts.Value(i);
            pl.xy.push_back(p.X());
            pl.xy.push_back(p.Y());
            out.minX = std::min(out.minX, p.X());
            out.maxX = std::max(out.maxX, p.X());
            out.minY = std::min(out.minY, p.Y());
            out.maxY = std::max(out.maxY, p.Y());
        }
        if (pl.xy.size() >= 4) out.lines.push_back(std::move(pl));
    }
}

ProjectedView project(const TopoDS_Shape& shape, ViewOrientation orientation, bool hidden) {
    ProjectedView v;
    if (shape.IsNull()) return v;
    Handle(HLRBRep_Algo) algo = new HLRBRep_Algo();
    algo->Add(shape);
    algo->Projector(HLRAlgo_Projector(viewFrame(orientation)));
    algo->Update();
    algo->Hide();
    HLRBRep_HLRToShape toShape(algo);
    addCompound(toShape.VCompound(), Layer::Visible, v);
    addCompound(toShape.OutLineVCompound(), Layer::Visible, v);
    // Tangent edges (fillet boundaries) drawn thin, as the default display does.
    addCompound(toShape.Rg1LineVCompound(), Layer::Thin, v);
    if (hidden) {
        addCompound(toShape.HCompound(), Layer::Hidden, v);
        addCompound(toShape.OutLineHCompound(), Layer::Hidden, v);
    }
    return v;
}

std::string formatNumber(double v, int decimals = 2) {
    std::ostringstream os;
    os.setf(std::ios::fixed);
    os.precision(decimals);
    os << v;
    std::string s = os.str();
    if (s.find('.') != std::string::npos) {
        while (!s.empty() && s.back() == '0') s.pop_back();
        if (!s.empty() && s.back() == '.') s.pop_back();
    }
    return s;
}

std::string scaleText(double scale) {
    if (scale >= 1) return formatNumber(scale) + ":1";
    return "1:" + formatNumber(1 / scale);
}

void rect(Sheet2D& s, double x, double y, double w, double h, Layer layer = Layer::Border) {
    s.lines.push_back({layer, {x, y, x + w, y, x + w, y + h, x, y + h, x, y}});
}

void line(Sheet2D& s, double x1, double y1, double x2, double y2, Layer layer = Layer::Thin) {
    s.lines.push_back({layer, {x1, y1, x2, y2}});
}

void text(Sheet2D& s, double x, double y, double h, const std::string& t,
          Text2D::Anchor a = Text2D::Anchor::Left) {
    if (!t.empty()) s.texts.push_back({x, y, h, t, a});
}

// Labelled title block cell: small label in the top left corner, value below it.
std::string today() {
    std::time_t t = std::time(nullptr);
    char buf[16];
    std::strftime(buf, sizeof buf, "%d-%m-%Y", std::localtime(&t));
    return buf;
}

void cell(Sheet2D& s, double x, double y, double w, double h, const std::string& label, const std::string& value,
          double valueHeight = 3.5) {
    rect(s, x, y, w, h, Layer::Thin);
    text(s, x + 1.2, y + h - 2.2, 1.6, label);
    // Keep the value below the label: the label's baseline sits 2.2 mm under the top edge.
    double vh = label.empty() ? std::min(valueHeight, h - 3) : std::min(valueHeight, (h - 4.2) * 0.85);
    double baseline = label.empty() ? y + (h - vh) / 2 : y + 1.3;
    // Shrink long values so they fit the cell width (Helvetica averages about 0.52 em per character).
    double width = 0.52 * vh * 1.4 * double(value.size());
    if (width > w - 3) vh *= (w - 3) / width;
    text(s, x + 1.5, baseline, vh, value);
}

void circle(Sheet2D& s, double cx, double cy, double r, Layer layer) {
    Polyline2D pl{layer, {}};
    for (int i = 0; i <= 48; ++i) {
        double a = 2 * M_PI * i / 48;
        pl.xy.push_back(cx + r * std::cos(a));
        pl.xy.push_back(cy + r * std::sin(a));
    }
    s.lines.push_back(pl);
}

void projectionSymbol(Sheet2D& s, double x, double y, ProjectionAngle angle) {
    // Truncated cone: side view as a trapezoid, end view as two concentric circles.
    double coneX = angle == ProjectionAngle::First ? x + 2 : x + 14;
    double circX = angle == ProjectionAngle::First ? x + 18 : x + 4;
    s.lines.push_back({Layer::Visible, {coneX, y + 1.5, coneX + 8, y, coneX + 8, y + 7, coneX, y + 5.5, coneX, y + 1.5}});
    circle(s, circX, y + 3.5, 3.5, Layer::Visible);
    circle(s, circX, y + 3.5, 2, Layer::Visible);
}

void sheetFormat(Sheet2D& s, PartDocument& part, const DrawingSpec& spec) {
    const double W = spec.paper.width, H = spec.paper.height;
    const double left = 20, margin = 10;
    rect(s, left, margin, W - left - margin, H - 2 * margin, Layer::Border);

    // Grid reference zones along the border.
    int cols = int(std::round((W - left - margin) / 50.0)), rows = int(std::round((H - 2 * margin) / 50.0));
    cols = std::max(cols, 4);
    rows = std::max(rows, 3);
    double cw = (W - left - margin) / cols, rh = (H - 2 * margin) / rows;
    for (int i = 0; i < cols; ++i) {
        double x = left + i * cw;
        if (i > 0) {
            line(s, x, margin - 5, x, margin);
            line(s, x, H - margin, x, H - margin + 5);
        }
        text(s, x + cw / 2, margin - 4, 3, std::to_string(cols - i), Text2D::Anchor::Centre);
        text(s, x + cw / 2, H - margin + 1.5, 3, std::to_string(cols - i), Text2D::Anchor::Centre);
    }
    for (int j = 0; j < rows; ++j) {
        double y = margin + j * rh;
        if (j > 0) {
            line(s, left - 5, y, left, y);
            line(s, W - margin, y, W - margin + 5, y);
        }
        std::string letter(1, char('A' + (rows - 1 - j)));
        text(s, left - 3.5, y + rh / 2 - 1.5, 3, letter, Text2D::Anchor::Centre);
        text(s, W - margin + 2.5, y + rh / 2 - 1.5, 3, letter, Text2D::Anchor::Centre);
    }

    // Title block, bottom right, 180 x 56 mm.
    const double tw = 180, x0 = W - margin - tw, y0 = margin;
    rect(s, x0, y0, tw, 56, Layer::Border);
    auto prop = [&](const char* k) { return part.property(k); };
    std::string weight = prop("Weight");

    cell(s, x0, y0, 30, 10, "SCALE", scaleText(spec.scale));
    cell(s, x0 + 30, y0, 25, 10, "SIZE", spec.paper.name);
    cell(s, x0 + 55, y0, 30, 10, "SHEET", "1 of 1");
    cell(s, x0 + 85, y0, 20, 10, "REV", spec.revision);
    rect(s, x0 + 105, y0, 75, 10, Layer::Thin);
    text(s, x0 + 106.2, y0 + 7.4, 1.8, spec.projection == ProjectionAngle::First ? "FIRST ANGLE PROJECTION" : "THIRD ANGLE PROJECTION");
    projectionSymbol(s, x0 + 140, y0 + 1, spec.projection);

    cell(s, x0, y0 + 10, 110, 8, "MATERIAL", prop("Material"));
    cell(s, x0 + 110, y0 + 10, 70, 8, "WEIGHT (g)", weight);
    std::string hardness = prop("SurfaceHardness");
    if (!prop("CoreHardness").empty()) hardness += hardness.empty() ? prop("CoreHardness") : " / " + prop("CoreHardness");
    cell(s, x0, y0 + 18, 70, 8, "HEAT TREATMENT", prop("HeatTreatment"), 3);
    cell(s, x0 + 70, y0 + 18, 110, 8, "CASE DEPTH / HARDNESS",
         prop("CaseDepth") + (hardness.empty() ? "" : "  " + hardness), 2.5);
    std::string partNo = prop("PartNo").empty() ? part.name : prop("PartNo");
    cell(s, x0, y0 + 26, 110, 10, "PART NUMBER", partNo, 5);
    cell(s, x0 + 110, y0 + 26, 70, 10, "CONFIGURATION", part.activeConfiguration, 3);
    std::string title = prop("Description").empty() ? part.name : prop("Description");
    cell(s, x0, y0 + 36, 180, 12, "TITLE", title, 5);
    cell(s, x0, y0 + 48, 90, 8, "", spec.companyName, 3);
    std::string drawn = spec.drawnBy.empty() ? prop("DrawnBy") : spec.drawnBy;
    std::string date = spec.date.empty() ? prop("ApprovalDate") : spec.date;
    if (date.empty()) date = today();
    cell(s, x0 + 90, y0 + 48, 45, 8, "DRAWN", drawn, 2.5);
    cell(s, x0 + 135, y0 + 48, 45, 8, "DATE", date, 2.5);

    // Revision table, top right.
    const double rx = W - margin - 120, ry = H - margin - 12;
    rect(s, rx, ry, 120, 12, Layer::Thin);
    line(s, rx, ry + 6, rx + 120, ry + 6);
    line(s, rx + 15, ry, rx + 15, ry + 12);
    line(s, rx + 90, ry, rx + 90, ry + 12);
    text(s, rx + 7.5, ry + 7.5, 2.5, "REV", Text2D::Anchor::Centre);
    text(s, rx + 52.5, ry + 7.5, 2.5, "DESCRIPTION", Text2D::Anchor::Centre);
    text(s, rx + 105, ry + 7.5, 2.5, "DATE", Text2D::Anchor::Centre);
    text(s, rx + 7.5, ry + 1.5, 2.5, spec.revision, Text2D::Anchor::Centre);
    text(s, rx + 52.5, ry + 1.5, 2.5, "ISSUED FOR PRODUCTION", Text2D::Anchor::Centre);
    text(s, rx + 105, ry + 1.5, 2.5, spec.date, Text2D::Anchor::Centre);
}

void arrow(Sheet2D& s, double x, double y, double dx, double dy) {
    const double len = 3, half = 0.6;
    double nx = -dy, ny = dx;
    s.lines.push_back({Layer::Thin,
                       {x, y, x - dx * len + nx * half, y - dy * len + ny * half, x - dx * len - nx * half,
                        y - dy * len - ny * half, x, y}});
}

void dimension(Sheet2D& s, const DrawingDimension& d, double scale) {
    double value;
    if (d.horizontal) {
        double y = std::max(d.y1, d.y2) + d.offset;
        line(s, d.x1, d.y1 + 1, d.x1, y + 2);
        line(s, d.x2, d.y2 + 1, d.x2, y + 2);
        line(s, d.x1, y, d.x2, y);
        double dir = d.x2 > d.x1 ? 1 : -1;
        arrow(s, d.x1, y, -dir, 0);
        arrow(s, d.x2, y, dir, 0);
        value = std::abs(d.x2 - d.x1) / scale;
        text(s, (d.x1 + d.x2) / 2, y + 1, 3.5, d.prefix + (d.overrideText.empty() ? formatNumber(value) : d.overrideText),
             Text2D::Anchor::Centre);
    } else {
        double x = std::max(d.x1, d.x2) + d.offset;
        line(s, d.x1 + 1, d.y1, x + 2, d.y1);
        line(s, d.x2 + 1, d.y2, x + 2, d.y2);
        line(s, x, d.y1, x, d.y2);
        double dir = d.y2 > d.y1 ? 1 : -1;
        arrow(s, x, d.y1, 0, -dir);
        arrow(s, x, d.y2, 0, dir);
        value = std::abs(d.y2 - d.y1) / scale;
        text(s, x + 1.5, (d.y1 + d.y2) / 2 - 1.75, 3.5,
             d.prefix + (d.overrideText.empty() ? formatNumber(value) : d.overrideText));
    }
}

}  // namespace

DrawingSpec DrawingSpec::standardThreeView(const PartDocument& part, const std::string& paperName) {
    DrawingSpec spec;
    spec.paper = paperFromName(paperName);
    Bnd_Box box;
    if (!part.shape().IsNull()) BRepBndLib::Add(part.shape(), box);
    double dx = 50, dy = 50, dz = 50;
    if (!box.IsVoid()) {
        double x0, y0, z0, x1, y1, z1;
        box.Get(x0, y0, z0, x1, y1, z1);
        dx = x1 - x0;
        dy = y1 - y0;
        dz = z1 - z0;
    }
    // Front view width (dx) plus side view width (dz) should fill about 55% of the
    // drawing area width, and front height (dy) plus top view height (dz) about 50% of its height.
    double areaW = spec.paper.width - 30, areaH = spec.paper.height - 20 - 60;
    double fit = std::min(0.55 * areaW / (dx + dz + 1e-9), 0.5 * areaH / (dy + dz + 1e-9));
    const double scales[] = {50, 20, 10, 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01};  // ISO 5455
    spec.scale = 0.01;
    for (double s : scales)
        if (s <= fit) {
            spec.scale = s;
            break;
        }

    double gap = 15;
    double fx = 20 + areaW * 0.45, fy = 10 + 60 + areaH * 0.55;
    double sideOffset = (dx + dz) / 2 * spec.scale + gap;
    double topOffset = (dy + dz) / 2 * spec.scale + gap;
    bool first = spec.projection == ProjectionAngle::First;
    bool turned = std::any_of(part.features.begin(), part.features.end(),
                              [](const Feature& f) { return f.type == FeatureType::Revolve; });
    spec.views.push_back({"Drawing View1", ViewOrientation::Front, fx, fy, 0, false, true, turned ? "\u00D8" : ""});
    spec.views.push_back({"Drawing View2", ViewOrientation::Top, fx, first ? fy - topOffset : fy + topOffset});
    spec.views.push_back({"Drawing View3", ViewOrientation::Right, first ? fx - sideOffset : fx + sideOffset, fy});
    spec.views.push_back({"Drawing View4", ViewOrientation::Isometric, spec.paper.width - 75, spec.paper.height - 60,
                          spec.scale * 0.75});
    spec.notes.push_back({25, 64, 3,
                          "GENERAL NOTES:\n"
                          "1. MATERIAL: ${Material}\n"
                          "2. HEAT TREATMENT: ${HeatTreatment}\n"
                          "3. CASE DEPTH: ${CaseDepth}\n"
                          "4. SURFACE HARDNESS: ${SurfaceHardness}, CORE HARDNESS: ${CoreHardness}\n"
                          "5. REMOVE ALL BURRS AND SHARP EDGES\n"
                          "6. UNTOLERANCED DIMENSIONS TO ISO 2768-m"});
    return spec;
}

Sheet2D renderDrawing(PartDocument& part, const DrawingSpec& spec) {
    Sheet2D s;
    s.paper = spec.paper;
    sheetFormat(s, part, spec);
    for (auto& v : spec.views) {
        double scale = v.scale > 0 ? v.scale : spec.scale;
        ProjectedView pv = project(part.shape(), v.orientation, v.showHidden);
        if (pv.lines.empty()) continue;
        double cx = (pv.minX + pv.maxX) / 2, cy = (pv.minY + pv.maxY) / 2;
        for (auto& pl : pv.lines) {
            for (size_t i = 0; i + 1 < pl.xy.size(); i += 2) {
                pl.xy[i] = v.x + (pl.xy[i] - cx) * scale;
                pl.xy[i + 1] = v.y + (pl.xy[i + 1] - cy) * scale;
            }
            s.lines.push_back(std::move(pl));
        }
        if (v.overallDimensions) {
            double x0 = v.x + (pv.minX - cx) * scale, x1 = v.x + (pv.maxX - cx) * scale;
            double y0 = v.y + (pv.minY - cy) * scale, y1 = v.y + (pv.maxY - cy) * scale;
            dimension(s, {x0, y1, x1, y1, 10, true}, scale);
            dimension(s, {x1, y0, x1, y1, 10, false, v.heightPrefix}, scale);
        }
        if (v.orientation == ViewOrientation::Isometric)
            text(s, v.x, v.y - (pv.maxY - pv.minY) / 2 * scale - 8, 3, "ISOMETRIC VIEW", Text2D::Anchor::Centre);
    }
    for (auto& d : spec.dimensions) dimension(s, d, spec.scale);
    for (auto& n : spec.notes) {
        std::istringstream lines(n.text);
        std::string ln;
        double y = n.y;
        while (std::getline(lines, ln)) {
            // Resolve ${Property} links against the part's custom properties.
            std::string out;
            size_t i = 0;
            while (i < ln.size()) {
                auto a = ln.find("${", i);
                if (a == std::string::npos) {
                    out += ln.substr(i);
                    break;
                }
                auto b = ln.find('}', a);
                if (b == std::string::npos) {
                    out += ln.substr(i);
                    break;
                }
                out += ln.substr(i, a - i) + part.property(ln.substr(a + 2, b - a - 2));
                i = b + 1;
            }
            text(s, n.x, y, n.height, out);
            y -= n.height * 1.6;
        }
    }
    return s;
}

// --- Exporters -------------------------------------------------------------------------

namespace {

std::string xmlEscape(const std::string& t) {
    std::string o;
    for (char c : t) {
        switch (c) {
            case '&': o += "&amp;"; break;
            case '<': o += "&lt;"; break;
            case '>': o += "&gt;"; break;
            case '"': o += "&quot;"; break;
            default: o += c;
        }
    }
    return o;
}

double strokeWidth(Layer l) {
    switch (l) {
        case Layer::Visible: return 0.5;
        case Layer::Border: return 0.7;
        default: return 0.25;
    }
}

const char* layerName(Layer l) {
    switch (l) {
        case Layer::Visible: return "VISIBLE";
        case Layer::Hidden: return "HIDDEN";
        case Layer::Thin: return "THIN";
        case Layer::Border: return "BORDER";
        case Layer::Centre: return "CENTRE";
    }
    return "0";
}

// UTF-8 to Windows-1252 for the characters drawings use (Ø, °, ±, µ and Latin-1).
std::string toWinAnsi(const std::string& s) {
    std::string out;
    for (size_t i = 0; i < s.size(); ++i) {
        unsigned char c = s[i];
        if (c < 0x80) {
            out += char(c);
        } else if ((c & 0xE0) == 0xC0 && i + 1 < s.size()) {
            unsigned cp = ((c & 0x1F) << 6) | (s[i + 1] & 0x3F);
            out += cp < 0x100 ? char(cp) : '?';
            ++i;
        } else {
            out += '?';
            while (i + 1 < s.size() && (s[i + 1] & 0xC0) == 0x80) ++i;
        }
    }
    return out;
}

}  // namespace

std::string toSVG(const Sheet2D& sheet) {
    const double W = sheet.paper.width, H = sheet.paper.height;
    std::ostringstream o;
    o << "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" << W << "mm\" height=\"" << H << "mm\" viewBox=\"0 0 "
      << W << " " << H << "\">\n<rect width=\"100%\" height=\"100%\" fill=\"white\"/>\n";
    o << "<g fill=\"none\" stroke=\"black\" stroke-linecap=\"round\" stroke-linejoin=\"round\">\n";
    o.precision(4);
    o.setf(std::ios::fixed);
    for (auto& pl : sheet.lines) {
        o << "<polyline stroke-width=\"" << strokeWidth(pl.layer) << "\"";
        if (pl.layer == Layer::Hidden) o << " stroke-dasharray=\"2 1\"";
        if (pl.layer == Layer::Centre) o << " stroke-dasharray=\"6 1 1 1\"";
        o << " points=\"";
        for (size_t i = 0; i + 1 < pl.xy.size(); i += 2) o << pl.xy[i] << "," << (H - pl.xy[i + 1]) << " ";
        o << "\"/>\n";
    }
    o << "</g>\n<g font-family=\"Helvetica, Arial, sans-serif\" fill=\"black\">\n";
    for (auto& t : sheet.texts) {
        const char* anchor = t.anchor == Text2D::Anchor::Centre ? "middle" : t.anchor == Text2D::Anchor::Right ? "end" : "start";
        o << "<text x=\"" << t.x << "\" y=\"" << (H - t.y) << "\" font-size=\"" << t.height * 1.4
          << "\" text-anchor=\"" << anchor << "\">" << xmlEscape(t.text) << "</text>\n";
    }
    o << "</g>\n</svg>\n";
    return o.str();
}

bool exportSVG(const Sheet2D& sheet, const std::string& path) {
    std::ofstream f(path);
    f << toSVG(sheet);
    return bool(f);
}

bool exportDXF(const Sheet2D& sheet, const std::string& path) {
    std::ofstream f(path);
    if (!f) return false;
    f.precision(6);
    f.setf(std::ios::fixed);
    f << "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n";
    f << "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n70\n2\n"
         "0\nLTYPE\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0.0\n"
         "0\nLTYPE\n2\nHIDDEN\n70\n0\n3\nHidden\n72\n65\n73\n2\n40\n3.0\n49\n2.0\n49\n-1.0\n"
         "0\nENDTAB\n0\nTABLE\n2\nLAYER\n70\n5\n";
    for (Layer l : {Layer::Visible, Layer::Hidden, Layer::Thin, Layer::Border, Layer::Centre}) {
        int color = l == Layer::Hidden ? 5 : l == Layer::Thin ? 8 : 7;
        f << "0\nLAYER\n2\n" << layerName(l) << "\n70\n0\n62\n" << color << "\n6\n"
          << (l == Layer::Hidden ? "HIDDEN" : "CONTINUOUS") << "\n";
    }
    f << "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n";
    for (auto& pl : sheet.lines)
        for (size_t i = 0; i + 3 < pl.xy.size(); i += 2)
            f << "0\nLINE\n8\n" << layerName(pl.layer) << "\n10\n" << pl.xy[i] << "\n20\n" << pl.xy[i + 1]
              << "\n30\n0.0\n11\n" << pl.xy[i + 2] << "\n21\n" << pl.xy[i + 3] << "\n31\n0.0\n";
    for (auto& t : sheet.texts) {
        int just = t.anchor == Text2D::Anchor::Centre ? 1 : t.anchor == Text2D::Anchor::Right ? 2 : 0;
        f << "0\nTEXT\n8\nTEXT\n10\n" << t.x << "\n20\n" << t.y << "\n30\n0.0\n40\n" << t.height << "\n1\n"
          << toWinAnsi(t.text) << "\n";
        if (just) f << "72\n" << just << "\n11\n" << t.x << "\n21\n" << t.y << "\n31\n0.0\n";
    }
    f << "0\nENDSEC\n0\nEOF\n";
    return bool(f);
}

bool exportPDF(const Sheet2D& sheet, const std::string& path) {
    const double k = 72.0 / 25.4;  // mm to points
    std::ostringstream c;
    c.precision(3);
    c.setf(std::ios::fixed);
    c << "1 J 1 j\n";
    for (auto& pl : sheet.lines) {
        if (pl.xy.size() < 4) continue;
        c << strokeWidth(pl.layer) * k << " w ";
        c << (pl.layer == Layer::Hidden ? "[5.7 2.8] 0 d\n" : "[] 0 d\n");
        c << pl.xy[0] * k << " " << pl.xy[1] * k << " m\n";
        for (size_t i = 2; i + 1 < pl.xy.size(); i += 2) c << pl.xy[i] * k << " " << pl.xy[i + 1] * k << " l\n";
        c << "S\n";
    }
    for (auto& t : sheet.texts) {
        std::string s = toWinAnsi(t.text);
        double size = t.height * 1.4 * k;
        double width = 0.52 * size * s.size();  // Helvetica average advance
        double x = t.x * k - (t.anchor == Text2D::Anchor::Centre ? width / 2 : t.anchor == Text2D::Anchor::Right ? width : 0);
        std::string esc;
        for (char ch : s) {
            if (ch == '(' || ch == ')' || ch == '\\') esc += '\\';
            esc += ch;
        }
        c << "BT /F1 " << size << " Tf " << x << " " << t.y * k << " Td (" << esc << ") Tj ET\n";
    }
    std::string content = c.str();

    std::ostringstream pdf;
    std::vector<size_t> offsets;
    pdf << "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n";
    auto obj = [&](const std::string& body) {
        offsets.push_back(size_t(pdf.tellp()));
        pdf << offsets.size() << " 0 obj\n" << body << "\nendobj\n";
    };
    std::ostringstream page;
    page.precision(2);
    page.setf(std::ios::fixed);
    page << "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " << sheet.paper.width * k << " " << sheet.paper.height * k
         << "] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>";
    obj("<< /Type /Catalog /Pages 2 0 R >>");
    obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
    obj(page.str());
    obj("<< /Length " + std::to_string(content.size()) + " >>\nstream\n" + content + "endstream");
    obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
    size_t xref = size_t(pdf.tellp());
    pdf << "xref\n0 " << offsets.size() + 1 << "\n0000000000 65535 f \n";
    for (size_t off : offsets) {
        char buf[32];
        std::snprintf(buf, sizeof buf, "%010zu 00000 n \n", off);
        pdf << buf;
    }
    pdf << "trailer\n<< /Size " << offsets.size() + 1 << " /Root 1 0 R >>\nstartxref\n" << xref << "\n%%EOF\n";

    std::ofstream f(path, std::ios::binary);
    f << pdf.str();
    return bool(f);
}

}  // namespace stawn

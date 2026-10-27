#include "stawn/Commands.hpp"

#include <BRepAdaptor_Curve.hxx>
#include <BRep_Tool.hxx>
#include <TopExp.hxx>
#include <TopTools_IndexedDataMapOfShapeListOfShape.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>

#include <cmath>
#include <stdexcept>

#include "stawn/Drawing.hpp"

using nlohmann::json;

namespace stawn {

namespace {

constexpr double kPi = 3.14159265358979323846;

struct OpInfo {
    const char* name;
    const char* description;
    const char* params;  // JSON object of parameter -> description
};

const OpInfo kOps[] = {
    {"new_part", "Start a new empty part", R"J({"name":"part name (optional)"})J"},
    {"open", "Open a .stprt part file", R"J({"path":"file path"})J"},
    {"save", "Save the part as .stprt", R"J({"path":"file path"})J"},
    {"import_step", "Add a STEP file as an imported body", R"J({"path":"file path"})J"},
    {"create_sketch", "Start a sketch on a standard plane", R"J({"plane":"Front, Top or Right","name":"optional"})J"},
    {"draw_line", "Draw a line in the active sketch", R"J({"x1":"mm","y1":"mm","x2":"mm","y2":"mm","construction":"bool, optional"})J"},
    {"draw_centerline", "Draw a construction line, e.g. a revolve axis", R"J({"x1":"mm","y1":"mm","x2":"mm","y2":"mm"})J"},
    {"draw_rectangle", "Draw a corner rectangle with horizontal and vertical relations", R"J({"x1":"mm","y1":"mm","x2":"mm","y2":"mm"})J"},
    {"draw_circle", "Draw a circle", R"J({"cx":"mm","cy":"mm","r":"radius mm"})J"},
    {"draw_arc", "Draw a counter-clockwise arc", R"J({"cx":"mm","cy":"mm","r":"mm","start_angle":"deg","end_angle":"deg"})J"},
    {"draw_polygon", "Draw a regular polygon", R"J({"cx":"mm","cy":"mm","r":"circumradius mm","sides":"3 or more","rotation":"deg, optional"})J"},
    {"add_relation", "Add a sketch relation", R"J({"type":"coincident, horizontal, vertical, parallel, perpendicular, equalLength, pointOnLine, midpoint, tangentLineArc, fix","a":"index","b":"index","kindA":"point|line|circle|arc","kindB":"point|line|circle|arc"})J"},
    {"add_dimension", "Add a driving sketch dimension", R"J({"type":"distance, horizontalDistance, verticalDistance, length, radius, diameter, angle","a":"index","b":"index","value":"mm or deg","kindA":"...","kindB":"..."})J"},
    {"close_sketch", "Solve and close the active sketch", R"J({})J"},
    {"extrude", "Boss-extrude a sketch", R"J({"depth":"mm","sketch":"optional, default last sketch","mid_plane":"bool","reverse":"bool"})J"},
    {"cut_extrude", "Cut-extrude a sketch", R"J({"depth":"mm","sketch":"optional","mid_plane":"bool","reverse":"bool"})J"},
    {"revolve", "Revolve a sketch about one of its lines", R"J({"axis_line":"line index, default the first construction line","angle":"deg, default 360","sketch":"optional"})J"},
    {"cut_revolve", "Revolve a sketch to remove material", R"J({"axis_line":"line index","angle":"deg","sketch":"optional"})J"},
    {"fillet", "Round edges", R"J({"radius":"mm","edges":"all, circular, linear, or a list of [x,y,z] points on the edges"})J"},
    {"chamfer", "Bevel edges", R"J({"distance":"mm","edges":"all, circular, linear, or a list of [x,y,z] points"})J"},
    {"rebuild", "Rebuild the model", R"J({})J"},
    {"list_features", "List the feature tree", R"J({})J"},
    {"list_dimensions", "List all driving dimensions with their values", R"J({})J"},
    {"set_dimension", "Change a dimension such as D1@Sketch1", R"J({"name":"D1@Sketch1","value":"number","this_configuration_only":"bool"})J"},
    {"suppress", "Suppress or unsuppress a feature in the active configuration", R"J({"feature":"name","suppressed":"bool, default true"})J"},
    {"rename_feature", "Rename a feature", R"J({"from":"old name","to":"new name"})J"},
    {"add_configuration", "Add a configuration and make it active", R"J({"name":"name","description":"optional"})J"},
    {"set_configuration", "Activate a configuration", R"J({"name":"name"})J"},
    {"list_configurations", "List configurations", R"J({})J"},
    {"set_property", "Set a custom property", R"J({"key":"name","value":"text, may use ${Mass} ${Material} ...","configuration_specific":"bool"})J"},
    {"get_properties", "Resolved custom properties of the active configuration", R"J({})J"},
    {"set_material", "Set the material from the library, or a custom density", R"J({"name":"material name","density":"kg/m3, optional"})J"},
    {"list_materials", "List the material library", R"J({})J"},
    {"mass_properties", "Volume, area, mass and centre of mass", R"J({})J"},
    {"export_step", "Export STEP", R"J({"path":"file path"})J"},
    {"export_stl", "Export STL", R"J({"path":"file path"})J"},
    {"export_drawing", "Make the standard drawing and export it", R"J({"path":"file path","paper":"A4..A0","format":"pdf, dxf or svg (default from extension)"})J"},
};

double num(const json& c, const char* key) {
    if (!c.contains(key) || !c[key].is_number()) throw std::runtime_error(std::string("missing number '") + key + "'");
    return c[key].get<double>();
}

double numOr(const json& c, const char* key, double fallback) {
    return c.contains(key) && c[key].is_number() ? c[key].get<double>() : fallback;
}

std::string str(const json& c, const char* key) {
    if (!c.contains(key) || !c[key].is_string()) throw std::runtime_error(std::string("missing text '") + key + "'");
    return c[key].get<std::string>();
}

RefKind kindFrom(const std::string& s) {
    if (s == "line") return RefKind::Line;
    if (s == "circle") return RefKind::Circle;
    if (s == "arc") return RefKind::Arc;
    return RefKind::Point;
}

bool isSeam(const TopoDS_Edge& e, const TopTools_IndexedDataMapOfShapeListOfShape& edgeFaces) {
    const TopTools_ListOfShape& faces = edgeFaces.FindFromKey(e);
    for (auto it = faces.cbegin(); it != faces.cend(); ++it)
        if (BRep_Tool::IsClosed(e, TopoDS::Face(*it))) return true;
    return false;
}

// Points on the body's edges picked by a selector: "all", "circular", "linear" or explicit points.
std::vector<EdgeRef> selectEdges(PartDocument& doc, const json& sel) {
    std::vector<EdgeRef> refs;
    if (sel.is_array()) {
        for (auto& p : sel) refs.push_back({p.at(0).get<double>(), p.at(1).get<double>(), p.at(2).get<double>()});
        return refs;
    }
    std::string mode = sel.is_string() ? sel.get<std::string>() : "all";
    doc.rebuild();
    const TopoDS_Shape& body = doc.shape();
    if (body.IsNull()) throw std::runtime_error("there is no body to select edges on");
    TopTools_IndexedMapOfShape edges;
    TopTools_IndexedDataMapOfShapeListOfShape edgeFaces;
    TopExp::MapShapes(body, TopAbs_EDGE, edges);
    TopExp::MapShapesAndAncestors(body, TopAbs_EDGE, TopAbs_FACE, edgeFaces);
    for (int i = 1; i <= edges.Extent(); ++i) {
        TopoDS_Edge e = TopoDS::Edge(edges(i));
        if (BRep_Tool::Degenerated(e) || isSeam(e, edgeFaces)) continue;
        BRepAdaptor_Curve c(e);
        if (mode == "circular" && c.GetType() != GeomAbs_Circle) continue;
        if (mode == "linear" && c.GetType() != GeomAbs_Line) continue;
        gp_Pnt mid = c.Value((c.FirstParameter() + c.LastParameter()) / 2);
        refs.push_back({mid.X(), mid.Y(), mid.Z()});
    }
    if (refs.empty()) throw std::runtime_error("no edges match '" + mode + "'");
    return refs;
}

json featureList(PartDocument& doc) {
    json out = json::array();
    for (auto& f : doc.features) {
        json fj = {{"name", f.name}, {"type", toString(f.type)}, {"suppressed", doc.isSuppressed(f)}};
        if (!f.error.empty()) fj["error"] = f.error;
        if (f.type == FeatureType::Sketch) {
            Sketch s = f.sketch;
            fj["status"] = toString(s.solve().status);
        }
        out.push_back(fj);
    }
    return out;
}

}  // namespace

CommandSession::CommandSession() : owned_(std::make_unique<PartDocument>()), slot_(&owned_) {}
CommandSession::CommandSession(std::unique_ptr<PartDocument> doc) : owned_(std::move(doc)), slot_(&owned_) {}
CommandSession::CommandSession(std::unique_ptr<PartDocument>* externalSlot) : slot_(externalSlot) {}

void CommandSession::reset(std::unique_ptr<PartDocument> doc) {
    *slot_ = std::move(doc);
    activeSketch_.clear();
    lastSketch_.clear();
}

json CommandSession::describe() {
    json out = json::array();
    for (auto& op : kOps) out.push_back({{"op", op.name}, {"description", op.description}, {"params", json::parse(op.params)}});
    return out;
}

Sketch& CommandSession::activeSketch() {
    Feature* f = activeSketch_.empty() ? nullptr : doc().feature(activeSketch_);
    if (!f) throw std::runtime_error("no active sketch: use create_sketch first");
    return f->sketch;
}

int CommandSession::pointAt(double x, double y) {
    Sketch& s = activeSketch();
    for (size_t i = 0; i < s.points.size(); ++i)
        if (std::abs(s.points[i].x - x) < 1e-6 && std::abs(s.points[i].y - y) < 1e-6) return int(i);
    return s.addPoint(x, y);
}

json CommandSession::run(const json& commands) {
    json results = json::array();
    for (auto& c : commands) {
        json r = execute(c);
        results.push_back(r);
        if (!r.value("ok", false)) break;
    }
    return results;
}

json CommandSession::execute(const json& c) {
    try {
        std::string op = str(c, "op");
        json r = {{"ok", true}};

        if (op == "new_part") {
            reset(std::make_unique<PartDocument>());
            if (c.contains("name")) doc().name = str(c, "name");
            return r;
        }
        if (op == "open") {
            std::string err;
            auto loaded = PartDocument::load(str(c, "path"), &err);
            if (!loaded) throw std::runtime_error(err);
            reset(std::move(loaded));
            doc().rebuild();
            r["features"] = featureList(doc());
            return r;
        }
        PartDocument& doc = this->doc();
        if (op == "save") {
            if (!doc.save(str(c, "path"))) throw std::runtime_error("cannot write " + str(c, "path"));
        } else if (op == "import_step") {
            std::string err;
            Feature* f = doc.importSTEP(str(c, "path"), &err);
            if (!f) throw std::runtime_error(err);
            r["feature"] = f->name;
        } else if (op == "create_sketch") {
            auto plane = planeFromString(c.value("plane", "Front"));
            if (!plane) throw std::runtime_error("plane must be Front, Top or Right");
            Feature& f = doc.addSketch(*plane, c.value("name", ""));
            activeSketch_ = lastSketch_ = f.name;
            r["sketch"] = f.name;
        } else if (op == "draw_line" || op == "draw_centerline") {
            Sketch& s = activeSketch();
            int a = pointAt(num(c, "x1"), num(c, "y1")), b = pointAt(num(c, "x2"), num(c, "y2"));
            r["line"] = s.addLine(a, b, op == "draw_centerline" || c.value("construction", false));
            r["points"] = {a, b};
        } else if (op == "draw_rectangle") {
            Sketch& s = activeSketch();
            double x1 = num(c, "x1"), y1 = num(c, "y1"), x2 = num(c, "x2"), y2 = num(c, "y2");
            int p[4] = {pointAt(x1, y1), pointAt(x2, y1), pointAt(x2, y2), pointAt(x1, y2)};
            json lines = json::array();
            for (int i = 0; i < 4; ++i) {
                int l = s.addLine(p[i], p[(i + 1) % 4]);
                if (i % 2 == 0) s.horizontal(l);
                else s.vertical(l);
                lines.push_back(l);
            }
            r["lines"] = lines;
        } else if (op == "draw_circle") {
            Sketch& s = activeSketch();
            double radius = num(c, "r");
            if (radius <= 0) throw std::runtime_error("radius must be positive");
            r["circle"] = s.addCircle(pointAt(num(c, "cx"), num(c, "cy")), radius);
        } else if (op == "draw_arc") {
            Sketch& s = activeSketch();
            double cx = num(c, "cx"), cy = num(c, "cy"), radius = num(c, "r");
            double a0 = num(c, "start_angle") * kPi / 180, a1 = num(c, "end_angle") * kPi / 180;
            int ctr = pointAt(cx, cy);
            int st = pointAt(cx + radius * std::cos(a0), cy + radius * std::sin(a0));
            int en = pointAt(cx + radius * std::cos(a1), cy + radius * std::sin(a1));
            r["arc"] = s.addArc(ctr, st, en);
        } else if (op == "draw_polygon") {
            Sketch& s = activeSketch();
            int sides = int(num(c, "sides"));
            if (sides < 3) throw std::runtime_error("a polygon needs at least 3 sides");
            double cx = num(c, "cx"), cy = num(c, "cy"), radius = num(c, "r"), rot = numOr(c, "rotation", 0) * kPi / 180;
            std::vector<int> pts;
            for (int i = 0; i < sides; ++i) {
                double a = rot + 2 * kPi * i / sides;
                pts.push_back(pointAt(cx + radius * std::cos(a), cy + radius * std::sin(a)));
            }
            json lines = json::array();
            for (int i = 0; i < sides; ++i) lines.push_back(s.addLine(pts[i], pts[(i + 1) % sides]));
            r["lines"] = lines;
        } else if (op == "add_relation" || op == "add_dimension") {
            Sketch& s = activeSketch();
            auto type = constraintTypeFromString(str(c, "type"));
            if (!type) throw std::runtime_error("unknown relation or dimension type " + str(c, "type"));
            SketchConstraint k;
            k.type = *type;
            k.a = int(numOr(c, "a", -1));
            k.b = int(numOr(c, "b", -1));
            k.refKindA = kindFrom(c.value("kindA", "point"));
            k.refKindB = kindFrom(c.value("kindB", "point"));
            k.value = numOr(c, "value", 0);
            k.value2 = numOr(c, "value2", 0);
            k.name = c.value("name", "");
            if (op == "add_dimension" && !k.isDimension()) throw std::runtime_error(str(c, "type") + " is a relation, not a dimension");
            int idx = s.addConstraint(k);
            if (s.constraints[idx].isDimension()) r["dimension"] = s.constraints[idx].name + "@" + activeSketch_;
            SolveResult sr = s.solve();
            r["status"] = toString(sr.status);
        } else if (op == "close_sketch") {
            SolveResult sr = activeSketch().solve();
            r["sketch"] = activeSketch_;
            r["status"] = toString(sr.status);
            r["degrees_of_freedom"] = sr.degreesOfFreedom;
            activeSketch_.clear();
        } else if (op == "extrude" || op == "cut_extrude" || op == "revolve" || op == "cut_revolve") {
            std::string sk = c.value("sketch", activeSketch_.empty() ? lastSketch_ : activeSketch_);
            Feature* skf = doc.feature(sk);
            if (!skf || skf->type != FeatureType::Sketch) throw std::runtime_error("no sketch to use: create one first");
            activeSketch_.clear();
            Operation operation = op.rfind("cut_", 0) == 0 ? Operation::Cut : Operation::Boss;
            Feature* made;
            if (op == "extrude" || op == "cut_extrude") {
                made = &doc.addExtrude(sk, num(c, "depth"), operation, c.value("name", ""));
                made->midPlane = c.value("mid_plane", false);
                made->reverse = c.value("reverse", false);
            } else {
                int axis = int(numOr(c, "axis_line", -1));
                if (axis < 0) {
                    auto& lines = skf->sketch.lines;
                    for (size_t i = 0; i < lines.size(); ++i)
                        if (lines[i].construction) {
                            axis = int(i);
                            break;
                        }
                }
                if (axis < 0) throw std::runtime_error("revolve needs axis_line or a centerline in the sketch");
                made = &doc.addRevolve(sk, axis, numOr(c, "angle", 360), operation, c.value("name", ""));
            }
            r["feature"] = made->name;
        } else if (op == "fillet" || op == "chamfer") {
            std::vector<EdgeRef> edges = selectEdges(doc, c.contains("edges") ? c["edges"] : json("all"));
            Feature& f = op == "fillet" ? doc.addFillet(edges, num(c, "radius"), c.value("name", ""))
                                        : doc.addChamfer(edges, num(c, "distance"), c.value("name", ""));
            r["feature"] = f.name;
            r["edge_count"] = edges.size();
        } else if (op == "rebuild") {
            // handled below
        } else if (op == "list_features") {
            doc.rebuild();
            r["features"] = featureList(doc);
            return r;
        } else if (op == "list_dimensions") {
            json d = json::object();
            for (auto& [k, v] : doc.listDimensions()) d[k] = v;
            r["dimensions"] = d;
            return r;
        } else if (op == "set_dimension") {
            if (!doc.setDimension(str(c, "name"), num(c, "value"), c.value("this_configuration_only", false)))
                throw std::runtime_error("no dimension named " + str(c, "name"));
        } else if (op == "suppress") {
            if (!doc.feature(str(c, "feature"))) throw std::runtime_error("no feature named " + str(c, "feature"));
            doc.setSuppressed(str(c, "feature"), c.value("suppressed", true));
        } else if (op == "rename_feature") {
            if (!doc.renameFeature(str(c, "from"), str(c, "to"))) throw std::runtime_error("cannot rename " + str(c, "from"));
        } else if (op == "add_configuration") {
            std::string name = str(c, "name");
            if (doc.findConfiguration(name)) throw std::runtime_error("configuration " + name + " exists");
            Configuration copy = *doc.activeConfig();
            copy.name = name;
            copy.description = c.value("description", name);
            doc.configurations.push_back(copy);
            doc.activeConfiguration = name;
        } else if (op == "set_configuration") {
            if (!doc.findConfiguration(str(c, "name"))) throw std::runtime_error("no configuration " + str(c, "name"));
            doc.activeConfiguration = str(c, "name");
        } else if (op == "list_configurations") {
            json list = json::array();
            for (auto& cf : doc.configurations)
                list.push_back({{"name", cf.name}, {"description", cf.description}, {"active", cf.name == doc.activeConfiguration}});
            r["configurations"] = list;
            return r;
        } else if (op == "set_property") {
            std::string key = str(c, "key"), value = str(c, "value");
            if (c.value("configuration_specific", false))
                doc.configuration(doc.activeConfiguration).properties[key] = value;
            else
                doc.properties[key] = value;
        } else if (op == "get_properties") {
            doc.rebuild();
            r["properties"] = doc.resolvedProperties();
            return r;
        } else if (op == "set_material") {
            std::string name = str(c, "name");
            if (auto m = findMaterial(name)) doc.material = *m;
            else if (c.contains("density")) doc.material = {name, num(c, "density")};
            else throw std::runtime_error("unknown material " + name + "; give a density to define it");
        } else if (op == "list_materials") {
            json list = json::array();
            for (auto& m : materialLibrary()) list.push_back({{"name", m.name}, {"density", m.density}});
            r["materials"] = list;
            return r;
        } else if (op == "mass_properties") {
            // handled below
        } else if (op == "export_step" || op == "export_stl") {
            doc.rebuild();
            bool ok = op == "export_step" ? doc.exportSTEP(str(c, "path")) : doc.exportSTL(str(c, "path"));
            if (!ok) throw std::runtime_error("export failed (is there a body?)");
            return r;
        } else if (op == "export_drawing") {
            doc.rebuild();
            std::string path = str(c, "path");
            std::string format = c.value("format", path.size() > 4 ? path.substr(path.size() - 3) : "pdf");
            Sheet2D sheet = renderDrawing(doc, DrawingSpec::standardThreeView(doc, c.value("paper", "A3")));
            bool ok = format == "dxf" ? exportDXF(sheet, path) : format == "svg" ? exportSVG(sheet, path) : exportPDF(sheet, path);
            if (!ok) throw std::runtime_error("cannot write " + path);
            return r;
        } else {
            throw std::runtime_error("unknown op '" + op + "'");
        }

        // Model-changing commands report the rebuild result and the new mass.
        RebuildResult rb = doc.rebuild();
        r["rebuild_ok"] = rb.ok;
        if (!rb.ok) r["rebuild_errors"] = rb.errors;
        MassProperties m = doc.massProperties();
        r["volume_mm3"] = m.volume;
        r["mass_g"] = m.mass;
        if (op == "mass_properties") {
            r["area_mm2"] = m.area;
            r["centre_of_mass"] = {m.cx, m.cy, m.cz};
            r["material"] = doc.material.name;
        }
        return r;
    } catch (const std::exception& e) {
        return {{"ok", false}, {"error", e.what()}};
    }
}

}  // namespace stawn

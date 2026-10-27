// STAWN CAD Pro - part document and feature regeneration on OpenCascade.
#include "stawn/Document.hpp"

#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepExtrema_DistShapeShape.hxx>
#include <BRepFilletAPI_MakeChamfer.hxx>
#include <BRepFilletAPI_MakeFillet.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepTools.hxx>
#include <BRep_Builder.hxx>
#include <STEPControl_Reader.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <GProp_GProps.hxx>
#include <STEPControl_Writer.hxx>
#include <ShapeAnalysis_FreeBounds.hxx>
#include <Standard_Failure.hxx>
#include <StlAPI_Writer.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_HSequenceOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <gp_Circ.hxx>

#include <algorithm>
#include <cmath>
#include <fstream>
#include <sstream>

namespace stawn {

namespace {
constexpr double kPi = 3.14159265358979323846;
}

const char* toString(StandardPlane p) {
    switch (p) {
        case StandardPlane::Front: return "Front";
        case StandardPlane::Top: return "Top";
        case StandardPlane::Right: return "Right";
    }
    return "?";
}

std::optional<StandardPlane> planeFromString(const std::string& s) {
    if (s == "Front") return StandardPlane::Front;
    if (s == "Top") return StandardPlane::Top;
    if (s == "Right") return StandardPlane::Right;
    return std::nullopt;
}

// Front looks along -Z with X right and Y up, Top looks down -Y with X right and -Z up,
// Right looks along -X with -Z right and Y up.
gp_Ax3 planeAxes(StandardPlane p) {
    switch (p) {
        case StandardPlane::Front: return gp_Ax3(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1), gp_Dir(1, 0, 0));
        case StandardPlane::Top: return gp_Ax3(gp_Pnt(0, 0, 0), gp_Dir(0, 1, 0), gp_Dir(1, 0, 0));
        case StandardPlane::Right: return gp_Ax3(gp_Pnt(0, 0, 0), gp_Dir(1, 0, 0), gp_Dir(0, 0, -1));
    }
    return gp_Ax3();
}

const char* toString(FeatureType t) {
    switch (t) {
        case FeatureType::Sketch: return "Sketch";
        case FeatureType::Extrude: return "Extrude";
        case FeatureType::Revolve: return "Revolve";
        case FeatureType::Fillet: return "Fillet";
        case FeatureType::Chamfer: return "Chamfer";
        case FeatureType::Import: return "Import";
    }
    return "?";
}

const std::vector<Material>& materialLibrary() {
    // Densities in kg/m^3.
    static const std::vector<Material> lib = {
        {"1.7147 (20MnCr5)", 7850},
        {"1.7131 (16MnCr5)", 7850},
        {"1.6587 (18CrNiMo7-6)", 7850},
        {"1.7225 (42CrMo4)", 7850},
        {"1.0503 (C45)", 7850},
        {"EN8 (080M40)", 7850},
        {"EN19 (709M40)", 7850},
        {"1.4301 (AISI 304)", 8000},
        {"1.4404 (AISI 316L)", 8000},
        {"Grey Cast Iron (EN-GJL-250)", 7200},
        {"Ductile Iron (EN-GJS-500-7)", 7100},
        {"Aluminium 6061-T6", 2700},
        {"Aluminium 7075-T6", 2810},
        {"Brass CuZn39Pb3", 8470},
        {"Copper Cu-ETP", 8930},
        {"Titanium Ti-6Al-4V", 4430},
        {"POM (Acetal)", 1410},
        {"PA66 (Nylon)", 1140},
    };
    return lib;
}

std::optional<Material> findMaterial(const std::string& name) {
    for (auto& m : materialLibrary())
        if (m.name == name) return m;
    return std::nullopt;
}

PartDocument::PartDocument() { configurations.push_back({"Default"}); }

std::string PartDocument::uniqueName(const std::string& base) const {
    for (int i = 1;; ++i) {
        std::string candidate = base + std::to_string(i);
        if (!feature(candidate)) return candidate;
    }
}

Feature& PartDocument::addSketch(StandardPlane plane, const std::string& name) {
    Feature f;
    f.type = FeatureType::Sketch;
    f.name = name.empty() ? uniqueName("Sketch") : name;
    f.plane = plane;
    features.push_back(std::move(f));
    return features.back();
}

Feature& PartDocument::addExtrude(const std::string& sketch, double depth, Operation op, const std::string& name) {
    Feature f;
    f.type = FeatureType::Extrude;
    f.name = name.empty() ? uniqueName(op == Operation::Cut ? "Cut-Extrude" : "Boss-Extrude") : name;
    f.sketchName = sketch;
    f.depth = depth;
    f.operation = op;
    features.push_back(std::move(f));
    return features.back();
}

Feature& PartDocument::addRevolve(const std::string& sketch, int axisLine, double angle, Operation op,
                                  const std::string& name) {
    Feature f;
    f.type = FeatureType::Revolve;
    f.name = name.empty() ? uniqueName(op == Operation::Cut ? "Cut-Revolve" : "Revolve") : name;
    f.sketchName = sketch;
    f.axisLine = axisLine;
    f.angle = angle;
    f.operation = op;
    features.push_back(std::move(f));
    return features.back();
}

Feature& PartDocument::addFillet(std::vector<EdgeRef> edges, double radius, const std::string& name) {
    Feature f;
    f.type = FeatureType::Fillet;
    f.name = name.empty() ? uniqueName("Fillet") : name;
    f.edges = std::move(edges);
    f.radius = radius;
    features.push_back(std::move(f));
    return features.back();
}

Feature& PartDocument::addChamfer(std::vector<EdgeRef> edges, double distance, const std::string& name) {
    Feature f;
    f.type = FeatureType::Chamfer;
    f.name = name.empty() ? uniqueName("Chamfer") : name;
    f.edges = std::move(edges);
    f.radius = distance;
    features.push_back(std::move(f));
    return features.back();
}

Feature* PartDocument::importSTEP(const std::string& path, std::string* error) {
    STEPControl_Reader reader;
    if (reader.ReadFile(path.c_str()) != IFSelect_RetDone) {
        if (error) *error = "Cannot read STEP file " + path;
        return nullptr;
    }
    reader.TransferRoots();
    TopoDS_Shape shape = reader.OneShape();
    if (shape.IsNull()) {
        if (error) *error = "The STEP file has no geometry";
        return nullptr;
    }
    std::ostringstream os;
    BRepTools::Write(shape, os);
    Feature f;
    f.type = FeatureType::Import;
    f.name = uniqueName("Imported");
    auto slash = path.find_last_of("/\\");
    f.sourceFile = slash == std::string::npos ? path : path.substr(slash + 1);
    f.brep = os.str();
    features.push_back(std::move(f));
    return &features.back();
}

Feature* PartDocument::feature(const std::string& n) {
    for (auto& f : features)
        if (f.name == n) return &f;
    return nullptr;
}

const Feature* PartDocument::feature(const std::string& n) const {
    for (auto& f : features)
        if (f.name == n) return &f;
    return nullptr;
}

bool PartDocument::renameFeature(const std::string& from, const std::string& to) {
    Feature* f = feature(from);
    if (!f || to.empty() || feature(to)) return false;
    f->name = to;
    for (auto& g : features)
        if (g.sketchName == from) g.sketchName = to;
    auto renameKey = [&](std::map<std::string, double>& dims) {
        std::map<std::string, double> out;
        for (auto& [k, v] : dims) {
            auto at = k.find('@');
            if (at != std::string::npos && k.substr(at + 1) == from)
                out[k.substr(0, at) + "@" + to] = v;
            else
                out[k] = v;
        }
        dims = std::move(out);
    };
    for (auto& c : configurations) {
        renameKey(c.dimensions);
        if (c.suppressedFeatures.erase(from)) c.suppressedFeatures.insert(to);
    }
    return true;
}

bool PartDocument::deleteFeature(const std::string& n) {
    auto it = std::find_if(features.begin(), features.end(), [&](const Feature& f) { return f.name == n; });
    if (it == features.end()) return false;
    features.erase(it);
    return true;
}

Configuration& PartDocument::configuration(const std::string& n) {
    if (auto* c = findConfiguration(n)) return *c;
    configurations.push_back({n});
    return configurations.back();
}

Configuration* PartDocument::findConfiguration(const std::string& n) {
    for (auto& c : configurations)
        if (c.name == n) return &c;
    return nullptr;
}

const Configuration* PartDocument::activeConfig() const {
    for (auto& c : configurations)
        if (c.name == activeConfiguration) return &c;
    return nullptr;
}

bool PartDocument::renameConfiguration(const std::string& from, const std::string& to) {
    auto* c = findConfiguration(from);
    if (!c || to.empty() || findConfiguration(to)) return false;
    c->name = to;
    if (activeConfiguration == from) activeConfiguration = to;
    return true;
}

namespace {

bool splitDimensionName(const std::string& full, std::string& dim, std::string& feat) {
    auto at = full.find('@');
    if (at == std::string::npos) return false;
    dim = full.substr(0, at);
    feat = full.substr(at + 1);
    return !dim.empty() && !feat.empty();
}

double* featureDimension(Feature& f, const std::string& dim) {
    if (dim != "D1") return nullptr;
    switch (f.type) {
        case FeatureType::Extrude: return &f.depth;
        case FeatureType::Revolve: return &f.angle;
        case FeatureType::Fillet:
        case FeatureType::Chamfer: return &f.radius;
        default: return nullptr;
    }
}

// Writes a dimension value straight into the model, ignoring configurations.
bool writeModelDimension(std::vector<Feature>& features, const std::string& full, double value) {
    std::string dim, feat;
    if (!splitDimensionName(full, dim, feat)) return false;
    for (auto& f : features) {
        if (f.name != feat) continue;
        if (f.type == FeatureType::Sketch) return f.sketch.setDimension(dim, value);
        if (double* d = featureDimension(f, dim)) {
            *d = value;
            return true;
        }
        return false;
    }
    return false;
}

}  // namespace

std::optional<double> PartDocument::getDimension(const std::string& full) const {
    if (auto* c = activeConfig()) {
        auto it = c->dimensions.find(full);
        if (it != c->dimensions.end()) return it->second;
    }
    std::string dim, feat;
    if (!splitDimensionName(full, dim, feat)) return std::nullopt;
    const Feature* f = feature(feat);
    if (!f) return std::nullopt;
    if (f->type == FeatureType::Sketch) return f->sketch.dimension(dim);
    if (double* d = featureDimension(const_cast<Feature&>(*f), dim)) return *d;
    return std::nullopt;
}

bool PartDocument::setDimension(const std::string& full, double value, bool thisConfigurationOnly) {
    if (!getDimension(full)) return false;
    auto* c = findConfiguration(activeConfiguration);
    if (c && (thisConfigurationOnly || c->dimensions.count(full))) {
        c->dimensions[full] = value;
        return true;
    }
    return writeModelDimension(features, full, value);
}

std::vector<std::pair<std::string, double>> PartDocument::listDimensions() const {
    std::vector<std::pair<std::string, double>> out;
    for (auto& f : features) {
        if (f.type == FeatureType::Sketch) {
            for (auto& c : f.sketch.constraints)
                if (c.isDimension()) out.push_back({c.name + "@" + f.name, *getDimension(c.name + "@" + f.name)});
        } else if (featureDimension(const_cast<Feature&>(f), "D1")) {
            out.push_back({"D1@" + f.name, *getDimension("D1@" + f.name)});
        }
    }
    return out;
}

bool PartDocument::isSuppressed(const Feature& f) const {
    if (auto* c = activeConfig()) return c->suppressedFeatures.count(f.name) > 0;
    return f.suppressed;
}

void PartDocument::setSuppressed(const std::string& name, bool suppressed, bool thisConfigurationOnly) {
    if (thisConfigurationOnly) {
        if (auto* c = findConfiguration(activeConfiguration)) {
            if (suppressed)
                c->suppressedFeatures.insert(name);
            else
                c->suppressedFeatures.erase(name);
        }
        return;
    }
    for (auto& c : configurations) {
        if (suppressed)
            c.suppressedFeatures.insert(name);
        else
            c.suppressedFeatures.erase(name);
    }
}

namespace {

gp_Pnt toWorld(const gp_Ax3& ax, double u, double v) {
    gp_XYZ p = ax.Location().XYZ() + ax.XDirection().XYZ() * u + ax.YDirection().XYZ() * v;
    return gp_Pnt(p);
}

// Builds the planar face (with holes) enclosed by the sketch's profile geometry.
TopoDS_Shape profileFace(const Feature& sk, std::string& error) {
    gp_Ax3 ax = planeAxes(sk.plane);
    const Sketch& s = sk.sketch;
    Handle(TopTools_HSequenceOfShape) edges = new TopTools_HSequenceOfShape;
    std::vector<TopoDS_Wire> wires;

    for (auto& l : s.lines) {
        if (l.construction) continue;
        gp_Pnt a = toWorld(ax, s.points[l.p1].x, s.points[l.p1].y);
        gp_Pnt b = toWorld(ax, s.points[l.p2].x, s.points[l.p2].y);
        if (a.Distance(b) < 1e-9) continue;
        edges->Append(BRepBuilderAPI_MakeEdge(a, b).Edge());
    }
    for (auto& a : s.arcs) {
        if (a.construction) continue;
        auto& c = s.points[a.center];
        auto& st = s.points[a.start];
        auto& en = s.points[a.end];
        double r = std::hypot(st.x - c.x, st.y - c.y);
        gp_Circ circ(gp_Ax2(toWorld(ax, c.x, c.y), ax.Direction(), ax.XDirection()), r);
        Handle(Geom_TrimmedCurve) arc =
            GC_MakeArcOfCircle(circ, toWorld(ax, st.x, st.y), toWorld(ax, en.x, en.y), true).Value();
        edges->Append(BRepBuilderAPI_MakeEdge(arc).Edge());
    }
    for (auto& c : s.circles) {
        if (c.construction) continue;
        auto& ctr = s.points[c.center];
        gp_Circ circ(gp_Ax2(toWorld(ax, ctr.x, ctr.y), ax.Direction(), ax.XDirection()), c.radius);
        TopoDS_Edge e = BRepBuilderAPI_MakeEdge(circ).Edge();
        wires.push_back(BRepBuilderAPI_MakeWire(e).Wire());
    }

    if (edges->Length() > 0) {
        Handle(TopTools_HSequenceOfShape) connected;
        ShapeAnalysis_FreeBounds::ConnectEdgesToWires(edges, 1e-5, Standard_False, connected);
        for (int i = 1; i <= connected->Length(); ++i) {
            TopoDS_Wire w = TopoDS::Wire(connected->Value(i));
            if (!w.Closed()) {
                error = "Sketch " + sk.name + " has an open contour";
                return {};
            }
            wires.push_back(w);
        }
    }
    if (wires.empty()) {
        error = "Sketch " + sk.name + " has no closed contour";
        return {};
    }

    struct Region {
        TopoDS_Face face;
        double area;
    };
    std::vector<Region> regions;
    for (auto& w : wires) {
        BRepBuilderAPI_MakeFace mf(w, Standard_True);
        if (!mf.IsDone()) continue;
        GProp_GProps g;
        BRepGProp::SurfaceProperties(mf.Face(), g);
        regions.push_back({mf.Face(), std::abs(g.Mass())});
    }
    if (regions.empty()) {
        error = "Sketch " + sk.name + " could not form a face";
        return {};
    }
    std::sort(regions.begin(), regions.end(), [](auto& a, auto& b) { return a.area > b.area; });

    // Larger regions come first: a smaller region inside the result becomes a hole,
    // a disjoint one becomes another island.
    TopoDS_Shape result = regions[0].face;
    for (size_t i = 1; i < regions.size(); ++i) {
        BRepAlgoAPI_Fuse probe(result, regions[i].face);
        GProp_GProps before, after;
        BRepGProp::SurfaceProperties(result, before);
        BRepGProp::SurfaceProperties(probe.Shape(), after);
        bool inside = std::abs(after.Mass() - before.Mass()) < 1e-6 * std::max(1.0, before.Mass());
        result = inside ? BRepAlgoAPI_Cut(result, regions[i].face).Shape() : probe.Shape();
    }
    return result;
}

TopoDS_Edge nearestEdge(const TopoDS_Shape& body, const EdgeRef& ref, double& distance) {
    TopoDS_Vertex v = BRepBuilderAPI_MakeVertex(gp_Pnt(ref.x, ref.y, ref.z));
    TopoDS_Edge best;
    distance = 1e300;
    for (TopExp_Explorer ex(body, TopAbs_EDGE); ex.More(); ex.Next()) {
        BRepExtrema_DistShapeShape d(v, ex.Current());
        if (d.IsDone() && d.Value() < distance) {
            distance = d.Value();
            best = TopoDS::Edge(ex.Current());
        }
    }
    return best;
}

}  // namespace

RebuildResult PartDocument::rebuild() {
    RebuildResult result;
    shape_.Nullify();

    // Work on a copy so configuration values never leak into the stored model. Errors are
    // cleared first so a failure from the last rebuild cannot carry over into this one.
    for (auto& f : features) f.error.clear();
    std::vector<Feature> work = features;
    if (auto* c = activeConfig())
        for (auto& [k, v] : c->dimensions) writeModelDimension(work, k, v);

    int last = rollbackIndex < 0 ? int(work.size()) : std::min<int>(rollbackIndex, int(work.size()));
    TopoDS_Shape body;

    for (int i = 0; i < last; ++i) {
        Feature& f = work[i];
        Feature& stored = features[i];
        if (isSuppressed(stored)) continue;
        try {
            if (f.type == FeatureType::Sketch) {
                SolveResult sr = f.sketch.solve();
                stored.sketch.points = f.sketch.points;  // keep solved positions for display
                stored.sketch.circles = f.sketch.circles;
                if (sr.status == SketchStatus::OverDefined || sr.status == SketchStatus::Failed)
                    f.error = std::string("Sketch is ") + toString(sr.status);
            } else if (f.type == FeatureType::Extrude || f.type == FeatureType::Revolve) {
                auto it = std::find_if(work.begin(), work.begin() + i,
                                       [&](const Feature& g) { return g.name == f.sketchName; });
                if (it == work.begin() + i) {
                    f.error = "Missing sketch " + f.sketchName;
                } else {
                    std::string err;
                    TopoDS_Shape face = profileFace(*it, err);
                    if (face.IsNull()) {
                        f.error = err;
                    } else {
                        TopoDS_Shape tool;
                        gp_Ax3 ax = planeAxes(it->plane);
                        if (f.type == FeatureType::Extrude) {
                            gp_Vec dir(ax.Direction());
                            if (f.reverse) dir.Reverse();
                            if (f.midPlane) {
                                gp_Trsf t;
                                t.SetTranslation(dir * (-f.depth / 2));
                                face = BRepBuilderAPI_Transform(face, t, true).Shape();
                            }
                            tool = BRepPrimAPI_MakePrism(face, dir * f.depth).Shape();
                        } else {
                            const Sketch& s = it->sketch;
                            if (f.axisLine < 0 || f.axisLine >= int(s.lines.size())) {
                                f.error = "Revolve needs an axis line";
                            } else {
                                auto& l = s.lines[f.axisLine];
                                gp_Pnt a = toWorld(ax, s.points[l.p1].x, s.points[l.p1].y);
                                gp_Pnt b = toWorld(ax, s.points[l.p2].x, s.points[l.p2].y);
                                tool = BRepPrimAPI_MakeRevol(face, gp_Ax1(a, gp_Dir(gp_Vec(a, b))),
                                                             f.angle * kPi / 180.0)
                                           .Shape();
                            }
                        }
                        if (!tool.IsNull()) {
                            if (f.operation == Operation::Boss)
                                body = body.IsNull() ? tool : BRepAlgoAPI_Fuse(body, tool).Shape();
                            else if (body.IsNull())
                                f.error = "Nothing to cut";
                            else
                                body = BRepAlgoAPI_Cut(body, tool).Shape();
                        }
                    }
                }
            } else if (f.type == FeatureType::Import) {
                TopoDS_Shape imported;
                BRep_Builder builder;
                std::istringstream is(f.brep);
                BRepTools::Read(imported, is, builder);
                if (imported.IsNull())
                    f.error = "Imported body is empty";
                else
                    body = body.IsNull() ? imported : BRepAlgoAPI_Fuse(body, imported).Shape();
            } else if (f.type == FeatureType::Fillet || f.type == FeatureType::Chamfer) {
                if (body.IsNull()) {
                    f.error = "No body to modify";
                } else {
                    std::vector<TopoDS_Edge> picked;
                    for (auto& ref : f.edges) {
                        double d;
                        TopoDS_Edge e = nearestEdge(body, ref, d);
                        if (e.IsNull() || d > 0.5) {
                            f.error = "Missing edge reference";
                            break;
                        }
                        picked.push_back(e);
                    }
                    if (f.error.empty() && !picked.empty()) {
                        if (f.type == FeatureType::Fillet) {
                            BRepFilletAPI_MakeFillet mk(body);
                            for (auto& e : picked) mk.Add(f.radius, e);
                            mk.Build();
                            if (mk.IsDone()) body = mk.Shape();
                            else f.error = "Fillet failed";
                        } else {
                            BRepFilletAPI_MakeChamfer mk(body);
                            for (auto& e : picked) mk.Add(f.radius, e);
                            mk.Build();
                            if (mk.IsDone()) body = mk.Shape();
                            else f.error = "Chamfer failed";
                        }
                    }
                }
            }
        } catch (const Standard_Failure& e) {
            f.error = std::string("Geometry error: ") + (e.GetMessageString() ? e.GetMessageString() : "");
        }
        if (!f.error.empty()) {
            stored.error = f.error;
            result.ok = false;
            result.errors.push_back(f.name + ": " + f.error);
        }
    }
    shape_ = body;
    return result;
}

MassProperties PartDocument::massProperties() const {
    MassProperties m;
    if (shape_.IsNull()) return m;
    GProp_GProps v, s;
    BRepGProp::VolumeProperties(shape_, v);
    BRepGProp::SurfaceProperties(shape_, s);
    m.volume = v.Mass();
    m.area = s.Mass();
    m.mass = m.volume * material.density * 1e-6;  // mm^3 * kg/m^3 -> g
    gp_Pnt c = v.CentreOfMass();
    m.cx = c.X();
    m.cy = c.Y();
    m.cz = c.Z();
    return m;
}

std::string PartDocument::expand(const std::string& value) {
    std::string out;
    size_t i = 0;
    std::optional<MassProperties> mp;
    auto fmt = [](double d, int prec) {
        std::ostringstream os;
        os.setf(std::ios::fixed);
        os.precision(prec);
        os << d;
        return os.str();
    };
    while (i < value.size()) {
        auto start = value.find("${", i);
        if (start == std::string::npos) {
            out += value.substr(i);
            break;
        }
        auto end = value.find('}', start);
        if (end == std::string::npos) {
            out += value.substr(i);
            break;
        }
        out += value.substr(i, start - i);
        std::string key = value.substr(start + 2, end - start - 2);
        auto mass = [&]() -> const MassProperties& {
            if (!mp) mp = massProperties();
            return *mp;
        };
        if (key == "Mass") out += fmt(mass().mass, 2);
        else if (key == "Volume") out += fmt(mass().volume, 2);
        else if (key == "Material") out += material.name;
        else if (key == "Density") out += fmt(material.density, 0);
        else if (key == "PartName") out += name;
        else if (key == "Configuration") out += activeConfiguration;
        else if (key == "Description") out += activeConfig() ? activeConfig()->description : "";
        else out += value.substr(start, end - start + 1);
        i = end + 1;
    }
    return out;
}

std::string PartDocument::property(const std::string& key) {
    if (auto* c = activeConfig()) {
        auto it = c->properties.find(key);
        if (it != c->properties.end()) return expand(it->second);
    }
    auto it = properties.find(key);
    return it == properties.end() ? std::string() : expand(it->second);
}

std::map<std::string, std::string> PartDocument::resolvedProperties() {
    std::map<std::string, std::string> out;
    for (auto& [k, v] : properties) out[k] = property(k);
    if (auto* c = activeConfig())
        for (auto& [k, v] : c->properties) out[k] = property(k);
    return out;
}

bool PartDocument::exportSTEP(const std::string& path) const {
    if (shape_.IsNull()) return false;
    STEPControl_Writer w;
    if (w.Transfer(shape_, STEPControl_AsIs) != IFSelect_RetDone) return false;
    return w.Write(path.c_str()) == IFSelect_RetDone;
}

bool PartDocument::exportSTL(const std::string& path, double deflection) const {
    if (shape_.IsNull()) return false;
    BRepMesh_IncrementalMesh mesh(shape_, deflection);
    return StlAPI_Writer().Write(shape_, path.c_str());
}

bool PartDocument::save(const std::string& path) const {
    std::ofstream out(path);
    if (!out) return false;
    out << toJSON();
    return bool(out);
}

std::unique_ptr<PartDocument> PartDocument::load(const std::string& path, std::string* error) {
    std::ifstream in(path);
    if (!in) {
        if (error) *error = "Cannot open " + path;
        return nullptr;
    }
    std::stringstream ss;
    ss << in.rdbuf();
    return fromJSON(ss.str(), error);
}

}  // namespace stawn

// STAWN CAD Pro - .stprt part file format (JSON).
//
// {
//   "format": "stawn.part", "version": 1, "name": "...", "units": "MMGS",
//   "material": {"name": "...", "density": 7850},
//   "features": [ {"name": "Sketch1", "type": "Sketch", "plane": "Front", "sketch": {...}}, ... ],
//   "configurations": [ {"name": "Default", "suppressed": [], "dimensions": {}, "properties": {}} ],
//   "activeConfiguration": "Default", "properties": {"PartNo": "..."}
// }
#include <stdexcept>

#include "stawn/Document.hpp"
#include "stawn/Json.hpp"

using nlohmann::json;

namespace stawn {

namespace {

const char* refKindName(RefKind k) {
    switch (k) {
        case RefKind::Point: return "point";
        case RefKind::Line: return "line";
        case RefKind::Circle: return "circle";
        case RefKind::Arc: return "arc";
    }
    return "point";
}

RefKind refKindFrom(const std::string& s) {
    if (s == "line") return RefKind::Line;
    if (s == "circle") return RefKind::Circle;
    if (s == "arc") return RefKind::Arc;
    return RefKind::Point;
}

std::optional<FeatureType> featureTypeFrom(const std::string& s) {
    for (auto t : {FeatureType::Sketch, FeatureType::Extrude, FeatureType::Revolve, FeatureType::Fillet,
                   FeatureType::Chamfer, FeatureType::Import})
        if (s == toString(t)) return t;
    return std::nullopt;
}

}  // namespace

json sketchToJson(const Sketch& s) {
    json j;
    j["points"] = json::array();
    for (auto& p : s.points) j["points"].push_back({{"x", p.x}, {"y", p.y}, {"fixed", p.fixed}});
    j["lines"] = json::array();
    for (auto& l : s.lines) j["lines"].push_back({{"p1", l.p1}, {"p2", l.p2}, {"construction", l.construction}});
    j["circles"] = json::array();
    for (auto& c : s.circles)
        j["circles"].push_back({{"center", c.center}, {"radius", c.radius}, {"construction", c.construction}});
    j["arcs"] = json::array();
    for (auto& a : s.arcs)
        j["arcs"].push_back(
            {{"center", a.center}, {"start", a.start}, {"end", a.end}, {"construction", a.construction}});
    j["constraints"] = json::array();
    for (auto& c : s.constraints) {
        json cj = {{"type", toString(c.type)}, {"a", c.a}, {"b", c.b}, {"kindA", refKindName(c.refKindA)},
                   {"kindB", refKindName(c.refKindB)}};
        if (c.isDimension() || c.type == ConstraintType::Fix) cj["value"] = c.value;
        if (c.type == ConstraintType::Fix) cj["value2"] = c.value2;
        if (!c.name.empty()) cj["name"] = c.name;
        j["constraints"].push_back(cj);
    }
    return j;
}

Sketch sketchFromJson(const json& j) {
    Sketch s;
    s.points.clear();
    for (auto& p : j.at("points")) s.points.push_back({p.at("x"), p.at("y"), p.value("fixed", false)});
    if (s.points.empty()) s.addPoint(0, 0, true);
    for (auto& l : j.value("lines", json::array())) s.addLine(int(l.at("p1")), int(l.at("p2")), l.value("construction", false));
    for (auto& c : j.value("circles", json::array()))
        s.addCircle(c.at("center"), c.at("radius"), c.value("construction", false));
    for (auto& a : j.value("arcs", json::array()))
        s.addArc(a.at("center"), a.at("start"), a.at("end"), a.value("construction", false));
    for (auto& cj : j.value("constraints", json::array())) {
        auto type = constraintTypeFromString(cj.at("type"));
        if (!type) throw std::runtime_error("Unknown constraint type " + cj.at("type").get<std::string>());
        SketchConstraint c;
        c.type = *type;
        c.a = cj.value("a", -1);
        c.b = cj.value("b", -1);
        c.refKindA = refKindFrom(cj.value("kindA", "point"));
        c.refKindB = refKindFrom(cj.value("kindB", "point"));
        c.value = cj.value("value", 0.0);
        c.value2 = cj.value("value2", 0.0);
        c.name = cj.value("name", "");
        s.addConstraint(c);
    }
    return s;
}

std::string PartDocument::toJSON() const {
    json j;
    j["format"] = "stawn.part";
    j["version"] = 1;
    j["name"] = name;
    j["units"] = units;
    j["material"] = {{"name", material.name}, {"density", material.density}};
    j["features"] = json::array();
    for (auto& f : features) {
        json fj = {{"name", f.name}, {"type", toString(f.type)}};
        if (f.suppressed) fj["suppressed"] = true;
        switch (f.type) {
            case FeatureType::Sketch:
                fj["plane"] = toString(f.plane);
                fj["sketch"] = sketchToJson(f.sketch);
                break;
            case FeatureType::Extrude:
                fj["sketchName"] = f.sketchName;
                fj["operation"] = f.operation == Operation::Cut ? "cut" : "boss";
                fj["depth"] = f.depth;
                fj["midPlane"] = f.midPlane;
                fj["reverse"] = f.reverse;
                break;
            case FeatureType::Revolve:
                fj["sketchName"] = f.sketchName;
                fj["operation"] = f.operation == Operation::Cut ? "cut" : "boss";
                fj["angle"] = f.angle;
                fj["axisLine"] = f.axisLine;
                break;
            case FeatureType::Fillet:
            case FeatureType::Chamfer:
                fj[f.type == FeatureType::Fillet ? "radius" : "distance"] = f.radius;
                fj["edges"] = json::array();
                for (auto& e : f.edges) fj["edges"].push_back({e.x, e.y, e.z});
                break;
            case FeatureType::Import:
                fj["sourceFile"] = f.sourceFile;
                fj["brep"] = f.brep;
                break;
        }
        j["features"].push_back(fj);
    }
    j["configurations"] = json::array();
    for (auto& c : configurations) {
        j["configurations"].push_back({{"name", c.name},
                                       {"description", c.description},
                                       {"bomPartNumber", c.bomPartNumber},
                                       {"suppressed", c.suppressedFeatures},
                                       {"dimensions", c.dimensions},
                                       {"properties", c.properties}});
    }
    j["activeConfiguration"] = activeConfiguration;
    j["properties"] = properties;
    return j.dump(2);
}

std::unique_ptr<PartDocument> PartDocument::fromJSON(const std::string& text, std::string* error) {
    try {
        json j = json::parse(text);
        if (j.value("format", "") != "stawn.part") throw std::runtime_error("Not a STAWN part file");
        auto doc = std::make_unique<PartDocument>();
        doc->name = j.value("name", "Part1");
        doc->units = j.value("units", "MMGS");
        if (j.contains("material"))
            doc->material = {j["material"].value("name", ""), j["material"].value("density", 7850.0)};
        for (auto& fj : j.value("features", json::array())) {
            auto type = featureTypeFrom(fj.at("type"));
            if (!type) throw std::runtime_error("Unknown feature type " + fj.at("type").get<std::string>());
            Feature f;
            f.type = *type;
            f.name = fj.at("name");
            f.suppressed = fj.value("suppressed", false);
            switch (f.type) {
                case FeatureType::Sketch:
                    f.plane = planeFromString(fj.value("plane", "Front")).value_or(StandardPlane::Front);
                    f.sketch = sketchFromJson(fj.at("sketch"));
                    break;
                case FeatureType::Extrude:
                    f.sketchName = fj.at("sketchName");
                    f.operation = fj.value("operation", "boss") == "cut" ? Operation::Cut : Operation::Boss;
                    f.depth = fj.value("depth", 10.0);
                    f.midPlane = fj.value("midPlane", false);
                    f.reverse = fj.value("reverse", false);
                    break;
                case FeatureType::Revolve:
                    f.sketchName = fj.at("sketchName");
                    f.operation = fj.value("operation", "boss") == "cut" ? Operation::Cut : Operation::Boss;
                    f.angle = fj.value("angle", 360.0);
                    f.axisLine = fj.value("axisLine", -1);
                    break;
                case FeatureType::Fillet:
                case FeatureType::Chamfer:
                    f.radius = fj.value(f.type == FeatureType::Fillet ? "radius" : "distance", 1.0);
                    for (auto& e : fj.value("edges", json::array())) f.edges.push_back({e.at(0), e.at(1), e.at(2)});
                    break;
                case FeatureType::Import:
                    f.sourceFile = fj.value("sourceFile", "");
                    f.brep = fj.value("brep", "");
                    break;
            }
            doc->features.push_back(std::move(f));
        }
        if (j.contains("configurations")) {
            doc->configurations.clear();
            for (auto& cj : j["configurations"]) {
                Configuration c;
                c.name = cj.at("name");
                c.description = cj.value("description", "");
                c.bomPartNumber = cj.value("bomPartNumber", "");
                c.suppressedFeatures = cj.value("suppressed", std::set<std::string>{});
                c.dimensions = cj.value("dimensions", std::map<std::string, double>{});
                c.properties = cj.value("properties", std::map<std::string, std::string>{});
                doc->configurations.push_back(std::move(c));
            }
            if (doc->configurations.empty()) doc->configurations.push_back({"Default"});
        }
        doc->activeConfiguration = j.value("activeConfiguration", doc->configurations.front().name);
        doc->properties = j.value("properties", std::map<std::string, std::string>{});
        return doc;
    } catch (const std::exception& e) {
        if (error) *error = e.what();
        return nullptr;
    }
}

}  // namespace stawn

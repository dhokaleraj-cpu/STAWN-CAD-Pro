#include "stawn_c.h"

#include <cstdlib>
#include <cstring>

#include "stawn/Document.hpp"
#include "stawn/Drawing.hpp"
#include "stawn/Json.hpp"
#include "stawn/Mesh.hpp"
#include "stawn/Samples.hpp"

using nlohmann::json;
using namespace stawn;

struct StawnDoc {
    std::unique_ptr<PartDocument> doc;
};

namespace {

char* dup(const std::string& s) {
    char* out = static_cast<char*>(std::malloc(s.size() + 1));
    std::memcpy(out, s.c_str(), s.size() + 1);
    return out;
}

void setError(char** error, const std::string& msg) {
    if (error) *error = dup(msg);
}

template <typename T>
T* copyArray(const std::vector<T>& v) {
    if (v.empty()) return nullptr;
    T* out = static_cast<T*>(std::malloc(v.size() * sizeof(T)));
    std::memcpy(out, v.data(), v.size() * sizeof(T));
    return out;
}

}  // namespace

extern "C" {

StawnDoc* stawn_doc_new(void) { return new StawnDoc{std::make_unique<PartDocument>()}; }

StawnDoc* stawn_doc_new_sample_shaft(void) {
    auto* d = new StawnDoc{makeSampleShaft()};
    d->doc->rebuild();
    return d;
}

StawnDoc* stawn_doc_open(const char* path, char** error) {
    std::string err;
    auto doc = PartDocument::load(path, &err);
    if (!doc) {
        setError(error, err);
        return nullptr;
    }
    doc->rebuild();
    return new StawnDoc{std::move(doc)};
}

StawnDoc* stawn_doc_from_json(const char* text, char** error) {
    std::string err;
    auto doc = PartDocument::fromJSON(text, &err);
    if (!doc) {
        setError(error, err);
        return nullptr;
    }
    return new StawnDoc{std::move(doc)};
}

void stawn_doc_free(StawnDoc* doc) { delete doc; }

int stawn_doc_save(StawnDoc* d, const char* path) { return d->doc->save(path) ? 1 : 0; }

char* stawn_doc_json(StawnDoc* d) { return dup(d->doc->toJSON()); }

int stawn_doc_set_json(StawnDoc* d, const char* text, char** error) {
    std::string err;
    auto doc = PartDocument::fromJSON(text, &err);
    if (!doc) {
        setError(error, err);
        return 0;
    }
    d->doc = std::move(doc);
    return 1;
}

int stawn_doc_rebuild(StawnDoc* d, char** errors) {
    RebuildResult r = d->doc->rebuild();
    if (errors) *errors = dup(json(r.errors).dump());
    return r.ok ? 1 : 0;
}

int stawn_doc_set_active_configuration(StawnDoc* d, const char* name) {
    if (!d->doc->findConfiguration(name)) return 0;
    d->doc->activeConfiguration = name;
    return 1;
}

int stawn_doc_set_dimension(StawnDoc* d, const char* fullName, double value, int thisConfigurationOnly) {
    return d->doc->setDimension(fullName, value, thisConfigurationOnly != 0) ? 1 : 0;
}

int stawn_doc_get_dimension(StawnDoc* d, const char* fullName, double* value) {
    auto v = d->doc->getDimension(fullName);
    if (!v) return 0;
    *value = *v;
    return 1;
}

char* stawn_doc_resolved_properties(StawnDoc* d) { return dup(json(d->doc->resolvedProperties()).dump()); }

StawnMassProperties stawn_doc_mass_properties(StawnDoc* d) {
    MassProperties m = d->doc->massProperties();
    return {m.volume, m.area, m.mass, m.cx, m.cy, m.cz};
}

StawnMesh* stawn_doc_mesh(StawnDoc* d, double deflection) {
    MeshData m = tessellate(d->doc->shape(), deflection > 0 ? deflection : 0.02);
    auto* out = static_cast<StawnMesh*>(std::calloc(1, sizeof(StawnMesh)));
    out->positions = copyArray(m.positions);
    out->normals = copyArray(m.normals);
    out->indices = copyArray(m.indices);
    out->triangleFace = copyArray(m.triangleFace);
    out->edgeLines = copyArray(m.edgeLines);
    out->segmentEdge = copyArray(m.segmentEdge);
    out->vertexCount = int32_t(m.positions.size() / 3);
    out->triangleCount = int32_t(m.indices.size() / 3);
    out->segmentCount = int32_t(m.segmentEdge.size());
    std::memcpy(out->bounds, m.bounds, sizeof m.bounds);
    return out;
}

void stawn_mesh_free(StawnMesh* m) {
    if (!m) return;
    std::free(m->positions);
    std::free(m->normals);
    std::free(m->indices);
    std::free(m->triangleFace);
    std::free(m->edgeLines);
    std::free(m->segmentEdge);
    std::free(m);
}

int stawn_doc_export_step(StawnDoc* d, const char* path) { return d->doc->exportSTEP(path) ? 1 : 0; }
int stawn_doc_export_stl(StawnDoc* d, const char* path) { return d->doc->exportSTL(path) ? 1 : 0; }

int stawn_doc_export_drawing(StawnDoc* d, const char* paper, const char* format, const char* path) {
    DrawingSpec spec = DrawingSpec::standardThreeView(*d->doc, paper ? paper : "A3");
    Sheet2D sheet = renderDrawing(*d->doc, spec);
    std::string f = format ? format : "pdf";
    if (f == "pdf") return exportPDF(sheet, path) ? 1 : 0;
    if (f == "dxf") return exportDXF(sheet, path) ? 1 : 0;
    if (f == "svg") return exportSVG(sheet, path) ? 1 : 0;
    return 0;
}

char* stawn_doc_drawing_svg(StawnDoc* d, const char* paper) {
    DrawingSpec spec = DrawingSpec::standardThreeView(*d->doc, paper ? paper : "A3");
    return dup(toSVG(renderDrawing(*d->doc, spec)));
}

char* stawn_sketch_solve(const char* sketchJson) {
    try {
        Sketch s = sketchFromJson(json::parse(sketchJson));
        SolveResult r = s.solve();
        return dup(json{{"sketch", sketchToJson(s)},
                        {"status", toString(r.status)},
                        {"dof", r.degreesOfFreedom},
                        {"residual", r.residual}}
                       .dump());
    } catch (const std::exception& e) {
        return dup(json{{"status", "Failed"}, {"error", e.what()}}.dump());
    }
}

char* stawn_material_library(void) {
    json j = json::array();
    for (auto& m : materialLibrary()) j.push_back({{"name", m.name}, {"density", m.density}});
    return dup(j.dump());
}

void stawn_free_string(char* s) { std::free(s); }

}  // extern "C"

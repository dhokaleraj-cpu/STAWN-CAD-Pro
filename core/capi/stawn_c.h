/* STAWN CAD Pro - C interface to the core, used by the Swift app.
 *
 * Edits go through JSON: the app reads the document with stawn_doc_json, changes the
 * feature list, configurations or properties, and writes it back with stawn_doc_set_json,
 * then calls stawn_doc_rebuild. Strings returned by these functions are owned by the caller
 * and must be released with stawn_free_string. */
#ifndef STAWN_C_H
#define STAWN_C_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct StawnDoc StawnDoc;

typedef struct {
    float* positions;     /* xyz per vertex */
    float* normals;       /* xyz per vertex */
    uint32_t* indices;    /* three per triangle */
    int32_t* triangleFace;
    float* edgeLines;     /* xyz xyz per segment */
    int32_t* segmentEdge;
    int32_t vertexCount;
    int32_t triangleCount;
    int32_t segmentCount;
    float bounds[6];
} StawnMesh;

typedef struct {
    double volume, area, mass, cx, cy, cz;
} StawnMassProperties;

StawnDoc* stawn_doc_new(void);
StawnDoc* stawn_doc_new_sample_shaft(void);
StawnDoc* stawn_doc_open(const char* path, char** error);
StawnDoc* stawn_doc_from_json(const char* json, char** error);
void stawn_doc_free(StawnDoc* doc);
int stawn_doc_save(StawnDoc* doc, const char* path);

char* stawn_doc_json(StawnDoc* doc);
int stawn_doc_set_json(StawnDoc* doc, const char* json, char** error);

/* Rebuilds the model. Returns 1 when every feature rebuilt. errors gets a JSON array of
 * "Feature: message" strings (may be NULL). */
int stawn_doc_rebuild(StawnDoc* doc, char** errors);

int stawn_doc_set_active_configuration(StawnDoc* doc, const char* name);
int stawn_doc_set_dimension(StawnDoc* doc, const char* fullName, double value, int thisConfigurationOnly);
int stawn_doc_get_dimension(StawnDoc* doc, const char* fullName, double* value);
/* JSON object of resolved custom properties for the active configuration. */
char* stawn_doc_resolved_properties(StawnDoc* doc);
StawnMassProperties stawn_doc_mass_properties(StawnDoc* doc);

StawnMesh* stawn_doc_mesh(StawnDoc* doc, double deflection);
void stawn_mesh_free(StawnMesh* mesh);

/* Adds a STEP file as an imported body. Returns 1 on success. */
int stawn_doc_import_step(StawnDoc* doc, const char* path, char** error);
int stawn_doc_export_step(StawnDoc* doc, const char* path);
int stawn_doc_export_stl(StawnDoc* doc, const char* path);

/* Drawings: paper is "A4".."A0", format is "pdf", "dxf" or "svg". An empty spec uses the
 * standard three-view layout. */
int stawn_doc_export_drawing(StawnDoc* doc, const char* paper, const char* format, const char* path);
char* stawn_doc_drawing_svg(StawnDoc* doc, const char* paper);

/* Solves a sketch given as JSON and returns {"sketch": {...}, "status": "...", "dof": n}. */
char* stawn_sketch_solve(const char* sketchJson);

/* JSON array of {"name", "density"} from the material library. */
char* stawn_material_library(void);

void stawn_free_string(char* s);

#ifdef __cplusplus
}
#endif

#endif

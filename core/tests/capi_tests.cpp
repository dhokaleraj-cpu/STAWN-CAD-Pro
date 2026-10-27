#include <doctest/doctest.h>

#include <nlohmann/json.hpp>
#include <string>

#include "stawn_c.h"

TEST_CASE("C interface: sample, mesh, configuration switch and JSON edit") {
    StawnDoc* d = stawn_doc_new_sample_shaft();
    StawnMesh* m = stawn_doc_mesh(d, 0.02);
    CHECK(m->triangleCount > 100);
    CHECK(m->segmentCount > 10);
    CHECK(m->bounds[3] == doctest::Approx(46.9).epsilon(1e-3));
    stawn_mesh_free(m);

    double v = 0;
    CHECK(stawn_doc_get_dimension(d, "D10@Sketch1", &v));
    CHECK(v == doctest::Approx(46.9));
    CHECK(stawn_doc_set_active_configuration(d, "Forging"));
    CHECK_FALSE(stawn_doc_set_active_configuration(d, "Nope"));
    char* errors = nullptr;
    CHECK(stawn_doc_rebuild(d, &errors));
    stawn_free_string(errors);

    char* json = stawn_doc_json(d);
    auto j = nlohmann::json::parse(json);
    stawn_free_string(json);
    j["material"]["name"] = "Aluminium 6061-T6";
    j["material"]["density"] = 2700;
    char* err = nullptr;
    REQUIRE(stawn_doc_set_json(d, j.dump().c_str(), &err));
    REQUIRE(stawn_doc_rebuild(d, nullptr));
    StawnMassProperties mp = stawn_doc_mass_properties(d);
    CHECK(mp.mass == doctest::Approx(mp.volume * 2700e-6));

    char* props = stawn_doc_resolved_properties(d);
    CHECK(nlohmann::json::parse(props)["Material"] == "Aluminium 6061-T6");
    stawn_free_string(props);
    stawn_doc_free(d);
}

TEST_CASE("C interface: sketch solve") {
    std::string sketch = R"({"points":[{"x":0,"y":0,"fixed":true},{"x":7,"y":0.5}],
        "lines":[{"p1":0,"p2":1}],
        "constraints":[{"type":"horizontal","a":0,"kindA":"line"},
                       {"type":"length","a":0,"kindA":"line","value":12,"name":"D1"}]})";
    char* out = stawn_sketch_solve(sketch.c_str());
    auto j = nlohmann::json::parse(out);
    stawn_free_string(out);
    CHECK(j["status"] == "Fully Defined");
    CHECK(j["sketch"]["points"][1]["x"].get<double>() == doctest::Approx(12));
}

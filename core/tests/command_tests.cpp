#include <doctest/doctest.h>

#include <cmath>
#include <cstdio>

#include "stawn/Commands.hpp"
#include "stawn_c.h"

using nlohmann::json;
using namespace stawn;

namespace {
constexpr double kPi = 3.14159265358979323846;
}

TEST_CASE("commands: circle extruded and filleted on all edges") {
    CommandSession s;
    json results = s.run(json::parse(R"([
        {"op": "new_part", "name": "Disc"},
        {"op": "create_sketch", "plane": "Front"},
        {"op": "draw_circle", "cx": 0, "cy": 0, "r": 30},
        {"op": "close_sketch"},
        {"op": "extrude", "depth": 15},
        {"op": "fillet", "radius": 2, "edges": "all"}
    ])"));
    REQUIRE(results.size() == 6);
    for (auto& r : results) CHECK_MESSAGE(r["ok"].get<bool>(), r.dump());
    CHECK(results[4]["volume_mm3"].get<double>() == doctest::Approx(kPi * 900 * 15).epsilon(1e-6));
    CHECK(results[5]["edge_count"] == 2);
    CHECK(results[5]["rebuild_ok"] == true);
    CHECK(results[5]["volume_mm3"].get<double>() < kPi * 900 * 15);
}

TEST_CASE("commands: rectangle block with a hexagon cut and material") {
    CommandSession s;
    json r = s.run(json::parse(R"([
        {"op": "create_sketch", "plane": "Top"},
        {"op": "draw_rectangle", "x1": 0, "y1": 0, "x2": 60, "y2": 40},
        {"op": "extrude", "depth": 20},
        {"op": "create_sketch", "plane": "Top"},
        {"op": "draw_polygon", "cx": 30, "cy": 20, "r": 10, "sides": 6},
        {"op": "cut_extrude", "depth": 20},
        {"op": "set_material", "name": "Aluminium 6061-T6"},
        {"op": "mass_properties"}
    ])"));
    for (auto& x : r) REQUIRE_MESSAGE(x["ok"].get<bool>(), x.dump());
    double hex = 1.5 * std::sqrt(3.0) * 100;  // area of a regular hexagon with circumradius 10
    CHECK(r[7]["volume_mm3"].get<double>() == doctest::Approx(60 * 40 * 20 - hex * 20).epsilon(1e-6));
    CHECK(r[7]["mass_g"].get<double>() == doctest::Approx((60 * 40 * 20 - hex * 20) * 2700e-6).epsilon(1e-6));
    CHECK(r[7]["material"] == "Aluminium 6061-T6");
}

TEST_CASE("commands: revolved pin with dimensions, configuration and properties") {
    CommandSession s;
    json r = s.run(json::parse(R"([
        {"op": "create_sketch", "plane": "Front"},
        {"op": "draw_line", "x1": 0, "y1": 0, "x2": 0, "y2": 4},
        {"op": "draw_line", "x1": 0, "y1": 4, "x2": 30, "y2": 4},
        {"op": "draw_line", "x1": 30, "y1": 4, "x2": 30, "y2": 0},
        {"op": "draw_line", "x1": 30, "y1": 0, "x2": 0, "y2": 0},
        {"op": "add_relation", "type": "vertical", "a": 0, "kindA": "line"},
        {"op": "add_relation", "type": "horizontal", "a": 1, "kindA": "line"},
        {"op": "add_relation", "type": "vertical", "a": 2, "kindA": "line"},
        {"op": "add_relation", "type": "horizontal", "a": 3, "kindA": "line"},
        {"op": "add_dimension", "type": "length", "a": 1, "kindA": "line", "value": 30},
        {"op": "add_dimension", "type": "length", "a": 0, "kindA": "line", "value": 4},
        {"op": "close_sketch"},
        {"op": "revolve", "axis_line": 3},
        {"op": "chamfer", "distance": 0.5, "edges": "circular"},
        {"op": "set_property", "key": "Weight", "value": "${Mass}"},
        {"op": "add_configuration", "name": "Long"},
        {"op": "set_dimension", "name": "D1@Sketch1", "value": 50, "this_configuration_only": true},
        {"op": "list_configurations"}
    ])"));
    for (auto& x : r) REQUIRE_MESSAGE(x["ok"].get<bool>(), x.dump());
    CHECK(r[9]["dimension"] == "D1@Sketch1");
    CHECK(r[11]["status"] == "Fully Defined");
    CHECK(r[12]["volume_mm3"].get<double>() == doctest::Approx(kPi * 16 * 30).epsilon(1e-6));
    CHECK(r[13]["edge_count"] == 2);
    double longVolume = r[16]["volume_mm3"].get<double>();
    CHECK(longVolume > kPi * 16 * 49);
    // Known limitation: edge references are found by position, so the chamfer on the far end
    // loses its edge when this configuration moves that end from x = 30 to x = 50.
    CHECK(r[16]["rebuild_ok"] == false);

    json back = s.execute({{"op", "set_configuration"}, {"name", "Default"}});
    CHECK(back["rebuild_ok"] == true);  // a failure in another configuration does not stick
    CHECK(back["volume_mm3"].get<double>() < kPi * 16 * 30);
    json props = s.execute({{"op", "get_properties"}});
    CHECK(std::stod(props["properties"]["Weight"].get<std::string>()) == doctest::Approx(back["mass_g"].get<double>()).epsilon(0.01));
}

TEST_CASE("commands: errors are reported, not thrown") {
    CommandSession s;
    CHECK(s.execute({{"op", "draw_circle"}, {"cx", 0}, {"cy", 0}, {"r", 5}})["error"] == "no active sketch: use create_sketch first");
    CHECK(s.execute({{"op", "explode"}})["ok"] == false);
    CHECK(s.execute({{"op", "extrude"}, {"depth", 5}})["ok"] == false);
    CHECK(s.execute({{"op", "set_dimension"}, {"name", "D9@Nope"}, {"value", 1}})["ok"] == false);
    json r = s.run(json::parse(R"([{"op":"create_sketch"},{"op":"draw_circle","cx":0,"cy":0,"r":-1},{"op":"close_sketch"}])"));
    CHECK(r.size() == 2);  // stops at the failing command
    CHECK(CommandSession::describe().size() > 30);
}

TEST_CASE("C interface: execute commands and save") {
    StawnDoc* d = stawn_doc_new();
    char* out = stawn_doc_execute(d, R"([{"op":"create_sketch","plane":"Right"},{"op":"draw_rectangle","x1":0,"y1":0,"x2":10,"y2":10},{"op":"extrude","depth":10}])");
    json r = json::parse(out);
    stawn_free_string(out);
    CHECK(r[2]["volume_mm3"].get<double>() == doctest::Approx(1000));
    StawnMassProperties m = stawn_doc_mass_properties(d);
    CHECK(m.volume == doctest::Approx(1000));
    CHECK(m.cx == doctest::Approx(5));  // Right plane extrudes along +X
    out = stawn_doc_execute(d, "not json");
    CHECK(json::parse(out)["ok"] == false);
    stawn_free_string(out);
    stawn_doc_free(d);
}

TEST_CASE("sketching with the mouse through the command API, then extruding") {
    CommandSession cs;
    json r = cs.execute({{"op", "create_sketch"}, {"plane", "Top"}});
    REQUIRE(r["ok"] == true);
    cs.execute({{"op", "sketch_input"}, {"event", "tool"}, {"tool", "circle"}});
    cs.execute({{"op", "sketch_input"}, {"event", "click"}, {"x", 0}, {"y", 0}, {"tol", 0.5}});
    r = cs.execute({{"op", "sketch_input"}, {"event", "click"}, {"x", 12}, {"y", 0}, {"tol", 0.5}});
    CHECK(r["changed"] == true);
    CHECK(r["degrees_of_freedom"] == 1);

    r = cs.execute({{"op", "get_sketch"}});
    CHECK(r["plane"].get<std::string>() == "Top");
    CHECK(r["axes"]["normal"][1].get<double>() == doctest::Approx(1).epsilon(1e-9));
    CHECK(r["defined"]["points"][0] == true);   // the origin, and the centre coincides with it
    CHECK(r["defined"]["circles"][0] == false);  // radius still free

    cs.execute({{"op", "sketch_input"}, {"event", "tool"}, {"tool", "dimension"}});
    cs.execute({{"op", "sketch_input"}, {"event", "click"}, {"x", 0}, {"y", 12}, {"tol", 0.5}});
    r = cs.execute({{"op", "sketch_input"}, {"event", "dimension"}, {"value", 30}});
    CHECK(r["status"].get<std::string>() == "Fully Defined");
    r = cs.execute({{"op", "get_sketch"}});
    CHECK(r["defined"]["circles"][0] == true);

    cs.execute({{"op", "close_sketch"}});
    r = cs.execute({{"op", "extrude"}, {"depth", 10}});
    REQUIRE(r["ok"] == true);
    r = cs.execute({{"op", "mass_properties"}});
    CHECK(r["volume_mm3"].get<double>() == doctest::Approx(3.14159265 * 15 * 15 * 10).epsilon(1e-3));

    r = cs.execute({{"op", "edit_sketch"}, {"sketch", "Sketch1"}});
    CHECK(r["ok"] == true);
    CHECK(r["geometry"]["circles"].size() == 1);
}

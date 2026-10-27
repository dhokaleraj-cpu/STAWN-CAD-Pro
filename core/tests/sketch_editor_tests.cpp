#include <doctest/doctest.h>

#include "stawn/SketchEditor.hpp"

using namespace stawn;
using nlohmann::json;

namespace {
json click(SketchEditor& e, Sketch& s, double x, double y, bool add = false) {
    return e.input(s, {{"event", "click"}, {"x", x}, {"y", y}, {"tol", 0.5}, {"add", add}});
}
json tool(SketchEditor& e, Sketch& s, const char* t) { return e.input(s, {{"event", "tool"}, {"tool", t}}); }
}  // namespace

TEST_CASE("a rectangle from the origin plus two dimensions is fully defined") {
    Sketch s;
    SketchEditor e;
    tool(e, s, "rectangle");
    click(e, s, 0.1, -0.2);  // snaps to the origin
    json r = click(e, s, 30, 20);
    CHECK(s.lines.size() == 4);
    CHECK(r["status"].get<std::string>() == "Under Defined");
    CHECK(r["degrees_of_freedom"] == 2);

    tool(e, s, "dimension");
    click(e, s, 15, 0);  // bottom line
    r = e.input(s, {{"event", "dimension"}, {"value", 40}});
    CHECK(r["dimension"].get<std::string>() == "D1");
    click(e, s, 40, 10);  // right line, now at x = 40
    r = e.input(s, {{"event", "dimension"}, {"value", 25}});
    CHECK(r["status"].get<std::string>() == "Fully Defined");
    CHECK(r["degrees_of_freedom"] == 0);
    CHECK(s.points[2].x == doctest::Approx(40));
    CHECK(s.points[3].y == doctest::Approx(25));
}

TEST_CASE("line chains infer horizontal and vertical and close on their start") {
    Sketch s;
    SketchEditor e;
    tool(e, s, "line");
    click(e, s, 10, 10);
    json r = e.input(s, {{"event", "move"}, {"x", 30}, {"y", 10.3}, {"tol", 0.5}});
    CHECK(r["snap"]["kind"].get<std::string>() == "horizontal");
    CHECK(r["preview"]["lines"].size() == 1);
    click(e, s, 30, 10.3);
    click(e, s, 30.2, 25);
    r = click(e, s, 10.1, 9.9);  // back on the first point closes the chain
    CHECK(s.lines.size() == 3);
    CHECK(r["preview"]["lines"].empty());
    CHECK(r["prompt"].get<std::string>() == "Click the start point of the line");
    int horizontal = 0, vertical = 0;
    for (auto& c : s.constraints) {
        horizontal += c.type == ConstraintType::Horizontal;
        vertical += c.type == ConstraintType::Vertical;
    }
    CHECK(horizontal == 1);
    CHECK(vertical == 1);
}

TEST_CASE("escape leaves no stray start point") {
    Sketch s;
    SketchEditor e;
    tool(e, s, "line");
    click(e, s, 5, 5);
    CHECK(s.points.size() == 2);
    e.input(s, {{"event", "escape"}});
    CHECK(s.points.size() == 1);
    e.input(s, {{"event", "escape"}});
    CHECK((e.tool() == SketchEditor::Tool::Select));
}

TEST_CASE("circles and arcs, then a diameter and a tangent relation") {
    Sketch s;
    SketchEditor e;
    tool(e, s, "circle");
    click(e, s, 0, 0);
    click(e, s, 10, 0);
    REQUIRE(s.circles.size() == 1);
    CHECK(s.circles[0].radius == doctest::Approx(10));

    tool(e, s, "dimension");
    json r = click(e, s, 0, 10);
    CHECK(r["dimension_request"]["options"][0]["label"].get<std::string>() == "Diameter");
    CHECK(r["dimension_request"]["options"][0]["value"].get<double>() == doctest::Approx(20));
    e.input(s, {{"event", "dimension"}, {"value", 30}});
    CHECK(s.circles[0].radius == doctest::Approx(15));

    tool(e, s, "arc");
    click(e, s, 50, 0);
    click(e, s, 60, 0);
    click(e, s, 50, 7);
    REQUIRE(s.arcs.size() == 1);
    CHECK(s.points[size_t(s.arcs[0].end)].y == doctest::Approx(10));

    tool(e, s, "line");
    click(e, s, 60, 0);   // arc start
    click(e, s, 60, -20);
    e.input(s, {{"event", "escape"}});
    tool(e, s, "select");
    click(e, s, 60, -10);
    r = click(e, s, 57.07, 7.07, true);  // on the arc, between its ends
    REQUIRE(r["selection"].size() == 2);
    CHECK(r["relations"] == json::array({"Tangent"}));
    r = e.input(s, {{"event", "relation"}, {"index", 0}});
    CHECK(r["status"].get<std::string>() != "Failed");
}

TEST_CASE("relations offered for two lines and deleting selected geometry") {
    Sketch s;
    SketchEditor e;
    tool(e, s, "line");
    click(e, s, 0, 5);
    click(e, s, 20, 8);
    e.input(s, {{"event", "escape"}});
    click(e, s, 0, 20);
    click(e, s, 20, 26);
    e.input(s, {{"event", "escape"}});
    tool(e, s, "select");
    click(e, s, 10, 6.5);
    json r = click(e, s, 10, 23, true);
    CHECK(r["relations"] == json::array({"Parallel", "Perpendicular", "Equal"}));
    e.input(s, {{"event", "relation"}, {"index", 0}});
    CHECK((s.constraints.back().type == ConstraintType::Parallel));

    click(e, s, 10, 6.5);
    r = e.input(s, {{"event", "delete"}});
    CHECK(r["changed"] == true);
    CHECK(s.lines.size() == 1);
    CHECK(s.points.size() == 3);  // origin plus the remaining line's ends
    CHECK(s.constraints.empty());
}

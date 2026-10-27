#include <doctest/doctest.h>

#include "stawn/Json.hpp"
#include "stawn/Sketch.hpp"

using namespace stawn;

TEST_CASE("a rectangle with two dimensions from the origin is fully defined") {
    Sketch s;
    int a = 0, b = s.addPoint(9, 0.4), c = s.addPoint(11, 6), d = s.addPoint(-0.5, 4.5);
    int l0 = s.addLine(a, b), l1 = s.addLine(b, c), l2 = s.addLine(c, d), l3 = s.addLine(d, a);
    s.horizontal(l0);
    s.vertical(l1);
    s.horizontal(l2);
    s.vertical(l3);
    SolveResult r = s.solve();
    CHECK((r.status == SketchStatus::UnderDefined));
    CHECK(r.degreesOfFreedom == 2);

    s.dimension(ConstraintType::Length, l0, -1, 20, RefKind::Line);
    s.dimension(ConstraintType::Length, l1, -1, 10, RefKind::Line);
    r = s.solve();
    CHECK((r.status == SketchStatus::FullyDefined));
    CHECK(r.degreesOfFreedom == 0);
    CHECK(s.points[c].x == doctest::Approx(20).epsilon(1e-6));
    CHECK(s.points[c].y == doctest::Approx(10).epsilon(1e-6));
}

TEST_CASE("changing a dimension moves the geometry") {
    Sketch s;
    int p = s.addPoint(5, 0.2);
    int l = s.addLine(0, p);
    s.horizontal(l);
    s.dimension(ConstraintType::Length, l, -1, 5, RefKind::Line);
    REQUIRE((s.solve().status == SketchStatus::FullyDefined));
    CHECK(s.setDimension("D1", 12.5));
    s.solve();
    CHECK(s.points[p].x == doctest::Approx(12.5));
    CHECK(s.points[p].y == doctest::Approx(0).epsilon(1e-9));
}

TEST_CASE("conflicting and redundant relations report over defined") {
    Sketch s;
    int p = s.addPoint(5, 0.2);
    int l = s.addLine(0, p);
    s.horizontal(l);
    s.dimension(ConstraintType::Length, l, -1, 5, RefKind::Line);
    s.dimension(ConstraintType::HorizontalDistance, 0, p, 7);
    CHECK((s.solve().status == SketchStatus::OverDefined));

    Sketch t;
    int q = t.addPoint(5, 0.2);
    int m = t.addLine(0, q);
    t.horizontal(m);
    t.addConstraint({ConstraintType::Horizontal, m, -1, RefKind::Line});  // redundant
    CHECK((t.solve().status == SketchStatus::OverDefined));
}

TEST_CASE("circle diameter, tangent line and angle") {
    Sketch s;
    int c = s.addPoint(10, 10);
    int circle = s.addCircle(c, 3);
    s.dimension(ConstraintType::Diameter, circle, -1, 8, RefKind::Circle);
    s.addConstraint({ConstraintType::Fix, c, -1, RefKind::Point, RefKind::Point, 10, 10});
    CHECK((s.solve().status == SketchStatus::FullyDefined));
    CHECK(s.circles[circle].radius == doctest::Approx(4));

    Sketch a;
    int l1 = a.addLine(0, a.addPoint(10, 0.5));
    int l2 = a.addLine(0, a.addPoint(5, 7));
    a.horizontal(l1);
    a.dimension(ConstraintType::Angle, l1, l2, 30, RefKind::Line, RefKind::Line);
    a.solve();
    auto& p = a.points[a.lines[l2].p2];
    CHECK(std::atan2(p.y, p.x) * 180 / 3.14159265358979 == doctest::Approx(30).epsilon(1e-6));
}

TEST_CASE("arc endpoints stay on one radius") {
    Sketch s;
    int c = s.addPoint(0, 0);
    s.coincident(c, 0);
    int a = s.addPoint(5, 0), b = s.addPoint(0, 4);
    int arc = s.addArc(c, a, b);
    s.dimension(ConstraintType::Radius, arc, -1, 6, RefKind::Arc);
    s.solve();
    CHECK(std::hypot(s.points[a].x, s.points[a].y) == doctest::Approx(6));
    CHECK(std::hypot(s.points[b].x, s.points[b].y) == doctest::Approx(6));
}

TEST_CASE("sketch JSON round trip keeps geometry and dimension names") {
    Sketch s;
    int p = s.addPoint(5, 0.2);
    int l = s.addLine(0, p);
    s.horizontal(l);
    s.dimension(ConstraintType::Length, l, -1, 5, RefKind::Line);
    Sketch t = sketchFromJson(sketchToJson(s));
    CHECK(t.points.size() == s.points.size());
    CHECK(t.constraints.size() == s.constraints.size());
    CHECK(t.dimension("D1").value() == doctest::Approx(5));
    CHECK((t.solve().status == SketchStatus::FullyDefined));
}

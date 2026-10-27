// STAWN CAD Pro - 2D sketch model and constraint solver.
#pragma once

#include <map>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace stawn {

struct Vec2 {
    double x = 0, y = 0;
};

struct SketchPoint {
    double x = 0, y = 0;
    bool fixed = false;  // a fixed point never moves (the sketch origin is fixed)
};

struct SketchLine {
    int p1 = -1, p2 = -1;
    bool construction = false;  // construction lines (centerlines) are not part of the profile
};

struct SketchCircle {
    int center = -1;
    double radius = 1;
    bool construction = false;
};

// Counter-clockwise arc from start to end around center.
struct SketchArc {
    int center = -1, start = -1, end = -1;
    bool construction = false;
};

enum class ConstraintType {
    Coincident,          // a = point, b = point
    Horizontal,          // a = line
    Vertical,            // a = line
    Parallel,            // a = line, b = line
    Perpendicular,       // a = line, b = line
    EqualLength,         // a = line, b = line
    PointOnLine,         // a = point, b = line
    PointOnCircle,       // a = point, b = circle (or arc with entityB = arc)
    Midpoint,            // a = point, b = line
    TangentLineArc,      // a = line, b = arc
    Fix,                 // a = point, value = x, value2 = y
    // Driving dimensions: these carry a value and a name such as D1.
    Distance,            // a = point, b = point
    HorizontalDistance,  // a = point, b = point: xb - xa
    VerticalDistance,    // a = point, b = point: yb - ya
    Length,              // a = line
    Radius,              // a = circle or arc (see refKind)
    Diameter,            // a = circle or arc (see refKind)
    Angle,               // a = line, b = line, value in degrees
};

enum class RefKind { Point, Line, Circle, Arc };

struct SketchConstraint {
    ConstraintType type{};
    int a = -1, b = -1;
    RefKind refKindA = RefKind::Point;
    RefKind refKindB = RefKind::Point;
    double value = 0, value2 = 0;
    std::string name;  // dimension name, e.g. "D1"
    bool isDimension() const;
};

enum class SketchStatus { UnderDefined, FullyDefined, OverDefined, Failed };

const char* toString(SketchStatus s);
const char* toString(ConstraintType t);
std::optional<ConstraintType> constraintTypeFromString(const std::string& s);

struct SolveResult {
    SketchStatus status = SketchStatus::Failed;
    int degreesOfFreedom = 0;
    double residual = 0;
    int iterations = 0;
    // Which geometry the relations and dimensions pin down, for colouring the sketch:
    // defined geometry is drawn black, geometry that can still move blue.
    std::vector<bool> pointDefined;
    std::vector<bool> circleDefined;  // the radius (the centre is its own point)
};

// Which kind of geometry a relation's a and b refer to; Point for unused slots.
std::pair<RefKind, RefKind> referenceKinds(const SketchConstraint& c);

class Sketch {
public:
    Sketch();  // creates the fixed origin as point 0

    int addPoint(double x, double y, bool fixed = false);
    int addLine(int p1, int p2, bool construction = false);
    int addLine(double x1, double y1, double x2, double y2, bool construction = false);
    int addCircle(int center, double radius, bool construction = false);
    int addArc(int center, int start, int end, bool construction = false);
    int addConstraint(const SketchConstraint& c);

    // Convenience wrappers for common relations and dimensions.
    int coincident(int p, int q);
    int horizontal(int line);
    int vertical(int line);
    int dimension(ConstraintType type, int a, int b, double value, RefKind ka = RefKind::Point,
                  RefKind kb = RefKind::Point);

    // Set a driving dimension by name (D1, D2...). Returns false if not found.
    bool setDimension(const std::string& name, double value);
    std::optional<double> dimension(const std::string& name) const;

    SolveResult solve(int maxIterations = 200);

    // Removing geometry also removes the relations and dimensions on it, and points no
    // longer used by any entity (except the origin). Later indices shift down.
    void removeLine(int i);
    void removeCircle(int i);
    void removeArc(int i);
    void removeConstraint(int i);
    void removeUnusedPoints();

    std::vector<SketchPoint> points;
    std::vector<SketchLine> lines;
    std::vector<SketchCircle> circles;
    std::vector<SketchArc> arcs;
    std::vector<SketchConstraint> constraints;

private:
    int nextDimensionNumber_ = 1;
};

}  // namespace stawn

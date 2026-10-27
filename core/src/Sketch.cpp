// STAWN CAD Pro - sketch constraint solver.
//
// Every free point contributes (x, y) and every circle its radius to the unknown vector.
// Each relation or dimension contributes one or two residuals. The solver runs
// Levenberg-Marquardt on the residuals, then inspects the rank of the Jacobian at the
// solution to report the sketch state the same way the status bar does: under defined
// (free degrees of freedom left), fully defined, or over defined (conflicting or redundant).
#include "stawn/Sketch.hpp"

#include <Eigen/Dense>
#include <cmath>
#include <functional>
#include <stdexcept>

namespace stawn {

namespace {
constexpr double kPi = 3.14159265358979323846;
constexpr double kSolvedTolerance = 1e-9;
}  // namespace

bool SketchConstraint::isDimension() const {
    switch (type) {
        case ConstraintType::Distance:
        case ConstraintType::HorizontalDistance:
        case ConstraintType::VerticalDistance:
        case ConstraintType::Length:
        case ConstraintType::Radius:
        case ConstraintType::Diameter:
        case ConstraintType::Angle:
            return true;
        default:
            return false;
    }
}

const char* toString(SketchStatus s) {
    switch (s) {
        case SketchStatus::UnderDefined: return "Under Defined";
        case SketchStatus::FullyDefined: return "Fully Defined";
        case SketchStatus::OverDefined: return "Over Defined";
        case SketchStatus::Failed: return "Failed";
    }
    return "?";
}

static const std::pair<ConstraintType, const char*> kConstraintNames[] = {
    {ConstraintType::Coincident, "coincident"},
    {ConstraintType::Horizontal, "horizontal"},
    {ConstraintType::Vertical, "vertical"},
    {ConstraintType::Parallel, "parallel"},
    {ConstraintType::Perpendicular, "perpendicular"},
    {ConstraintType::EqualLength, "equalLength"},
    {ConstraintType::PointOnLine, "pointOnLine"},
    {ConstraintType::PointOnCircle, "pointOnCircle"},
    {ConstraintType::Midpoint, "midpoint"},
    {ConstraintType::TangentLineArc, "tangentLineArc"},
    {ConstraintType::Fix, "fix"},
    {ConstraintType::Distance, "distance"},
    {ConstraintType::HorizontalDistance, "horizontalDistance"},
    {ConstraintType::VerticalDistance, "verticalDistance"},
    {ConstraintType::Length, "length"},
    {ConstraintType::Radius, "radius"},
    {ConstraintType::Diameter, "diameter"},
    {ConstraintType::Angle, "angle"},
};

const char* toString(ConstraintType t) {
    for (auto& [type, name] : kConstraintNames)
        if (type == t) return name;
    return "?";
}

std::optional<ConstraintType> constraintTypeFromString(const std::string& s) {
    for (auto& [type, name] : kConstraintNames)
        if (s == name) return type;
    return std::nullopt;
}

Sketch::Sketch() { addPoint(0, 0, true); }

int Sketch::addPoint(double x, double y, bool fixed) {
    points.push_back({x, y, fixed});
    return int(points.size()) - 1;
}

int Sketch::addLine(int p1, int p2, bool construction) {
    lines.push_back({p1, p2, construction});
    return int(lines.size()) - 1;
}

int Sketch::addLine(double x1, double y1, double x2, double y2, bool construction) {
    return addLine(addPoint(x1, y1), addPoint(x2, y2), construction);
}

int Sketch::addCircle(int center, double radius, bool construction) {
    circles.push_back({center, radius, construction});
    return int(circles.size()) - 1;
}

int Sketch::addArc(int center, int start, int end, bool construction) {
    arcs.push_back({center, start, end, construction});
    return int(arcs.size()) - 1;
}

int Sketch::addConstraint(const SketchConstraint& c) {
    SketchConstraint copy = c;
    if (copy.isDimension() && copy.name.empty()) copy.name = "D" + std::to_string(nextDimensionNumber_++);
    if (copy.isDimension() && copy.name.size() > 1 && copy.name[0] == 'D') {
        try {
            nextDimensionNumber_ = std::max(nextDimensionNumber_, std::stoi(copy.name.substr(1)) + 1);
        } catch (...) {
        }
    }
    constraints.push_back(copy);
    return int(constraints.size()) - 1;
}

int Sketch::coincident(int p, int q) {
    return addConstraint({ConstraintType::Coincident, p, q, RefKind::Point, RefKind::Point});
}
int Sketch::horizontal(int line) { return addConstraint({ConstraintType::Horizontal, line, -1, RefKind::Line}); }
int Sketch::vertical(int line) { return addConstraint({ConstraintType::Vertical, line, -1, RefKind::Line}); }

int Sketch::dimension(ConstraintType type, int a, int b, double value, RefKind ka, RefKind kb) {
    SketchConstraint c;
    c.type = type;
    c.a = a;
    c.b = b;
    c.refKindA = ka;
    c.refKindB = kb;
    c.value = value;
    return addConstraint(c);
}

bool Sketch::setDimension(const std::string& name, double value) {
    for (auto& c : constraints)
        if (c.isDimension() && c.name == name) {
            c.value = value;
            return true;
        }
    return false;
}

std::optional<double> Sketch::dimension(const std::string& name) const {
    for (auto& c : constraints)
        if (c.isDimension() && c.name == name) return c.value;
    return std::nullopt;
}

namespace {

// Maps the sketch geometry to and from the solver's unknown vector.
struct Unknowns {
    std::vector<int> pointIndex;   // index of x in the vector, or -1 if the point is fixed
    std::vector<int> circleIndex;  // index of radius in the vector
    int size = 0;

    explicit Unknowns(const Sketch& s) {
        pointIndex.assign(s.points.size(), -1);
        for (size_t i = 0; i < s.points.size(); ++i)
            if (!s.points[i].fixed) {
                pointIndex[i] = size;
                size += 2;
            }
        circleIndex.assign(s.circles.size(), -1);
        for (size_t i = 0; i < s.circles.size(); ++i) circleIndex[i] = size++;
    }
};

struct Evaluator {
    const Sketch& s;
    const Unknowns& u;
    const Eigen::VectorXd& v;

    Vec2 p(int i) const {
        int k = u.pointIndex.at(i);
        if (k < 0) return {s.points[i].x, s.points[i].y};
        return {v[k], v[k + 1]};
    }
    double circleRadius(int i) const { return v[u.circleIndex.at(i)]; }
    double arcRadius(int i) const {
        auto c = p(s.arcs.at(i).center), a = p(s.arcs.at(i).start);
        return std::hypot(a.x - c.x, a.y - c.y);
    }
    Vec2 center(int i, RefKind k) const {
        return k == RefKind::Arc ? p(s.arcs.at(i).center) : p(s.circles.at(i).center);
    }
    double radius(int i, RefKind k) const { return k == RefKind::Arc ? arcRadius(i) : circleRadius(i); }
    std::pair<Vec2, Vec2> line(int i) const { return {p(s.lines.at(i).p1), p(s.lines.at(i).p2)}; }
    Vec2 dir(int i) const {
        auto [a, b] = line(i);
        double dx = b.x - a.x, dy = b.y - a.y, len = std::hypot(dx, dy);
        if (len < 1e-12) return {1, 0};
        return {dx / len, dy / len};
    }

    void residuals(std::vector<double>& r) const {
        r.clear();
        // Every arc keeps its start and end on the same circle.
        for (size_t i = 0; i < s.arcs.size(); ++i) {
            auto c = p(s.arcs[i].center), e = p(s.arcs[i].end);
            r.push_back(std::hypot(e.x - c.x, e.y - c.y) - arcRadius(int(i)));
        }
        for (auto& c : s.constraints) {
            switch (c.type) {
                case ConstraintType::Coincident: {
                    auto a = p(c.a), b = p(c.b);
                    r.push_back(a.x - b.x);
                    r.push_back(a.y - b.y);
                    break;
                }
                case ConstraintType::Horizontal: {
                    auto [a, b] = line(c.a);
                    r.push_back(a.y - b.y);
                    break;
                }
                case ConstraintType::Vertical: {
                    auto [a, b] = line(c.a);
                    r.push_back(a.x - b.x);
                    break;
                }
                case ConstraintType::Parallel: {
                    auto d1 = dir(c.a), d2 = dir(c.b);
                    r.push_back(d1.x * d2.y - d1.y * d2.x);
                    break;
                }
                case ConstraintType::Perpendicular: {
                    auto d1 = dir(c.a), d2 = dir(c.b);
                    r.push_back(d1.x * d2.x + d1.y * d2.y);
                    break;
                }
                case ConstraintType::EqualLength: {
                    auto [a1, b1] = line(c.a);
                    auto [a2, b2] = line(c.b);
                    r.push_back(std::hypot(b1.x - a1.x, b1.y - a1.y) - std::hypot(b2.x - a2.x, b2.y - a2.y));
                    break;
                }
                case ConstraintType::PointOnLine: {
                    auto q = p(c.a);
                    auto [a, b] = line(c.b);
                    auto d = dir(c.b);
                    r.push_back((q.x - a.x) * d.y - (q.y - a.y) * d.x);
                    break;
                }
                case ConstraintType::PointOnCircle: {
                    auto q = p(c.a);
                    auto ctr = center(c.b, c.refKindB);
                    r.push_back(std::hypot(q.x - ctr.x, q.y - ctr.y) - radius(c.b, c.refKindB));
                    break;
                }
                case ConstraintType::Midpoint: {
                    auto q = p(c.a);
                    auto [a, b] = line(c.b);
                    r.push_back(q.x - (a.x + b.x) / 2);
                    r.push_back(q.y - (a.y + b.y) / 2);
                    break;
                }
                case ConstraintType::TangentLineArc: {
                    auto [a, b] = line(c.a);
                    auto d = dir(c.a);
                    auto ctr = center(c.b, c.refKindB);
                    double dist = std::abs((ctr.x - a.x) * d.y - (ctr.y - a.y) * d.x);
                    r.push_back(dist - radius(c.b, c.refKindB));
                    break;
                }
                case ConstraintType::Fix: {
                    auto q = p(c.a);
                    r.push_back(q.x - c.value);
                    r.push_back(q.y - c.value2);
                    break;
                }
                case ConstraintType::Distance: {
                    auto a = p(c.a), b = p(c.b);
                    r.push_back(std::hypot(b.x - a.x, b.y - a.y) - c.value);
                    break;
                }
                case ConstraintType::HorizontalDistance: {
                    auto a = p(c.a), b = p(c.b);
                    r.push_back(std::abs(b.x - a.x) - c.value);
                    break;
                }
                case ConstraintType::VerticalDistance: {
                    auto a = p(c.a), b = p(c.b);
                    r.push_back(std::abs(b.y - a.y) - c.value);
                    break;
                }
                case ConstraintType::Length: {
                    auto [a, b] = line(c.a);
                    r.push_back(std::hypot(b.x - a.x, b.y - a.y) - c.value);
                    break;
                }
                case ConstraintType::Radius:
                    r.push_back(radius(c.a, c.refKindA) - c.value);
                    break;
                case ConstraintType::Diameter:
                    r.push_back(2 * radius(c.a, c.refKindA) - c.value);
                    break;
                case ConstraintType::Angle: {
                    auto d1 = dir(c.a), d2 = dir(c.b);
                    double ang = std::atan2(d1.x * d2.y - d1.y * d2.x, d1.x * d2.x + d1.y * d2.y);
                    double target = c.value * kPi / 180.0;
                    double diff = std::remainder(std::abs(ang) - target, 2 * kPi);
                    r.push_back(diff);
                    break;
                }
            }
        }
    }
};

void computeResiduals(const Sketch& s, const Unknowns& u, const Eigen::VectorXd& v, Eigen::VectorXd& out) {
    std::vector<double> r;
    Evaluator{s, u, v}.residuals(r);
    out = Eigen::Map<Eigen::VectorXd>(r.data(), Eigen::Index(r.size()));
}

Eigen::MatrixXd jacobian(const Sketch& s, const Unknowns& u, const Eigen::VectorXd& v, Eigen::Index rows) {
    Eigen::MatrixXd J(rows, u.size);
    Eigen::VectorXd plus, minus, w = v;
    for (int j = 0; j < u.size; ++j) {
        double h = 1e-7 * std::max(1.0, std::abs(v[j]));
        w[j] = v[j] + h;
        computeResiduals(s, u, w, plus);
        w[j] = v[j] - h;
        computeResiduals(s, u, w, minus);
        w[j] = v[j];
        J.col(j) = (plus - minus) / (2 * h);
    }
    return J;
}

}  // namespace

SolveResult Sketch::solve(int maxIterations) {
    Unknowns u(*this);
    Eigen::VectorXd v(u.size);
    for (size_t i = 0; i < points.size(); ++i)
        if (u.pointIndex[i] >= 0) {
            v[u.pointIndex[i]] = points[i].x;
            v[u.pointIndex[i] + 1] = points[i].y;
        }
    for (size_t i = 0; i < circles.size(); ++i) v[u.circleIndex[i]] = circles[i].radius;

    SolveResult result;
    Eigen::VectorXd r;
    computeResiduals(*this, u, v, r);
    double cost = r.squaredNorm();
    double lambda = 1e-3;

    for (int it = 0; it < maxIterations && cost > kSolvedTolerance * kSolvedTolerance && u.size > 0; ++it) {
        result.iterations = it + 1;
        Eigen::MatrixXd J = jacobian(*this, u, v, r.size());
        Eigen::MatrixXd A = J.transpose() * J;
        Eigen::VectorXd g = J.transpose() * r;
        bool improved = false;
        for (int attempt = 0; attempt < 12; ++attempt) {
            Eigen::MatrixXd damped = A;
            damped.diagonal().array() += lambda * (1.0 + A.diagonal().array());
            Eigen::VectorXd step = damped.ldlt().solve(-g);
            Eigen::VectorXd candidate = v + step;
            Eigen::VectorXd rc;
            computeResiduals(*this, u, candidate, rc);
            double c = rc.squaredNorm();
            if (std::isfinite(c) && c < cost) {
                v = candidate;
                r = rc;
                cost = c;
                lambda = std::max(lambda / 10, 1e-12);
                improved = true;
                break;
            }
            lambda *= 10;
        }
        if (!improved) break;
    }

    result.residual = std::sqrt(cost);
    for (size_t i = 0; i < points.size(); ++i)
        if (u.pointIndex[i] >= 0) {
            points[i].x = v[u.pointIndex[i]];
            points[i].y = v[u.pointIndex[i] + 1];
        }
    for (size_t i = 0; i < circles.size(); ++i) circles[i].radius = v[u.circleIndex[i]];

    if (result.residual > 1e-6) {
        result.status = SketchStatus::OverDefined;  // constraints conflict
        return result;
    }
    if (u.size == 0) {
        result.status = r.size() > 0 ? SketchStatus::OverDefined : SketchStatus::FullyDefined;
        return result;
    }
    Eigen::MatrixXd J = jacobian(*this, u, v, r.size());
    Eigen::ColPivHouseholderQR<Eigen::MatrixXd> qr(J);
    qr.setThreshold(1e-8);
    int rank = int(qr.rank());
    result.degreesOfFreedom = u.size - rank;
    if (rank < r.size())
        result.status = SketchStatus::OverDefined;  // redundant relations
    else if (result.degreesOfFreedom > 0)
        result.status = SketchStatus::UnderDefined;
    else
        result.status = SketchStatus::FullyDefined;
    return result;
}

}  // namespace stawn

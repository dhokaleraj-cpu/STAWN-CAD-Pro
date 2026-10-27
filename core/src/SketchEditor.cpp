#include "stawn/SketchEditor.hpp"

#include <algorithm>
#include <cmath>
#include <stdexcept>

using nlohmann::json;

namespace stawn {

namespace {

constexpr double kPi = 3.14159265358979323846;

const char* kindName(RefKind k) {
    switch (k) {
        case RefKind::Point: return "point";
        case RefKind::Line: return "line";
        case RefKind::Circle: return "circle";
        case RefKind::Arc: return "arc";
    }
    return "point";
}

double dist(Vec2 a, Vec2 b) { return std::hypot(a.x - b.x, a.y - b.y); }
Vec2 pt(const Sketch& s, int i) { return {s.points[size_t(i)].x, s.points[size_t(i)].y}; }

double segmentDistance(Vec2 p, Vec2 a, Vec2 b) {
    double dx = b.x - a.x, dy = b.y - a.y, len2 = dx * dx + dy * dy;
    double t = len2 < 1e-18 ? 0 : std::clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / len2, 0.0, 1.0);
    return dist(p, {a.x + t * dx, a.y + t * dy});
}

// Counter-clockwise angle from `from` to `to`, in [0, 2pi).
double ccw(double from, double to) {
    double d = std::fmod(to - from, 2 * kPi);
    return d < 0 ? d + 2 * kPi : d;
}

struct Pick {
    RefKind kind = RefKind::Point;
    int index = -1;
    double distance = 1e300;
};

// Nearest geometry within tol; points win over curves so that ends are easy to pick.
Pick pickAt(const Sketch& s, Vec2 p, double tol) {
    Pick best;
    for (size_t i = 0; i < s.points.size(); ++i) {
        double d = dist(p, pt(s, int(i)));
        if (d <= tol && d < best.distance) best = {RefKind::Point, int(i), d};
    }
    if (best.index >= 0) return best;
    for (size_t i = 0; i < s.lines.size(); ++i) {
        double d = segmentDistance(p, pt(s, s.lines[i].p1), pt(s, s.lines[i].p2));
        if (d <= tol && d < best.distance) best = {RefKind::Line, int(i), d};
    }
    for (size_t i = 0; i < s.circles.size(); ++i) {
        double d = std::abs(dist(p, pt(s, s.circles[i].center)) - s.circles[i].radius);
        if (d <= tol && d < best.distance) best = {RefKind::Circle, int(i), d};
    }
    for (size_t i = 0; i < s.arcs.size(); ++i) {
        auto& a = s.arcs[i];
        Vec2 c = pt(s, a.center), st = pt(s, a.start), en = pt(s, a.end);
        double r = dist(c, st);
        double a0 = std::atan2(st.y - c.y, st.x - c.x), a1 = std::atan2(en.y - c.y, en.x - c.x);
        double ap = std::atan2(p.y - c.y, p.x - c.x);
        if (ccw(a0, ap) > ccw(a0, a1)) continue;  // outside the arc's sweep
        double d = std::abs(dist(p, c) - r);
        if (d <= tol && d < best.distance) best = {RefKind::Arc, int(i), d};
    }
    return best;
}

double lineLength(const Sketch& s, int l) { return dist(pt(s, s.lines[size_t(l)].p1), pt(s, s.lines[size_t(l)].p2)); }

double lineAngle(const Sketch& s, int l1, int l2) {
    auto dir = [&](int l) {
        Vec2 a = pt(s, s.lines[size_t(l)].p1), b = pt(s, s.lines[size_t(l)].p2);
        return std::atan2(b.y - a.y, b.x - a.x);
    };
    double d = std::abs(std::remainder(dir(l2) - dir(l1), 2 * kPi));
    return d * 180 / kPi;
}

struct Choice {
    std::string label;
    SketchConstraint constraint;
};

SketchConstraint make(ConstraintType t, int a, int b = -1, RefKind ka = RefKind::Point, RefKind kb = RefKind::Point,
                      double value = 0, double value2 = 0) {
    SketchConstraint c;
    c.type = t;
    c.a = a;
    c.b = b;
    c.refKindA = ka;
    c.refKindB = kb;
    c.value = value;
    c.value2 = value2;
    return c;
}

// Relations that make sense for the selected geometry.
std::vector<Choice> relationChoices(const Sketch& s, const std::vector<SketchEditor::Item>& sel) {
    using T = ConstraintType;
    std::vector<Choice> out;
    auto is = [&](size_t i, RefKind k) { return sel[i].kind == k; };
    if (sel.size() == 1) {
        if (is(0, RefKind::Line)) {
            out.push_back({"Horizontal", make(T::Horizontal, sel[0].index, -1, RefKind::Line)});
            out.push_back({"Vertical", make(T::Vertical, sel[0].index, -1, RefKind::Line)});
        } else if (is(0, RefKind::Point) && sel[0].index != 0) {
            Vec2 p = pt(s, sel[0].index);
            out.push_back({"Fix", make(T::Fix, sel[0].index, -1, RefKind::Point, RefKind::Point, p.x, p.y)});
        }
    } else if (sel.size() == 2) {
        // Order the pair so that points come first, then lines, then curves.
        SketchEditor::Item a = sel[0], b = sel[1];
        if (int(b.kind) < int(a.kind)) std::swap(a, b);
        if (a.kind == RefKind::Point && b.kind == RefKind::Point) {
            out.push_back({"Coincident", make(T::Coincident, a.index, b.index)});
        } else if (a.kind == RefKind::Line && b.kind == RefKind::Line) {
            out.push_back({"Parallel", make(T::Parallel, a.index, b.index, RefKind::Line, RefKind::Line)});
            out.push_back({"Perpendicular", make(T::Perpendicular, a.index, b.index, RefKind::Line, RefKind::Line)});
            out.push_back({"Equal", make(T::EqualLength, a.index, b.index, RefKind::Line, RefKind::Line)});
        } else if (a.kind == RefKind::Point && b.kind == RefKind::Line) {
            out.push_back({"Coincident", make(T::PointOnLine, a.index, b.index, RefKind::Point, RefKind::Line)});
            out.push_back({"Midpoint", make(T::Midpoint, a.index, b.index, RefKind::Point, RefKind::Line)});
        } else if (a.kind == RefKind::Point) {
            out.push_back({"Coincident", make(T::PointOnCircle, a.index, b.index, RefKind::Point, b.kind)});
        } else if (a.kind == RefKind::Line) {
            out.push_back({"Tangent", make(T::TangentLineArc, a.index, b.index, RefKind::Line, b.kind)});
        }
    }
    return out;
}

struct DimensionChoice {
    std::string label;
    SketchConstraint constraint;  // value holds the current measurement
};

std::vector<DimensionChoice> dimensionChoices(const Sketch& s, const std::vector<SketchEditor::Item>& picks) {
    using T = ConstraintType;
    std::vector<DimensionChoice> out;
    if (picks.size() == 1) {
        auto& p = picks[0];
        if (p.kind == RefKind::Line) {
            out.push_back({"Length", make(T::Length, p.index, -1, RefKind::Line, RefKind::Point, lineLength(s, p.index))});
        } else if (p.kind == RefKind::Circle) {
            double r = s.circles[size_t(p.index)].radius;
            out.push_back({"Diameter", make(T::Diameter, p.index, -1, RefKind::Circle, RefKind::Point, 2 * r)});
            out.push_back({"Radius", make(T::Radius, p.index, -1, RefKind::Circle, RefKind::Point, r)});
        } else if (p.kind == RefKind::Arc) {
            auto& a = s.arcs[size_t(p.index)];
            double r = dist(pt(s, a.center), pt(s, a.start));
            out.push_back({"Radius", make(T::Radius, p.index, -1, RefKind::Arc, RefKind::Point, r)});
            out.push_back({"Diameter", make(T::Diameter, p.index, -1, RefKind::Arc, RefKind::Point, 2 * r)});
        }
    } else if (picks.size() == 2) {
        auto& a = picks[0];
        auto& b = picks[1];
        if (a.kind == RefKind::Point && b.kind == RefKind::Point) {
            Vec2 p = pt(s, a.index), q = pt(s, b.index);
            // Offer the axis-aligned distance first when the points are nearly lined up.
            DimensionChoice d{"Distance", make(T::Distance, a.index, b.index, RefKind::Point, RefKind::Point, dist(p, q))};
            DimensionChoice h{"Horizontal", make(T::HorizontalDistance, a.index, b.index, RefKind::Point, RefKind::Point,
                                                 std::abs(q.x - p.x))};
            DimensionChoice v{"Vertical", make(T::VerticalDistance, a.index, b.index, RefKind::Point, RefKind::Point,
                                               std::abs(q.y - p.y))};
            if (std::abs(q.y - p.y) < 1e-6) out = {h, d, v};
            else if (std::abs(q.x - p.x) < 1e-6) out = {v, d, h};
            else out = {d, h, v};
        } else if (a.kind == RefKind::Line && b.kind == RefKind::Line) {
            out.push_back({"Angle", make(T::Angle, a.index, b.index, RefKind::Line, RefKind::Line, lineAngle(s, a.index, b.index))});
        }
    }
    return out;
}

}  // namespace

const char* toString(SketchEditor::Tool t) {
    switch (t) {
        case SketchEditor::Tool::Select: return "select";
        case SketchEditor::Tool::Line: return "line";
        case SketchEditor::Tool::Centerline: return "centerline";
        case SketchEditor::Tool::Rectangle: return "rectangle";
        case SketchEditor::Tool::Circle: return "circle";
        case SketchEditor::Tool::Arc: return "arc";
        case SketchEditor::Tool::Dimension: return "dimension";
    }
    return "select";
}

void SketchEditor::reset() {
    tool_ = Tool::Select;
    clearPending();
    selection_.clear();
}

void SketchEditor::clearPending() {
    pending_.clear();
    chainStart_ = -1;
    dimensionPicks_.clear();
}

SketchEditor::Snap SketchEditor::snap(const Sketch& s, double x, double y, double tol, bool inferAxis) const {
    Snap out{{x, y}, -1, ""};
    double best = tol;
    for (size_t i = 0; i < s.points.size(); ++i) {
        double d = dist({x, y}, pt(s, int(i)));
        if (d <= best) {
            best = d;
            out = {pt(s, int(i)), int(i), i == 0 ? "origin" : "point"};
        }
    }
    if (out.point >= 0 || !inferAxis || pending_.empty()) return out;
    // Line up with the previous point, adding a horizontal or vertical relation on click.
    Vec2 last = pt(s, pending_.back());
    if (std::abs(y - last.y) <= tol && std::abs(x - last.x) > tol) out = {{x, last.y}, -1, "horizontal"};
    else if (std::abs(x - last.x) <= tol && std::abs(y - last.y) > tol) out = {{last.x, y}, -1, "vertical"};
    return out;
}

int SketchEditor::pointFor(Sketch& s, const Snap& at) {
    if (at.point >= 0) return at.point;
    for (size_t i = 0; i < s.points.size(); ++i)
        if (dist(at.at, pt(s, int(i))) < 1e-9) return int(i);
    return s.addPoint(at.at.x, at.at.y);
}

json SketchEditor::input(Sketch& s, const json& e) {
    std::string event = e.value("event", "");
    double x = e.value("x", lastCursor_.x), y = e.value("y", lastCursor_.y);
    double tol = std::max(e.value("tol", 0.5), 1e-6);
    bool changed = false;
    json extra = json::object();

    if (event == "tool") {
        std::string t = e.value("tool", "select");
        static const std::pair<const char*, Tool> tools[] = {
            {"select", Tool::Select}, {"line", Tool::Line}, {"centerline", Tool::Centerline},
            {"rectangle", Tool::Rectangle}, {"circle", Tool::Circle}, {"arc", Tool::Arc},
            {"dimension", Tool::Dimension}};
        auto it = std::find_if(std::begin(tools), std::end(tools), [&](auto& p) { return t == p.first; });
        if (it == std::end(tools)) throw std::runtime_error("unknown sketch tool " + t);
        if (!pending_.empty()) {
            s.removeUnusedPoints();  // a lone start point from an unfinished line
            changed = true;
        }
        clearPending();
        tool_ = it->second;
        if (tool_ != Tool::Select) selection_.clear();
    } else if (event == "move") {
        lastCursor_ = {x, y};
    } else if (event == "click") {
        lastCursor_ = {x, y};
        bool axis = tool_ == Tool::Line || tool_ == Tool::Centerline;
        Snap at = snap(s, x, y, tol, axis);
        extra = click(s, at, tol, e.value("add", false), x, y);
        changed = extra.value("changed", false);
        extra.erase("changed");
    } else if (event == "escape") {
        if (!pending_.empty() || !dimensionPicks_.empty()) {
            if (!pending_.empty()) changed = true;
            clearPending();
            s.removeUnusedPoints();
        } else if (!selection_.empty()) {
            selection_.clear();
        } else {
            tool_ = Tool::Select;
        }
    } else if (event == "delete") {
        // Highest index first within each kind so earlier indices stay valid.
        auto sel = selection_;
        std::sort(sel.begin(), sel.end(), [](const Item& a, const Item& b) {
            return a.kind != b.kind ? int(a.kind) > int(b.kind) : a.index > b.index;
        });
        for (auto& it : sel) {
            switch (it.kind) {
                case RefKind::Line: s.removeLine(it.index); break;
                case RefKind::Circle: s.removeCircle(it.index); break;
                case RefKind::Arc: s.removeArc(it.index); break;
                case RefKind::Point: {
                    if (it.index == 0) break;  // the origin stays
                    // Deleting a point deletes the geometry that uses it.
                    for (int i = int(s.arcs.size()) - 1; i >= 0; --i) {
                        auto& a = s.arcs[size_t(i)];
                        if (a.center == it.index || a.start == it.index || a.end == it.index) s.removeArc(i);
                    }
                    for (int i = int(s.circles.size()) - 1; i >= 0; --i)
                        if (s.circles[size_t(i)].center == it.index) s.removeCircle(i);
                    for (int i = int(s.lines.size()) - 1; i >= 0; --i)
                        if (s.lines[size_t(i)].p1 == it.index || s.lines[size_t(i)].p2 == it.index) s.removeLine(i);
                    break;
                }
            }
        }
        changed = !selection_.empty();
        selection_.clear();
    } else if (event == "relation") {
        auto choices = relationChoices(s, selection_);
        size_t i = size_t(e.value("index", 0));
        if (i >= choices.size()) throw std::runtime_error("no such relation for the selection");
        s.addConstraint(choices[i].constraint);
        selection_.clear();
        changed = true;
    } else if (event == "dimension") {
        auto choices = dimensionChoices(s, dimensionPicks_);
        size_t i = size_t(e.value("option", 0));
        if (i >= choices.size()) throw std::runtime_error("nothing to dimension: pick geometry first");
        SketchConstraint c = choices[i].constraint;
        c.value = e.value("value", c.value);
        if (!(c.value > 0) && c.type != ConstraintType::Angle) throw std::runtime_error("a dimension must be positive");
        int idx = s.addConstraint(c);
        extra["dimension"] = s.constraints[size_t(idx)].name;
        dimensionPicks_.clear();
        changed = true;
    } else {
        throw std::runtime_error("unknown sketch event " + event);
    }

    json r = {{"tool", toString(tool_)}, {"changed", changed}};
    for (auto& [k, v] : extra.items()) r[k] = v;
    if (changed) {
        SolveResult sr = s.solve();
        r["status"] = toString(sr.status);
        r["degrees_of_freedom"] = sr.degreesOfFreedom;
    }
    Snap hover = snap(s, lastCursor_.x, lastCursor_.y, tol, tool_ == Tool::Line || tool_ == Tool::Centerline);
    r["snap"] = hover.kind.empty() ? json(nullptr) : json{{"x", hover.at.x}, {"y", hover.at.y}, {"kind", hover.kind}};
    r["preview"] = preview(s, hover.at);
    json sel = json::array();
    for (auto& it : selection_) sel.push_back({{"kind", kindName(it.kind)}, {"index", it.index}});
    r["selection"] = sel;
    r["relations"] = selectionChoices(s);
    r["dimension_request"] = dimensionRequest(s);

    static const char* prompts[] = {"Select geometry to add relations, or press Delete",
                                    "Click the start point of the line",
                                    "Click the start point of the centerline",
                                    "Click the first corner",
                                    "Click the centre of the circle",
                                    "Click the centre of the arc",
                                    "Select a line, circle, arc or two points to dimension"};
    std::string prompt = prompts[int(tool_)];
    if (!pending_.empty()) {
        switch (tool_) {
            case Tool::Line: prompt = "Click the next point; press Esc to finish"; break;
            case Tool::Centerline: prompt = "Click the end point"; break;
            case Tool::Rectangle: prompt = "Click the opposite corner"; break;
            case Tool::Circle: prompt = "Click a point on the circle"; break;
            case Tool::Arc: prompt = pending_.size() == 1 ? "Click the start of the arc" : "Click the end of the arc"; break;
            default: break;
        }
    } else if (tool_ == Tool::Dimension && dimensionPicks_.size() == 1 && dimensionPicks_[0].kind == RefKind::Point) {
        prompt = "Select a second point";
    }
    r["prompt"] = prompt;
    return r;
}

json SketchEditor::click(Sketch& s, const Snap& at, double tol, bool add, double x, double y) {
    json r = {{"changed", false}};
    switch (tool_) {
        case Tool::Select: {
            Pick p = pickAt(s, {x, y}, tol);
            if (p.index < 0) {
                if (!add) selection_.clear();
                break;
            }
            Item it{p.kind, p.index};
            auto found = std::find(selection_.begin(), selection_.end(), it);
            if (add) {
                if (found != selection_.end()) selection_.erase(found);
                else selection_.push_back(it);
            } else {
                selection_ = {it};
            }
            break;
        }
        case Tool::Line:
        case Tool::Centerline: {
            int p = pointFor(s, at);
            r["changed"] = true;
            if (pending_.empty()) {
                pending_ = {p};
                chainStart_ = p;
                break;
            }
            int last = pending_.back();
            if (p == last) break;
            int l = s.addLine(last, p, tool_ == Tool::Centerline);
            if (at.kind == "horizontal") s.horizontal(l);
            else if (at.kind == "vertical") s.vertical(l);
            r["line"] = l;
            // A centerline is a single segment; a line chain ends when it closes.
            if (tool_ == Tool::Centerline || p == chainStart_) {
                pending_.clear();
                chainStart_ = -1;
            } else {
                pending_ = {p};
            }
            break;
        }
        case Tool::Rectangle: {
            int p = pointFor(s, at);
            r["changed"] = true;
            if (pending_.empty()) {
                pending_ = {p};
                break;
            }
            Vec2 a = pt(s, pending_[0]), c = pt(s, p);
            if (std::abs(a.x - c.x) < 1e-9 || std::abs(a.y - c.y) < 1e-9) break;  // no area yet
            int corners[4] = {pending_[0], pointFor(s, {{c.x, a.y}, -1, ""}), p, pointFor(s, {{a.x, c.y}, -1, ""})};
            json lines = json::array();
            for (int i = 0; i < 4; ++i) {
                int l = s.addLine(corners[i], corners[(i + 1) % 4]);
                if (i % 2 == 0) s.horizontal(l);
                else s.vertical(l);
                lines.push_back(l);
            }
            r["lines"] = lines;
            pending_.clear();
            break;
        }
        case Tool::Circle: {
            if (pending_.empty()) {
                pending_ = {pointFor(s, at)};
                r["changed"] = true;
                break;
            }
            double radius = dist(pt(s, pending_[0]), at.at);
            if (radius < 1e-6) break;
            r["circle"] = s.addCircle(pending_[0], radius);
            r["changed"] = true;
            pending_.clear();
            break;
        }
        case Tool::Arc: {
            if (pending_.size() < 2) {
                int p = pointFor(s, at);
                if (pending_.size() == 1 && p == pending_[0]) break;
                pending_.push_back(p);
                r["changed"] = true;
                break;
            }
            Vec2 c = pt(s, pending_[0]), st = pt(s, pending_[1]);
            double radius = dist(c, st), ang = std::atan2(at.at.y - c.y, at.at.x - c.x);
            Vec2 end{c.x + radius * std::cos(ang), c.y + radius * std::sin(ang)};
            int e = (at.point >= 0 && std::abs(dist(c, at.at) - radius) < tol) ? at.point : pointFor(s, {end, -1, ""});
            if (e == pending_[1]) break;
            r["arc"] = s.addArc(pending_[0], pending_[1], e);
            r["changed"] = true;
            pending_.clear();
            break;
        }
        case Tool::Dimension: {
            Pick p = pickAt(s, {x, y}, tol);
            if (p.index < 0) {
                dimensionPicks_.clear();
                break;
            }
            Item it{p.kind, p.index};
            bool pairs = dimensionPicks_.size() == 1 && dimensionPicks_[0].kind == it.kind &&
                         (it.kind == RefKind::Point || it.kind == RefKind::Line) && !(dimensionPicks_[0] == it);
            if (pairs) dimensionPicks_.push_back(it);
            else dimensionPicks_ = {it};
            break;
        }
    }
    return r;
}

json SketchEditor::preview(const Sketch& s, const Vec2& cur) const {
    json lines = json::array(), circles = json::array(), arcs = json::array();
    auto line = [&](Vec2 a, Vec2 b, bool construction) {
        lines.push_back({{"x1", a.x}, {"y1", a.y}, {"x2", b.x}, {"y2", b.y}, {"construction", construction}});
    };
    if (!pending_.empty()) {
        Vec2 a = pt(s, pending_[0]);
        switch (tool_) {
            case Tool::Line:
            case Tool::Centerline: line(pt(s, pending_.back()), cur, tool_ == Tool::Centerline); break;
            case Tool::Rectangle:
                line(a, {cur.x, a.y}, false);
                line({cur.x, a.y}, cur, false);
                line(cur, {a.x, cur.y}, false);
                line({a.x, cur.y}, a, false);
                break;
            case Tool::Circle: circles.push_back({{"cx", a.x}, {"cy", a.y}, {"r", dist(a, cur)}}); break;
            case Tool::Arc:
                if (pending_.size() == 1) {
                    line(a, cur, true);
                } else {
                    Vec2 st = pt(s, pending_[1]);
                    double a0 = std::atan2(st.y - a.y, st.x - a.x), a1 = std::atan2(cur.y - a.y, cur.x - a.x);
                    arcs.push_back({{"cx", a.x}, {"cy", a.y}, {"r", dist(a, st)}, {"start_angle", a0 * 180 / kPi},
                                    {"end_angle", (a0 + ccw(a0, a1)) * 180 / kPi}});
                }
                break;
            default: break;
        }
    }
    return {{"lines", lines}, {"circles", circles}, {"arcs", arcs}};
}

json SketchEditor::selectionChoices(const Sketch& s) const {
    json out = json::array();
    for (auto& c : relationChoices(s, selection_)) out.push_back(c.label);
    return out;
}

json SketchEditor::dimensionRequest(const Sketch& s) const {
    auto choices = dimensionChoices(s, dimensionPicks_);
    if (choices.empty()) return nullptr;
    json options = json::array();
    for (auto& c : choices)
        options.push_back({{"label", c.label}, {"value", c.constraint.value},
                           {"unit", c.constraint.type == ConstraintType::Angle ? "deg" : "mm"}});
    return {{"options", options}};
}

}  // namespace stawn

// STAWN CAD Pro - interactive sketching shared by the Mac and Windows apps.
//
// The apps forward mouse and keyboard input in sketch coordinates (mm on the sketch plane)
// and draw what comes back: the rubber-band preview, the snap marker, the prompt, and the
// relation and dimension choices for the current selection. Geometry is added to the sketch
// as the user clicks, the same way the sketch tools work in other feature-based CAD apps:
//
//   {"event": "tool", "tool": "line"}
//   {"event": "move", "x": 12.3, "y": 4.5, "tol": 0.8}     tol: snap distance in mm
//   {"event": "click", "x": 12.3, "y": 4.5, "tol": 0.8, "add": false}
//   {"event": "escape"}   ends a line chain, clears the selection, then returns to Select
//   {"event": "delete"}   deletes the selected geometry and relations
#pragma once

#include <nlohmann/json.hpp>
#include <string>
#include <vector>

#include "stawn/Sketch.hpp"

namespace stawn {

class SketchEditor {
public:
    enum class Tool { Select, Line, Centerline, Rectangle, Circle, Arc, Dimension };

    struct Item {
        RefKind kind;
        int index;
        bool constraint = false;  // a relation or dimension rather than geometry
        bool operator==(const Item& o) const { return kind == o.kind && index == o.index && constraint == o.constraint; }
    };

    // Handles one input event and returns the editor state (see the header comment).
    // "changed" is true when the sketch geometry or relations changed.
    nlohmann::json input(Sketch& sketch, const nlohmann::json& event);
    void reset();
    Tool tool() const { return tool_; }

private:
    struct Snap {
        Vec2 at;
        int point = -1;  // existing point snapped to, or -1
        std::string kind;  // "point", "origin", "horizontal", "vertical" or ""
    };

    Snap snap(const Sketch& s, double x, double y, double tol, bool inferAxis) const;
    nlohmann::json click(Sketch& s, const Snap& at, double tol, bool add, double x, double y);
    nlohmann::json preview(const Sketch& s, const Vec2& cursor) const;
    nlohmann::json selectionChoices(const Sketch& s) const;
    nlohmann::json dimensionRequest(const Sketch& s) const;
    int pointFor(Sketch& s, const Snap& at);
    void clearPending();

    Tool tool_ = Tool::Select;
    std::vector<int> pending_;  // points placed so far by the active tool
    int chainStart_ = -1;
    std::vector<Item> selection_;
    std::vector<Item> dimensionPicks_;
    Vec2 lastCursor_;
};

const char* toString(SketchEditor::Tool t);

}  // namespace stawn

// STAWN CAD Pro - command API. Every modelling action the app offers is also a JSON command,
// so parts can be built from scripts, the command line or an AI assistant through MCP.
//
//   {"op": "create_sketch", "plane": "Front"}
//   {"op": "draw_circle", "cx": 0, "cy": 0, "r": 30}
//   {"op": "extrude", "depth": 15}
//   {"op": "fillet", "radius": 2, "edges": "all"}
//
// Each command returns {"ok": true, ...} or {"ok": false, "error": "..."}. See
// docs/COMMANDS.md for the full list.
#pragma once

#include <memory>
#include <nlohmann/json.hpp>
#include <string>

#include "stawn/Document.hpp"

namespace stawn {

class CommandSession {
public:
    CommandSession();
    explicit CommandSession(std::unique_ptr<PartDocument> doc);
    // Works on a document owned elsewhere (the app's or the C API's), replacing it on new_part and open.
    explicit CommandSession(std::unique_ptr<PartDocument>* externalSlot);

    nlohmann::json execute(const nlohmann::json& command);
    // Runs a list of commands, stopping at the first failure. Returns the list of results.
    nlohmann::json run(const nlohmann::json& commands);

    PartDocument& document() { return **slot_; }
    std::unique_ptr<PartDocument> release() { return std::move(*slot_); }
    void reset(std::unique_ptr<PartDocument> doc);

    static nlohmann::json describe();  // op names with their parameters, for help and MCP

private:
    Sketch& activeSketch();
    int pointAt(double x, double y);  // reuses an existing sketch point at the same spot
    PartDocument& doc() { return **slot_; }
    std::unique_ptr<PartDocument> owned_;
    std::unique_ptr<PartDocument>* slot_;
    std::string activeSketch_;
    std::string lastSketch_;
};

}  // namespace stawn

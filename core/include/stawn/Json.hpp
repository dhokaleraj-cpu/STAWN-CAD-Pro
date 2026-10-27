// STAWN CAD Pro - JSON forms of sketches and property templates.
#pragma once

#include <nlohmann/json.hpp>

#include "stawn/Sketch.hpp"

namespace stawn {

nlohmann::json sketchToJson(const Sketch& s);
Sketch sketchFromJson(const nlohmann::json& j);

}  // namespace stawn

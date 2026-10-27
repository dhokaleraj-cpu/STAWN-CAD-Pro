// STAWN CAD Pro - bring parts over from another CAD system.
//
// The Windows export bridge (tools/solidworks_bridge) writes, for each part, one STEP file per
// configuration and a manifest with the configurations, custom properties and material.
// partFromManifest turns that into a STAWN part: one imported body per configuration, each
// shown only in its own configuration, with the properties and material carried over.
#pragma once

#include <memory>
#include <string>

#include "stawn/Document.hpp"

namespace stawn {

std::unique_ptr<PartDocument> partFromManifest(const std::string& manifestPath, std::string* error = nullptr);

// Maps a property expression from the source system to a STAWN link where one exists,
// e.g. "SW-Mass@Part.SLDPRT" -> "${Mass}"; anything else is kept as its resolved text.
std::string translatePropertyValue(const std::string& expression, const std::string& resolved);

}  // namespace stawn

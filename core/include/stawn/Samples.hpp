// STAWN CAD Pro - sample documents used by tests, the command line tool and the app's
// "New from sample" menu.
#pragma once

#include <memory>

#include "stawn/Document.hpp"

namespace stawn {

// A stepped 8 mm shaft built the same way as the recording: a fully defined half profile
// on the Front plane, revolved about its axis, with end chamfers and neck fillets, a
// "Finish Grinding" configuration and heat treatment custom properties.
std::unique_ptr<PartDocument> makeSampleShaft();

}  // namespace stawn

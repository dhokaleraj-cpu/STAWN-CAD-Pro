#include <doctest/doctest.h>

#include <cstdio>
#include <fstream>
#include <sstream>

#include "stawn/Drawing.hpp"
#include "stawn/Samples.hpp"

using namespace stawn;

TEST_CASE("standard drawing has views, title block and linked properties") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->rebuild().ok);
    DrawingSpec spec = DrawingSpec::standardThreeView(*doc, "A3");
    CHECK(spec.views.size() == 4);
    CHECK(spec.scale == doctest::Approx(2));
    Sheet2D sheet = renderDrawing(*doc, spec);

    auto hasText = [&](const std::string& t) {
        for (auto& x : sheet.texts)
            if (x.text == t) return true;
        return false;
    };
    CHECK(hasText("DM400002"));
    CHECK(hasText("1.7147 (20MnCr5)"));
    CHECK(hasText("2:1"));
    CHECK(hasText("Finish Grinding"));
    CHECK(hasText("46.9"));  // overall length dimension
    CHECK(hasText("\u00D88"));  // overall diameter dimension
    CHECK(hasText("3. CASE DEPTH: 0.5-1.0 AT 513 HV1"));

    // Every view's geometry stays on the sheet.
    for (auto& pl : sheet.lines)
        for (size_t i = 0; i + 1 < pl.xy.size(); i += 2) {
            CHECK(pl.xy[i] >= 0);
            CHECK(pl.xy[i] <= 420);
            CHECK(pl.xy[i + 1] >= 0);
            CHECK(pl.xy[i + 1] <= 297);
        }
}

TEST_CASE("drawing exports") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->rebuild().ok);
    Sheet2D sheet = renderDrawing(*doc, DrawingSpec::standardThreeView(*doc));
    REQUIRE(exportPDF(sheet, "test_drawing.pdf"));
    REQUIRE(exportDXF(sheet, "test_drawing.dxf"));
    std::ifstream pdf("test_drawing.pdf", std::ios::binary);
    std::stringstream ss;
    ss << pdf.rdbuf();
    CHECK(ss.str().rfind("%PDF-1.4", 0) == 0);
    CHECK(ss.str().find("%%EOF") != std::string::npos);
    CHECK(toSVG(sheet).find("<svg") == 0);
    std::remove("test_drawing.pdf");
    std::remove("test_drawing.dxf");
}

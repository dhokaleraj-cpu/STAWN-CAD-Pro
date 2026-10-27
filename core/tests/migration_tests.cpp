#include <doctest/doctest.h>

#include <cstdio>
#include <fstream>

#include "stawn/Migration.hpp"
#include "stawn/Samples.hpp"

using namespace stawn;

TEST_CASE("property expressions map to STAWN links") {
    CHECK(translatePropertyValue("\"SW-Mass@DM400002.SLDPRT\"", "9.09") == "${Mass}");
    CHECK(translatePropertyValue("SW-Material@Part1.SLDPRT", "1.7147") == "${Material}");
    CHECK(translatePropertyValue("SW-Massive", "x") == "x");
    CHECK(translatePropertyValue("MIN 50 HRC", "MIN 50 HRC") == "MIN 50 HRC");
    CHECK(translatePropertyValue("Plain", "") == "Plain");
}

TEST_CASE("a manifest with two configurations becomes a part with one body each") {
    auto shaft = makeSampleShaft();
    REQUIRE(shaft->rebuild().ok);
    double finish = shaft->massProperties().volume;
    REQUIRE(shaft->exportSTEP("mig_finish.step"));
    shaft->activeConfiguration = "Forging";
    REQUIRE(shaft->rebuild().ok);
    double forging = shaft->massProperties().volume;
    REQUIRE(shaft->exportSTEP("mig_forging.step"));

    {
        std::ofstream m("mig_manifest.json");
        m << R"M({
          "source": "C:\\SOLIDWORKS Data\\DM400002_8mm_Shaft.SLDPRT",
          "name": "DM400002_8mm_Shaft",
          "material": {"name": "1.7147 (20MnCr5)", "density": 7850},
          "properties": {
            "PartNo": {"expression": "DM400002", "resolved": "DM400002"},
            "Weight": {"expression": "\"SW-Mass@DM400002_8mm_Shaft.SLDPRT\"", "resolved": "9.09"},
            "CaseDepth": "0.5-1.0 AT 513 HV1"
          },
          "activeConfiguration": "Forging",
          "configurations": [
            {"name": "Finish Grinding", "description": "Finish", "step": "mig_finish.step",
             "properties": {"Process": {"expression": "Ground", "resolved": "Ground"}}},
            {"name": "Forging", "step": "mig_forging.step", "properties": {"Process": "Forged"}}
          ]
        })M";
    }
    std::string err;
    auto doc = partFromManifest("mig_manifest.json", &err);
    REQUIRE_MESSAGE(doc, err);
    CHECK(doc->configurations.size() == 2);
    CHECK(doc->activeConfiguration == "Forging");
    REQUIRE(doc->rebuild().ok);
    CHECK(doc->massProperties().volume == doctest::Approx(forging).epsilon(1e-4));
    CHECK(doc->property("Process") == "Forged");
    CHECK(doc->property("PartNo") == "DM400002");
    CHECK(std::stod(doc->property("Weight")) == doctest::Approx(doc->massProperties().mass).epsilon(0.01));

    doc->activeConfiguration = "Finish Grinding";
    REQUIRE(doc->rebuild().ok);
    CHECK(doc->massProperties().volume == doctest::Approx(finish).epsilon(1e-4));
    CHECK(doc->property("Process") == "Ground");
    CHECK(doc->properties.at("SourceFile").find("SLDPRT") != std::string::npos);

    CHECK_FALSE(partFromManifest("missing.json", &err));
    for (const char* f : {"mig_finish.step", "mig_forging.step", "mig_manifest.json"}) std::remove(f);
}

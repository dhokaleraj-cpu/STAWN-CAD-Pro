#include <doctest/doctest.h>

#include <cmath>
#include <cstdio>
#include <fstream>

#include "stawn/Document.hpp"
#include "stawn/Samples.hpp"

using namespace stawn;

namespace {
constexpr double kPi = 3.14159265358979323846;
// Plain stepped shaft volume: pi * sum(r^2 * length).
double steppedVolume(double collarRadius) {
    return kPi * (4 * 6.75 * 2 + collarRadius * collarRadius * 8.55 * 2 + 9 * 16.3);
}
}  // namespace

TEST_CASE("extruded rectangle with a cut hole") {
    PartDocument doc;
    Feature& sk = doc.addSketch(StandardPlane::Front);
    Sketch& s = sk.sketch;
    int b = s.addPoint(40, 0), c = s.addPoint(40, 20), d = s.addPoint(0, 20);
    s.addLine(0, b);
    s.addLine(b, c);
    s.addLine(c, d);
    s.addLine(d, 0);
    doc.addExtrude(sk.name, 10);

    Feature& hole = doc.addSketch(StandardPlane::Front);
    hole.sketch.addCircle(hole.sketch.addPoint(20, 10), 5);
    Feature& cut = doc.addExtrude(hole.name, 10, Operation::Cut);
    (void)cut;

    RebuildResult r = doc.rebuild();
    REQUIRE(r.ok);
    CHECK(doc.massProperties().volume == doctest::Approx(40 * 20 * 10 - kPi * 25 * 10).epsilon(1e-6));
}

TEST_CASE("sketch with an inner loop extrudes as a face with a hole") {
    PartDocument doc;
    Feature& sk = doc.addSketch(StandardPlane::Top);
    sk.sketch.addCircle(sk.sketch.addPoint(0, 0), 10);
    sk.sketch.addCircle(sk.sketch.addPoint(0, 0), 4);
    doc.addExtrude(sk.name, 5);
    REQUIRE(doc.rebuild().ok);
    CHECK(doc.massProperties().volume == doctest::Approx(kPi * (100 - 16) * 5).epsilon(1e-6));
    CHECK(doc.massProperties().cy == doctest::Approx(2.5));  // Top plane extrudes along +Y
}

TEST_CASE("sample shaft rebuilds in both configurations") {
    auto doc = makeSampleShaft();
    for (auto& f : doc->features)
        if (f.type == FeatureType::Sketch) CHECK((Sketch(f.sketch).solve().status == SketchStatus::FullyDefined));

    RebuildResult r = doc->rebuild();
    INFO(r.errors.size());
    REQUIRE(r.ok);
    double finish = doc->massProperties().volume;
    // Chamfers remove material, neck fillets add a little.
    CHECK(finish == doctest::Approx(steppedVolume(4)).epsilon(0.01));
    CHECK(finish != doctest::Approx(steppedVolume(4)).epsilon(1e-6));
    CHECK(doc->massProperties().cx == doctest::Approx(46.9 / 2).epsilon(1e-3));

    doc->activeConfiguration = "Forging";
    REQUIRE(doc->rebuild().ok);
    CHECK(doc->massProperties().volume == doctest::Approx(steppedVolume(4.3)).epsilon(1e-6));
    // The configuration override never touches the stored model value.
    doc->activeConfiguration = "Finish Grinding";
    CHECK(doc->getDimension("D2@Sketch1").value() == doctest::Approx(4));
}

TEST_CASE("dimension changes flow through the feature tree") {
    auto doc = makeSampleShaft();
    doc->activeConfiguration = "Forging";
    CHECK(doc->getDimension("D2@Sketch1").value() == doctest::Approx(4.3));
    CHECK(doc->setDimension("D3@Sketch1", 2.5, true));  // this configuration only
    REQUIRE(doc->rebuild().ok);
    double forging = doc->massProperties().volume;
    CHECK(forging == doctest::Approx(kPi * (4 * 6.75 * 2 + 4.3 * 4.3 * 8.55 * 2 + 6.25 * 16.3)).epsilon(1e-6));

    doc->activeConfiguration = "Finish Grinding";
    CHECK(doc->getDimension("D3@Sketch1").value() == doctest::Approx(3));
    CHECK(doc->setDimension("D1@Revolve1", 180));
    REQUIRE(doc->rebuild().ok);
    CHECK(doc->massProperties().volume == doctest::Approx(steppedVolume(4) / 2).epsilon(0.02));
}

TEST_CASE("rollback and suppression") {
    auto doc = makeSampleShaft();
    doc->rollbackIndex = 2;  // Sketch1 and Revolve1 only
    REQUIRE(doc->rebuild().ok);
    CHECK(doc->massProperties().volume == doctest::Approx(steppedVolume(4)).epsilon(1e-6));
    doc->rollbackIndex = -1;
    doc->setSuppressed("Revolve1", true);
    RebuildResult r = doc->rebuild();
    CHECK_FALSE(r.ok);  // chamfer and fillet have no body
    CHECK(doc->shape().IsNull());
}

TEST_CASE("custom properties resolve links and configuration values") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->rebuild().ok);
    CHECK(doc->property("Material") == "1.7147 (20MnCr5)");
    CHECK(doc->property("PartName") == "DM400002_8mm_Shaft");
    double grams = doc->massProperties().mass;
    CHECK(grams == doctest::Approx(doc->massProperties().volume * 7850e-6));
    CHECK(std::stod(doc->property("Weight")) == doctest::Approx(grams).epsilon(0.01));
    doc->configuration("Forging").properties["Process"] = "Hot Forged";
    CHECK(doc->property("Process") == "Carburized");
    doc->activeConfiguration = "Forging";
    CHECK(doc->property("Process") == "Hot Forged");
}

TEST_CASE("renaming a feature keeps references and configuration data") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->renameFeature("Sketch1", "Profile"));
    CHECK(doc->feature("Revolve1")->sketchName == "Profile");
    CHECK(doc->findConfiguration("Forging")->dimensions.count("D2@Profile") == 1);
    REQUIRE(doc->renameFeature("Chamfer1", "EndChamfers"));
    CHECK(doc->findConfiguration("Forging")->suppressedFeatures.count("EndChamfers") == 1);
    CHECK(doc->rebuild().ok);
}

TEST_CASE("part file round trip") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->rebuild().ok);
    double v = doc->massProperties().volume;
    std::string err;
    auto copy = PartDocument::fromJSON(doc->toJSON(), &err);
    REQUIRE_MESSAGE(copy, err);
    CHECK(copy->configurations.size() == 2);
    CHECK(copy->activeConfiguration == "Finish Grinding");
    CHECK(copy->properties.at("CaseDepth") == "0.5-1.0 AT 513 HV1");
    REQUIRE(copy->rebuild().ok);
    CHECK(copy->massProperties().volume == doctest::Approx(v).epsilon(1e-9));
    CHECK_FALSE(PartDocument::fromJSON("{\"format\":\"other\"}", &err));
}

TEST_CASE("STEP and STL export") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->rebuild().ok);
    const char* step = "test_shaft.step";
    const char* stl = "test_shaft.stl";
    REQUIRE(doc->exportSTEP(step));
    REQUIRE(doc->exportSTL(stl));
    std::ifstream f(step);
    std::string first;
    std::getline(f, first);
    CHECK(first.rfind("ISO-10303-21", 0) == 0);
    std::remove(step);
    std::remove(stl);
}

TEST_CASE("STEP import brings geometry into a new part and survives saving") {
    auto doc = makeSampleShaft();
    REQUIRE(doc->rebuild().ok);
    double v = doc->massProperties().volume;
    REQUIRE(doc->exportSTEP("roundtrip.step"));

    PartDocument imported;
    std::string err;
    Feature* f = imported.importSTEP("roundtrip.step", &err);
    REQUIRE_MESSAGE(f, err);
    CHECK(f->name == "Imported1");
    CHECK(f->sourceFile == "roundtrip.step");
    REQUIRE(imported.rebuild().ok);
    CHECK(imported.massProperties().volume == doctest::Approx(v).epsilon(1e-4));

    auto copy = PartDocument::fromJSON(imported.toJSON(), &err);
    REQUIRE(copy);
    REQUIRE(copy->rebuild().ok);
    CHECK(copy->massProperties().volume == doctest::Approx(v).epsilon(1e-4));
    CHECK_FALSE(imported.importSTEP("missing.step", &err));
    std::remove("roundtrip.step");
}

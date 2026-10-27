#include "stawn/Samples.hpp"

namespace stawn {

std::unique_ptr<PartDocument> makeSampleShaft() {
    auto doc = std::make_unique<PartDocument>();
    doc->name = "DM400002_8mm_Shaft";
    doc->material = *findMaterial("1.7147 (20MnCr5)");

    Feature& sk = doc->addSketch(StandardPlane::Front);
    Sketch& s = sk.sketch;

    // Half profile above the axis, starting at the origin (point 0).
    const double xs[] = {0, 6.75, 6.75, 15.3, 15.3, 31.6, 31.6, 40.15, 40.15, 46.9, 46.9};
    const double ys[] = {2, 2, 4, 4, 3, 3, 4, 4, 2, 2, 0};
    std::vector<int> p = {0};
    for (int i = 0; i < 11; ++i) p.push_back(s.addPoint(xs[i] + 0.3, ys[i] + 0.2));  // rough sketch

    std::vector<int> lines;
    for (size_t i = 0; i + 1 < p.size(); ++i) lines.push_back(s.addLine(p[i], p[i + 1]));
    int axis = s.addLine(p.back(), p[0]);  // the bottom edge lies on the axis of revolution
    lines.push_back(axis);

    for (size_t i = 0; i < lines.size(); ++i) {
        if (i % 2 == 0) s.vertical(lines[i]);
        else s.horizontal(lines[i]);
    }

    // Radii from the axis (shown as diameters on the drawing) and lengths from the left end.
    s.dimension(ConstraintType::VerticalDistance, 0, p[1], 2);   // D1  journal
    s.dimension(ConstraintType::VerticalDistance, 0, p[3], 4);   // D2  collar
    s.dimension(ConstraintType::VerticalDistance, 0, p[5], 3);   // D3  neck
    s.dimension(ConstraintType::VerticalDistance, 0, p[7], 4);   // D4  collar
    s.dimension(ConstraintType::VerticalDistance, 0, p[9], 2);   // D5  journal
    s.dimension(ConstraintType::HorizontalDistance, 0, p[2], 6.75);   // D6
    s.dimension(ConstraintType::HorizontalDistance, 0, p[4], 15.3);   // D7
    s.dimension(ConstraintType::HorizontalDistance, 0, p[6], 31.6);   // D8
    s.dimension(ConstraintType::HorizontalDistance, 0, p[8], 40.15);  // D9
    s.dimension(ConstraintType::HorizontalDistance, 0, p[10], 46.9);  // D10 overall length

    doc->addRevolve(sk.name, axis, 360);
    doc->addChamfer({{0, 2, 0}, {46.9, 2, 0}}, 0.3);
    doc->addFillet({{15.3, 3, 0}, {31.6, 3, 0}}, 0.5);

    doc->properties = {
        {"PartNo", "DM400002"},
        {"PartName", "${PartName}"},
        {"Description", "8mm Shaft"},
        {"Process", "Carburized"},
        {"Material", "${Material}"},
        {"Weight", "${Mass}"},
        {"JominyValue", "NA"},
        {"HeatTreatment", "Case Carburising"},
        {"CaseDepth", "0.5-1.0 AT 513 HV1"},
        {"SurfaceHardness", "MIN 50 HRC"},
        {"CoreHardness", "MAX 40 HRC"},
    };

    doc->renameConfiguration("Default", "Finish Grinding");
    doc->configuration("Finish Grinding").description = "Finish Grinding";
    // A forging configuration without the chamfers and fillets, with grinding stock left on.
    Configuration& forging = doc->configuration("Forging");
    forging.description = "Forging";
    forging.suppressedFeatures = {"Chamfer1", "Fillet1"};
    forging.dimensions["D2@Sketch1"] = 4.3;
    forging.dimensions["D4@Sketch1"] = 4.3;
    doc->activeConfiguration = "Finish Grinding";
    return doc;
}

}  // namespace stawn

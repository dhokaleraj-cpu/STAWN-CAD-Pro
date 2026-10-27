// STAWN CAD Pro - part document: feature tree, configurations, properties, regeneration.
#pragma once

#include <TopoDS_Shape.hxx>
#include <gp_Ax3.hxx>

#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <vector>

#include "stawn/Sketch.hpp"

namespace stawn {

enum class StandardPlane { Front, Top, Right };

const char* toString(StandardPlane p);
std::optional<StandardPlane> planeFromString(const std::string& s);
gp_Ax3 planeAxes(StandardPlane p);

enum class FeatureType { Sketch, Extrude, Revolve, Fillet, Chamfer, Import };
const char* toString(FeatureType t);

enum class Operation { Boss, Cut };

// A reference to an edge of the body that survives regeneration. Edges are found again by
// the point they pass nearest to, which keeps fillets and chamfers attached when an
// upstream dimension changes the model without changing its topology.
struct EdgeRef {
    double x = 0, y = 0, z = 0;
};

struct Feature {
    std::string name;
    FeatureType type = FeatureType::Sketch;
    bool suppressed = false;

    // Sketch
    StandardPlane plane = StandardPlane::Front;
    Sketch sketch;

    // Extrude and revolve
    std::string sketchName;  // the profile sketch
    Operation operation = Operation::Boss;
    double depth = 10;        // extrude depth in mm (D1@Extrude)
    bool midPlane = false;
    bool reverse = false;
    double angle = 360;       // revolve angle in degrees (D1@Revolve)
    int axisLine = -1;        // index of the sketch line used as the revolve axis

    // Fillet and chamfer
    double radius = 1;        // fillet radius or chamfer distance (D1@Fillet / D1@Chamfer)
    std::vector<EdgeRef> edges;

    // Import: a body read from a STEP file, stored inside the part as B-rep text
    std::string sourceFile;
    std::string brep;

    std::string error;        // set when the feature failed to rebuild
};

struct Material {
    std::string name;
    double density = 7850;  // kg/m^3
};

const std::vector<Material>& materialLibrary();
std::optional<Material> findMaterial(const std::string& name);

struct Configuration {
    std::string name;
    std::string description;
    std::string bomPartNumber;               // empty means the document name
    std::set<std::string> suppressedFeatures;
    std::map<std::string, double> dimensions;  // "D1@Sketch1" -> value
    std::map<std::string, std::string> properties;  // configuration-specific custom properties
};

struct MassProperties {
    double volume = 0;   // mm^3
    double area = 0;     // mm^2
    double mass = 0;     // g
    double cx = 0, cy = 0, cz = 0;  // centre of mass, mm
};

struct RebuildResult {
    bool ok = true;
    std::vector<std::string> errors;
};

class PartDocument {
public:
    PartDocument();

    std::string name = "Part1";
    std::string units = "MMGS";
    Material material{"", 7850};

    std::vector<Feature> features;
    std::vector<Configuration> configurations;
    std::string activeConfiguration = "Default";
    std::map<std::string, std::string> properties;  // file-level custom properties

    // Rollback bar: only features before this index are rebuilt. -1 means all.
    int rollbackIndex = -1;

    Feature& addSketch(StandardPlane plane, const std::string& name = "");
    Feature& addExtrude(const std::string& sketch, double depth, Operation op = Operation::Boss,
                        const std::string& name = "");
    Feature& addRevolve(const std::string& sketch, int axisLine, double angle = 360,
                        Operation op = Operation::Boss, const std::string& name = "");
    Feature& addFillet(std::vector<EdgeRef> edges, double radius, const std::string& name = "");
    Feature& addChamfer(std::vector<EdgeRef> edges, double distance, const std::string& name = "");
    // Reads a STEP file as an imported body (geometry only, no feature history).
    Feature* importSTEP(const std::string& path, std::string* error = nullptr);

    Feature* feature(const std::string& name);
    const Feature* feature(const std::string& name) const;
    bool renameFeature(const std::string& from, const std::string& to);
    bool deleteFeature(const std::string& name);

    Configuration& configuration(const std::string& name);  // creates if missing
    Configuration* findConfiguration(const std::string& name);
    const Configuration* activeConfig() const;
    bool renameConfiguration(const std::string& from, const std::string& to);

    // Dimension access by full name, e.g. "D1@Sketch1" or "D1@Revolve1". Setting a
    // dimension writes it into the active configuration when that configuration already
    // overrides it, and into the model otherwise.
    std::optional<double> getDimension(const std::string& fullName) const;
    bool setDimension(const std::string& fullName, double value, bool thisConfigurationOnly = false);
    std::vector<std::pair<std::string, double>> listDimensions() const;

    bool isSuppressed(const Feature& f) const;
    void setSuppressed(const std::string& feature, bool suppressed, bool thisConfigurationOnly = true);

    // Resolved custom property: configuration-specific value wins over the file value.
    // Values can link to evaluated data with ${Mass}, ${Volume}, ${Material}, ${Density},
    // ${PartName}, ${Configuration} and ${Description}.
    std::string property(const std::string& key);
    std::map<std::string, std::string> resolvedProperties();

    RebuildResult rebuild();
    const TopoDS_Shape& shape() const { return shape_; }
    MassProperties massProperties() const;

    bool exportSTEP(const std::string& path) const;
    bool exportSTL(const std::string& path, double deflection = 0.05) const;

    std::string toJSON() const;
    static std::unique_ptr<PartDocument> fromJSON(const std::string& json, std::string* error = nullptr);
    bool save(const std::string& path) const;
    static std::unique_ptr<PartDocument> load(const std::string& path, std::string* error = nullptr);

private:
    std::string uniqueName(const std::string& base) const;
    std::string expand(const std::string& value);
    TopoDS_Shape shape_;
};

}  // namespace stawn

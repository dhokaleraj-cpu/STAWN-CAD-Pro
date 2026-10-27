// STAWN CAD Pro - 2D drawings: projected views with hidden line removal, title block
// linked to custom properties, and export to SVG, DXF and PDF.
#pragma once

#include <map>
#include <string>
#include <vector>

#include "stawn/Document.hpp"

namespace stawn {

enum class ViewOrientation { Front, Top, Bottom, Right, Left, Back, Isometric };
const char* toString(ViewOrientation v);

enum class ProjectionAngle { First, Third };

struct Paper {
    std::string name = "A3";
    double width = 420, height = 297;  // mm, landscape
};
Paper paperFromName(const std::string& name);  // A4, A3, A2, A1, A0

struct DrawingView {
    std::string name;  // "Drawing View1"
    ViewOrientation orientation = ViewOrientation::Front;
    double x = 150, y = 180;  // centre of the view on the sheet, mm
    double scale = 0;         // 0 means use the sheet scale
    bool showHidden = false;
    bool overallDimensions = false;  // add overall width and height dimensions
    std::string heightPrefix;        // "Ø" for turned parts
};

struct DrawingNote {
    double x = 0, y = 0;
    double height = 3.5;
    std::string text;  // may hold ${Property} links, resolved from the part
};

// A linear dimension placed by the user between two points on the sheet.
struct DrawingDimension {
    double x1 = 0, y1 = 0, x2 = 0, y2 = 0;  // measured points, mm on the sheet
    double offset = 8;                       // distance of the dimension line from the points
    bool horizontal = true;
    std::string prefix;                      // e.g. "Ø"
    std::string overrideText;                // empty means the measured model value
};

struct DrawingSpec {
    std::string sheetName = "Sheet1";
    Paper paper;
    double scale = 2;  // 2 means 2:1
    ProjectionAngle projection = ProjectionAngle::First;
    std::vector<DrawingView> views;
    std::vector<DrawingNote> notes;
    std::vector<DrawingDimension> dimensions;
    std::string revision = "A";
    std::string drawnBy;
    std::string date;
    std::string companyName = "Company Confidential";

    // Front view with projected top and right views placed for the projection angle, plus an
    // isometric view in the upper right, sized for the paper.
    static DrawingSpec standardThreeView(const PartDocument& part, const std::string& paper = "A3");
};

enum class Layer { Visible, Hidden, Thin, Border, Centre };

struct Polyline2D {
    Layer layer = Layer::Visible;
    std::vector<double> xy;  // x0 y0 x1 y1 ...
};

struct Text2D {
    double x = 0, y = 0, height = 3.5;
    std::string text;
    enum class Anchor { Left, Centre, Right } anchor = Anchor::Left;
};

struct Sheet2D {
    Paper paper;
    std::vector<Polyline2D> lines;
    std::vector<Text2D> texts;
};

// Builds the full sheet: border, zones, title block, views and annotations.
Sheet2D renderDrawing(PartDocument& part, const DrawingSpec& spec);

bool exportSVG(const Sheet2D& sheet, const std::string& path);
bool exportDXF(const Sheet2D& sheet, const std::string& path);
bool exportPDF(const Sheet2D& sheet, const std::string& path);
std::string toSVG(const Sheet2D& sheet);

}  // namespace stawn

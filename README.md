# STAWN CAD Pro

A native macOS parametric CAD app for turned and machined parts, following the familiar
feature-tree workflow: sketch, feature, configuration, custom properties, drawing.

## What works today

**Core (C++17 on OpenCascade, tested on Linux and macOS)**
- Sketch solver (Levenberg-Marquardt) with coincident, horizontal, vertical, parallel,
  perpendicular, equal, point on line or circle, midpoint, tangent and fix relations, and
  distance, horizontal and vertical distance, length, radius, diameter and angle dimensions.
  Reports Under Defined, Fully Defined or Over Defined.
- Features: extrude (boss and cut, mid plane, reverse), revolve (boss and cut), fillet,
  chamfer, STEP import (geometry only). Profiles with inner loops become faces with holes.
- Feature tree with rollback, suppression and rename that keeps references.
- Configurations with suppressed features, configuration-specific dimensions and
  configuration-specific custom properties.
- Custom properties with links: `${Mass}`, `${Volume}`, `${Material}`, `${Density}`,
  `${PartName}`, `${Configuration}`, `${Description}`.
- Material library with densities, mass properties.
- `.stprt` part files (JSON), STEP and STL export.
- Drawings: front, top, right and isometric views with hidden line removal, ISO A4 to A0
  sheet with zones, revision table, title block filled from custom properties, general
  notes, overall dimensions, first or third angle. Export to PDF, DXF and SVG.

**Command API, MCP server and SolidWorks bridge**
- 37 JSON commands for sketching, features, configurations, properties and export
  (`docs/COMMANDS.md`), available from `stawn-cli run`, the C API and an MCP server
  (`tools/stawn_mcp`) so Claude can build parts in STAWN directly.
- `tools/solidworks_bridge`: a Windows script that exports SolidWorks parts to STEP per
  configuration with their properties, and `stawn-cli migrate` to turn them into STAWN parts.

**macOS app (SwiftUI, SceneKit viewport)**
- Welcome window with New, Sample Shaft, Open and Recent.
- CommandManager tabs, Feature Tree, Property panel with dimension editing, Configuration
  manager, Custom Properties task pane driven by property templates, status bar.
- Heads-up view toolbar: zoom to fit, standard views, display styles.
- Make Drawing with PDF preview and Save As PDF, DXF or SVG. Export STEP and STL.

**Next**: interactive sketcher in the app, feature dialogs with edge picking, section and
detail views, editable drawing dimensions and GD&T, assemblies. See `docs/PLAN.md`.

## Build the core and run the tests

```sh
# macOS
brew install cmake opencascade eigen nlohmann-json doctest
# Ubuntu 24.04
sudo apt install cmake libocct-*-dev libeigen3-dev nlohmann-json3-dev doctest-dev libtbb-dev

cmake -S . -B build && cmake --build build -j
./build/stawn_tests
./build/stawn-cli sample shaft.stprt
./build/stawn-cli info shaft.stprt Forging
./build/stawn-cli drawing shaft.stprt shaft.pdf A3
```

## Build the Mac app

```sh
brew install xcodegen opencascade eigen nlohmann-json
xcodegen generate
open StawnCADPro.xcodeproj   # then Run
```

On an Intel Mac, change `HOMEBREW_PREFIX` in `project.yml` to `/usr/local`.

## Layout

```
core/include/stawn   public C++ headers (Sketch, Document, Drawing, Mesh, Samples)
core/src             C++ implementation
core/capi            C interface used by Swift (stawn_c.h)
core/tests           doctest unit tests
tools/stawn_cli.cpp  command line tool
tools/stawn_mcp      MCP server for Claude
tools/solidworks_bridge  SolidWorks export for migration (Windows)
app/StawnCADPro      SwiftUI macOS app
templates            custom property templates (.stprp.json)
examples             sample parts
```

## Licences

OpenCascade is LGPL 2.1 with an exception and is linked dynamically. Eigen is MPL 2.0.
nlohmann/json is MIT. STAWN CAD Pro contains no code, icons or assets from other CAD products.

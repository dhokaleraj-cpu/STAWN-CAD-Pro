# STAWN CAD Pro command API

Every modelling action is a JSON command. Run them from a script with `stawn-cli run script.json [out.stprt]`,
from Swift or C with `stawn_doc_execute`, or from Claude through the MCP server in `tools/stawn_mcp`.
Lengths are in mm and angles in degrees. Each command returns `{"ok": true, ...}` or `{"ok": false, "error": "..."}`;
commands that change the model also return `rebuild_ok`, `volume_mm3` and `mass_g`.

## Example

```json
[
 {
  "op": "create_sketch",
  "plane": "Front"
 },
 {
  "op": "draw_circle",
  "cx": 0,
  "cy": 0,
  "r": 30
 },
 {
  "op": "close_sketch"
 },
 {
  "op": "extrude",
  "depth": 15
 },
 {
  "op": "fillet",
  "radius": 2,
  "edges": "all"
 },
 {
  "op": "set_material",
  "name": "1.7147 (20MnCr5)"
 },
 {
  "op": "save",
  "path": "disc.stprt"
 }
]
```

Sketch indices: point 0 is the sketch origin. `draw_*` commands return the indices of the points, lines, circles and arcs they create,
and reuse an existing point when a new end point lands on it, so consecutive lines form a connected profile.

## Commands

| Command | What it does | Parameters |
|---|---|---|
| `new_part` | Start a new empty part | `name`: part name (optional) |
| `open` | Open a .stprt part file | `path`: file path |
| `save` | Save the part as .stprt | `path`: file path |
| `import_step` | Add a STEP file as an imported body | `path`: file path |
| `create_sketch` | Start a sketch on a standard plane | `name`: optional<br>`plane`: Front, Top or Right |
| `draw_line` | Draw a line in the active sketch | `construction`: bool, optional<br>`x1`: mm<br>`x2`: mm<br>`y1`: mm<br>`y2`: mm |
| `draw_centerline` | Draw a construction line, e.g. a revolve axis | `x1`: mm<br>`x2`: mm<br>`y1`: mm<br>`y2`: mm |
| `draw_rectangle` | Draw a corner rectangle with horizontal and vertical relations | `x1`: mm<br>`x2`: mm<br>`y1`: mm<br>`y2`: mm |
| `draw_circle` | Draw a circle | `cx`: mm<br>`cy`: mm<br>`r`: radius mm |
| `draw_arc` | Draw a counter-clockwise arc | `cx`: mm<br>`cy`: mm<br>`end_angle`: deg<br>`r`: mm<br>`start_angle`: deg |
| `draw_polygon` | Draw a regular polygon | `cx`: mm<br>`cy`: mm<br>`r`: circumradius mm<br>`rotation`: deg, optional<br>`sides`: 3 or more |
| `add_relation` | Add a sketch relation | `a`: index<br>`b`: index<br>`kindA`: point|line|circle|arc<br>`kindB`: point|line|circle|arc<br>`type`: coincident, horizontal, vertical, parallel, perpendicular, equalLength, pointOnLine, midpoint, tangentLineArc, fix |
| `add_dimension` | Add a driving sketch dimension | `a`: index<br>`b`: index<br>`kindA`: ...<br>`kindB`: ...<br>`type`: distance, horizontalDistance, verticalDistance, length, radius, diameter, angle<br>`value`: mm or deg |
| `edit_sketch` | Open an existing sketch for editing; returns it as get_sketch does | `sketch`: sketch name |
| `get_sketch` | A sketch's plane, geometry, relations, status and which points are defined | `sketch`: optional, default the active sketch |
| `sketch_input` | Mouse and keyboard input for interactive sketching in the active sketch | `event`: tool, move, click, escape, delete, relation or dimension<br>`tool`: select, line, centerline, rectangle, circle, arc or dimension<br>`x`, `y`: mm<br>`tol`: snap distance mm<br>`add`: bool, add to selection<br>`index`: relation choice<br>`option`: dimension choice<br>`value`: dimension value |
| `close_sketch` | Solve and close the active sketch | none |
| `extrude` | Boss-extrude a sketch | `depth`: mm<br>`mid_plane`: bool<br>`reverse`: bool<br>`sketch`: optional, default last sketch |
| `cut_extrude` | Cut-extrude a sketch | `depth`: mm<br>`mid_plane`: bool<br>`reverse`: bool<br>`sketch`: optional |
| `revolve` | Revolve a sketch about one of its lines | `angle`: deg, default 360<br>`axis_line`: line index, default the first construction line<br>`sketch`: optional |
| `cut_revolve` | Revolve a sketch to remove material | `angle`: deg<br>`axis_line`: line index<br>`sketch`: optional |
| `fillet` | Round edges | `edges`: all, circular, linear, or a list of [x,y,z] points on the edges<br>`radius`: mm |
| `chamfer` | Bevel edges | `distance`: mm<br>`edges`: all, circular, linear, or a list of [x,y,z] points |
| `rebuild` | Rebuild the model | none |
| `list_features` | List the feature tree | none |
| `list_dimensions` | List all driving dimensions with their values | none |
| `set_dimension` | Change a dimension such as D1@Sketch1 | `name`: D1@Sketch1<br>`this_configuration_only`: bool<br>`value`: number |
| `suppress` | Suppress or unsuppress a feature in the active configuration | `feature`: name<br>`suppressed`: bool, default true |
| `rename_feature` | Rename a feature | `from`: old name<br>`to`: new name |
| `add_configuration` | Add a configuration and make it active | `description`: optional<br>`name`: name |
| `set_configuration` | Activate a configuration | `name`: name |
| `list_configurations` | List configurations | none |
| `set_property` | Set a custom property | `configuration_specific`: bool<br>`key`: name<br>`value`: text, may use ${Mass} ${Material} ... |
| `get_properties` | Resolved custom properties of the active configuration | none |
| `set_material` | Set the material from the library, or a custom density | `density`: kg/m3, optional<br>`name`: material name |
| `list_materials` | List the material library | none |
| `mass_properties` | Volume, area, mass and centre of mass | none |
| `export_step` | Export STEP | `path`: file path |
| `export_stl` | Export STL | `path`: file path |
| `export_drawing` | Make the standard drawing and export it | `format`: pdf, dxf or svg (default from extension)<br>`paper`: A4..A0<br>`path`: file path |

## MCP server

```sh
pip install "mcp<2"
cmake -S . -B build && cmake --build build -j   # builds build/libstawn.dylib
```

Add it to Claude Desktop or Claude Code as a stdio server:

```json
{
  "mcpServers": {
    "stawn-cad-pro": {
      "command": "python3",
      "args": [
        "/Users/you/STAWN-CAD-Pro/native-app/tools/stawn_mcp/server.py"
      ]
    }
  }
}
```

Tools: `list_commands`, `run_commands`, `new_part`, `open_part`, `save_part`, `model_info`, `export`.

## Known limitation

Fillet and chamfer edges are remembered by a point on the edge. If a dimension or configuration moves that edge,
the fillet reports a missing edge reference in that configuration. Proper topological naming is planned.

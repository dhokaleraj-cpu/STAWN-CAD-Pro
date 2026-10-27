# STAWN CAD Pro plan

The full architecture and phased plan lives in the project doc:
https://claude.ai/artifact/7SReHjvjUbyoZtJYnQrEC9

| Phase | Scope | State |
|---|---|---|
| 0 Foundations | Core library, OpenCascade, Swift bridge, viewport, STEP import and export | Done |
| 0b Windows 11 | WinUI 3 app over stawn.dll with the same panels as the Mac app; vcpkg build | Written, first Windows build pending |
| 1 Sketcher | Solver, relations and the shared sketch editor done; sketch mode in both apps with Extrude/Revolve dialogs (Mac build to verify) | In progress |
| 2 Part features | Extrude, revolve, cut, fillet, chamfer done; hole, pattern, mirror, shell, draft next | In progress |
| 3 Configurations and properties | Done in core and app | Done |
| 4 Drawings | Standard views, title block, PDF/DXF/SVG done; section, detail, GD&T, editable dims next | In progress |
| 5 Assemblies | Components, mates, BOM | Not started |
| 6 Breadth | Sheet metal, weldments, surfaces, macros | Not started |

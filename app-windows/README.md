# STAWN CAD Pro for Windows 11

Native Windows interface (WinUI 3, C#) over the same C++ core as the macOS app. The core is
built into `stawn.dll`; the app calls its C API (`core/capi/stawn_c.h`) through P/Invoke, so
parts, configurations, custom properties, drawings and the command API behave the same on both
platforms and `.stprt` files move freely between them.

| Mac (SwiftUI)                 | Windows (WinUI 3)                        |
|-------------------------------|------------------------------------------|
| `PartModel.swift`             | `Model/PartModel.cs`                     |
| `Viewport.swift` (SceneKit)   | `Views/Viewport.cs` (Win2D)              |
| `ManagerPanels.swift`         | `Views/FeatureTree.cs`, `PropertyManagerPanel.cs`, `ConfigurationManagerPanel.cs` |
| `CustomPropertiesPane.swift`  | `Views/CustomPropertiesPane.cs`          |
| `DrawingSheet.swift`          | Drawing tab + `Dialogs.DrawingPreviewAsync` |
| `PartWindow.swift`            | `MainWindow.xaml(.cs)`                   |

## Requirements

- Windows 11 (x64 or ARM64; Parallels on Apple silicon is ARM64)
- Visual Studio 2022 with *Desktop development with C++* and *.NET desktop development*
  (Windows App SDK C# templates), or the Build Tools plus .NET 8 SDK
- CMake 3.20+ and [vcpkg](https://github.com/microsoft/vcpkg) with `VCPKG_ROOT` set

## Build

From a *Developer PowerShell for VS 2022* in the repository root:

```powershell
.\app-windows\build.ps1 -Run
```

The script configures CMake with the vcpkg toolchain (OpenCascade, Eigen, nlohmann-json and
doctest come from `vcpkg.json`; the first build of OpenCascade takes a while), builds and tests
the core, collects `stawn.dll` and its dependencies in `build-windows\native`, then publishes
the app to `build-windows\app\StawnCADPro.exe`. Opening `StawnCADPro.Windows.csproj` in Visual
Studio works too once `build-windows\native` exists.

Pass a part on the command line to open it directly: `StawnCADPro.exe C:\parts\DM400002.stprt`.

## Status

Done: Welcome screen, File menu (new, open, save, STEP import, STEP/STL export), CommandManager
tabs, feature tree with suppress/rename, PropertyManager with all/this-configuration dimension
edits and mass properties, ConfigurationManager, template-driven custom properties pane,
3D view with orbit/pan/zoom, standard views and display styles, drawing preview and PDF/DXF/SVG
export, Fillet/Chamfer dialogs and a Run Commands dialog for the command API.

Next: interactive sketcher and feature PropertyManagers (shared design with the Mac app), and a
Direct3D 11 viewport with picking to replace the Win2D painter's renderer.

# SolidWorks export bridge

Moves existing SolidWorks parts into STAWN CAD Pro. SolidWorks files are a closed format, so
the bridge asks SolidWorks itself to export them, on the Windows machine where it is licensed.

1. **On Windows** (the Parallels VM): install Python 3 and `py -m pip install pywin32`, then
   ```
   py sw_export.py "C:\SOLIDWORKS Data" "\\Mac\Home\STAWN Export" --limit 5
   ```
   Each part gets a folder with one STEP file per configuration and a `manifest.json` holding
   the configurations, descriptions, custom properties (both the expression and its
   evaluated value) and material. Parts open read-only and are never saved.
2. **On the Mac**:
   ```
   stawn-cli migrate "~/STAWN Export/DM400002_8mm_Shaft/manifest.json" DM400002_8mm_Shaft.stprt
   ```
   The STAWN part has one imported body per configuration, each shown only in its own
   configuration. Links such as `SW-Mass@part.SLDPRT` become `${Mass}`; other properties keep
   their evaluated text.

What comes over: geometry per configuration, configurations, custom properties, material.
What does not: the feature tree and sketches (STEP carries no history), drawings
(export those as PDF or DXF from SolidWorks), assemblies (planned with STAWN assemblies).

The separate solidworks-mcp project can drive SolidWorks from Claude in the same VM; this
script needs only pywin32. It has not yet been run against a real SolidWorks install, so
start with `--limit 5` and check `export_report.json`.

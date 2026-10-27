"""Export SolidWorks parts for STAWN CAD Pro.

Run this on Windows with SolidWorks installed and licensed (for example inside the Parallels
VM). For every .SLDPRT under the source folder it writes, into the output folder:

    <part>/<part>__<configuration>.step   one STEP file per configuration
    <part>/manifest.json                  configurations, custom properties, material

Then, on the Mac:

    stawn-cli migrate <output>/<part>/manifest.json <part>.stprt

Usage (from a Windows command prompt):

    py -m pip install pywin32
    py sw_export.py "C:\\SOLIDWORKS Data" "\\\\Mac\\Home\\STAWN Export"

Options: --limit N (first N parts only), --skip-existing.
Parts are opened read-only and never saved, so the source files are not changed.
"""

import argparse
import json
import re
import sys
import traceback
from pathlib import Path

try:
    import pythoncom
    import win32com.client
    from win32com.client import VARIANT
except ImportError:
    sys.exit("This script needs Windows and pywin32: py -m pip install pywin32")

SW_DOC_PART = 1
SW_OPEN_SILENT = 1
SW_OPEN_READ_ONLY = 2
SW_SAVE_AS_CURRENT_VERSION = 0
SW_SAVE_AS_SILENT = 1


def byref_int():
    return VARIANT(pythoncom.VT_BYREF | pythoncom.VT_I4, 0)


def safe_name(text):
    return re.sub(r'[<>:"/\\|?*]+', "_", text).strip() or "part"


def connect():
    try:
        app = win32com.client.Dispatch("SldWorks.Application")
    except Exception as e:  # noqa: BLE001
        sys.exit(f"Cannot start SolidWorks through COM: {e}")
    app.Visible = False
    return app


def read_properties(model, configuration=""):
    """Custom properties as {name: {"expression": ..., "resolved": ...}}."""
    out = {}
    try:
        manager = model.Extension.CustomPropertyManager(configuration)
        names = manager.GetNames() or ()
    except Exception:  # noqa: BLE001
        return out
    for name in names:
        expression, resolved = "", ""
        try:
            value = VARIANT(pythoncom.VT_BYREF | pythoncom.VT_BSTR, "")
            evaluated = VARIANT(pythoncom.VT_BYREF | pythoncom.VT_BSTR, "")
            manager.Get4(name, False, value, evaluated)
            expression, resolved = value.value, evaluated.value
        except Exception:  # noqa: BLE001
            try:
                expression = manager.Get(name)
                resolved = expression
            except Exception:  # noqa: BLE001
                continue
        out[name] = {"expression": expression, "resolved": resolved}
    return out


def read_material(model, configuration):
    name = ""
    try:
        database = VARIANT(pythoncom.VT_BYREF | pythoncom.VT_BSTR, "")
        name = model.GetMaterialPropertyName2(configuration, database) or ""
    except Exception:  # noqa: BLE001
        pass
    density = 0.0
    try:
        density = float(model.Extension.CreateMassProperty().Density)  # kg/m^3
    except Exception:  # noqa: BLE001
        pass
    return {"name": name, "density": density}


def export_part(app, path, out_root, skip_existing):
    part_name = path.stem
    out_dir = out_root / safe_name(part_name)
    manifest_path = out_dir / "manifest.json"
    if skip_existing and manifest_path.exists():
        return "skipped"
    out_dir.mkdir(parents=True, exist_ok=True)

    errors, warnings = byref_int(), byref_int()
    model = app.OpenDoc6(str(path), SW_DOC_PART, SW_OPEN_SILENT | SW_OPEN_READ_ONLY, "", errors, warnings)
    if model is None:
        raise RuntimeError(f"OpenDoc6 failed with error code {errors.value}")
    try:
        active = model.ConfigurationManager.ActiveConfiguration.Name
        configurations = []
        for name in model.GetConfigurationNames() or ():
            model.ShowConfiguration2(name)
            model.ForceRebuild3(False)
            step_name = f"{safe_name(part_name)}__{safe_name(name)}.step"
            e, w = byref_int(), byref_int()
            ok = model.Extension.SaveAs(str(out_dir / step_name), SW_SAVE_AS_CURRENT_VERSION, SW_SAVE_AS_SILENT, None, e, w)
            if not ok:
                raise RuntimeError(f"STEP export of configuration {name} failed with error code {e.value}")
            config = model.GetConfigurationByName(name)
            configurations.append({
                "name": name,
                "description": getattr(config, "Description", "") or "",
                "bomPartNumber": getattr(config, "AlternateName", "") or "",
                "step": step_name,
                "properties": read_properties(model, name),
            })
        model.ShowConfiguration2(active)
        manifest = {
            "format": "stawn.migration",
            "version": 1,
            "source": str(path),
            "name": part_name,
            "activeConfiguration": active,
            "material": read_material(model, active),
            "properties": read_properties(model, ""),
            "configurations": configurations,
        }
        manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
        return f"{len(configurations)} configuration(s)"
    finally:
        app.CloseDoc(model.GetTitle())


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", type=Path, help="folder with .SLDPRT files (searched recursively)")
    parser.add_argument("output", type=Path, help="folder for STEP files and manifests")
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--skip-existing", action="store_true")
    args = parser.parse_args()

    parts = sorted(p for p in args.source.rglob("*") if p.suffix.lower() == ".sldprt" and not p.name.startswith("~$"))
    if args.limit:
        parts = parts[: args.limit]
    if not parts:
        sys.exit(f"No .SLDPRT files under {args.source}")
    args.output.mkdir(parents=True, exist_ok=True)

    app = connect()
    report = []
    for i, path in enumerate(parts, 1):
        try:
            result = export_part(app, path, args.output, args.skip_existing)
            print(f"[{i}/{len(parts)}] {path.name}: {result}")
            report.append({"part": str(path), "ok": True, "result": result})
        except Exception as e:  # noqa: BLE001
            print(f"[{i}/{len(parts)}] {path.name}: FAILED {e}")
            report.append({"part": str(path), "ok": False, "error": str(e), "trace": traceback.format_exc()})
    (args.output / "export_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    failed = sum(1 for r in report if not r["ok"])
    print(f"Done: {len(report) - failed} exported, {failed} failed. Report: {args.output / 'export_report.json'}")


if __name__ == "__main__":
    main()

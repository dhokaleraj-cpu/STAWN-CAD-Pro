"""STAWN CAD Pro MCP server.

Lets Claude (or any MCP client) build and edit STAWN parts through the command API in
docs/COMMANDS.md. It loads the STAWN core shared library (libstawn) with ctypes and keeps one
open part per server process.

    pip install "mcp<2"
    STAWN_LIB=/path/to/build/libstawn.dylib python tools/stawn_mcp/server.py
"""

import ctypes
import json
import os
import sys
from pathlib import Path
from typing import Any

from mcp.server.fastmcp import FastMCP


def _load_library() -> ctypes.CDLL:
    candidates = []
    if os.environ.get("STAWN_LIB"):
        candidates.append(Path(os.environ["STAWN_LIB"]))
    build = Path(__file__).resolve().parents[2] / "build"
    candidates += [build / "libstawn.dylib", build / "libstawn.so", build / "stawn.dll"]
    for path in candidates:
        if path.exists():
            lib = ctypes.CDLL(str(path))
            break
    else:
        sys.exit("libstawn not found: build the core with cmake or set STAWN_LIB")
    lib.stawn_doc_new.restype = ctypes.c_void_p
    lib.stawn_doc_free.argtypes = [ctypes.c_void_p]
    lib.stawn_doc_execute.restype = ctypes.c_void_p
    lib.stawn_doc_execute.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    lib.stawn_commands_describe.restype = ctypes.c_void_p
    lib.stawn_free_string.argtypes = [ctypes.c_void_p]
    return lib


_lib = _load_library()
_doc = _lib.stawn_doc_new()


def _take_string(ptr: int) -> str:
    try:
        return ctypes.string_at(ptr).decode("utf-8")
    finally:
        _lib.stawn_free_string(ptr)


def _execute(command: Any) -> Any:
    return json.loads(_take_string(_lib.stawn_doc_execute(_doc, json.dumps(command).encode("utf-8"))))


mcp = FastMCP("stawn-cad-pro")


@mcp.tool()
def list_commands() -> list:
    """List every STAWN CAD Pro command with its parameters."""
    return json.loads(_take_string(_lib.stawn_commands_describe()))


@mcp.tool()
def run_commands(commands: list[dict]) -> list:
    """Run STAWN commands in order, stopping at the first failure.

    Each command is an object with an "op" and its parameters, for example
    {"op": "create_sketch", "plane": "Front"}, {"op": "draw_circle", "cx": 0, "cy": 0, "r": 30},
    {"op": "extrude", "depth": 15}, {"op": "fillet", "radius": 2, "edges": "all"}.
    Lengths are in mm and angles in degrees. Call list_commands for the full list.
    """
    return _execute(commands)


@mcp.tool()
def new_part(name: str = "Part1") -> dict:
    """Start a new, empty part."""
    return _execute({"op": "new_part", "name": name})


@mcp.tool()
def open_part(path: str) -> dict:
    """Open a .stprt part file."""
    return _execute({"op": "open", "path": path})


@mcp.tool()
def save_part(path: str) -> dict:
    """Save the current part as a .stprt file."""
    return _execute({"op": "save", "path": path})


@mcp.tool()
def model_info() -> dict:
    """Feature tree, dimensions, configurations, custom properties and mass of the current part."""
    results = _execute([
        {"op": "list_features"},
        {"op": "list_dimensions"},
        {"op": "list_configurations"},
        {"op": "get_properties"},
        {"op": "mass_properties"},
    ])
    info: dict = {}
    for r in results:
        r = dict(r)
        r.pop("ok", None)
        info.update(r)
    return info


@mcp.tool()
def export(path: str, kind: str = "step", paper: str = "A3") -> dict:
    """Export the part. kind is step, stl, or drawing (format from the extension: .pdf, .dxf, .svg)."""
    op = {"step": "export_step", "stl": "export_stl", "drawing": "export_drawing"}.get(kind)
    if op is None:
        return {"ok": False, "error": "kind must be step, stl or drawing"}
    command = {"op": op, "path": path}
    if kind == "drawing":
        command["paper"] = paper
    return _execute(command)


if __name__ == "__main__":
    mcp.run()

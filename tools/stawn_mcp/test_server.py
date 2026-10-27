"""Smoke test for the MCP server's tool functions, without an MCP client."""
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(__file__))
import server  # noqa: E402

results = server.run_commands([
    {"op": "create_sketch", "plane": "Front"},
    {"op": "draw_circle", "cx": 0, "cy": 0, "r": 30},
    {"op": "extrude", "depth": 15},
    {"op": "fillet", "radius": 2, "edges": "all"},
])
assert all(r["ok"] for r in results), results
info = server.model_info()
assert [f["name"] for f in info["features"]] == ["Sketch1", "Boss-Extrude1", "Fillet1"], info
with tempfile.TemporaryDirectory() as d:
    assert server.save_part(os.path.join(d, "disc.stprt"))["ok"]
    assert server.export(os.path.join(d, "disc.pdf"), "drawing")["ok"]
    assert server.open_part(os.path.join(d, "disc.stprt"))["ok"]
assert len(server.list_commands()) > 30
print("MCP server smoke test passed:", round(info["volume_mm3"], 1), "mm^3")

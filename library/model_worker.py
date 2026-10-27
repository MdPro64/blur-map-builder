"""Read the size of .model files with the Blender add-on's reader (no Blender needed).
usage: python model_worker.py FILE...  -> one json line per file: {"file", "lo": [x, y up, z], "hi": [...]} or
{"file", "error"}. Run by make_catalog.py in a separate process, because the reader hangs on a few models."""
import contextlib
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "model"))
import cpmodel  # noqa: E402

cpmodel.limit_memory()             # a runaway model: MemoryError (reported below), not gigabytes
B = cpmodel._reader()
for path in sys.argv[1:]:
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            M = B.read_cpmodel_data(None, path)
        boxes = [e["bounding_box"] for e in M["elements"]]
        # the reader turns the model z-up (Blender): its y = game z, its z = game y (up)
        lo = [min(b[0][k] for b in boxes) for k in ("x", "z", "y")]
        hi = [max(b[1][k] for b in boxes) for k in ("x", "z", "y")]
        print(json.dumps({"file": path, "lo": lo, "hi": hi}), flush=True)
    except Exception as ex:  # noqa: BLE001 - reported, the catalog uses a default box
        print(json.dumps({"file": path, "error": repr(ex)[:120]}), flush=True)

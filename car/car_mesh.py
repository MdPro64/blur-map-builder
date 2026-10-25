"""THE CAR'S OWN 3D MODEL in the game (an imported .glb body). Work in steps:
  step 1 (now): prove a car model we write is shown - the body car's model with the body made 35 % taller
          (vertex stream 0 = body, interior, glass, brakes; the tyres / rims are in other streams: untouched).
  step 2: the .glb's shape in the body's place (same file layout: the .glb simplified to fit the body's room).
Car model facts: car_model.py (positions = half floats x, up, forward)."""
import contextlib
import io
import os
import struct
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
MB = os.path.dirname(HERE)
for p in (MB, os.path.join(MB, "library"), os.path.join(MB, "model")):
    if p not in sys.path:
        sys.path.insert(0, p)

TALLER = 1.35


def _read(data):
    import cpmodel
    f = tempfile.NamedTemporaryFile(delete=False, suffix=".model")
    f.write(data)
    f.close()
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            return cpmodel._reader().read_cpmodel_data(None, f.name)
    finally:
        os.unlink(f.name)


def test_model(base):
    """step 1: bytes of the body car's model with stream 0 stretched up"""
    import game_preview
    import car_model
    data = bytearray(game_preview._model_bytes(car_model.model_path(base)))
    vs = _read(bytes(data))["vertex_streams"][0]
    assert vs["definition"][0]["type"] == 9, "stream 0 has no half-float positions"
    rows = np.frombuffer(data, np.uint8, vs["count"] * vs["bytes"], vs["start"]).reshape(vs["count"], vs["bytes"]).copy()
    pos = rows[:, 0:8].copy().view(np.float16)
    lo = pos[:, 1].astype(np.float32).min()
    pos[:, 1] = ((pos[:, 1].astype(np.float32) - lo) * TALLER + lo).astype(np.float16)
    rows[:, 0:8] = pos.view(np.uint8).reshape(-1, 8)
    data[vs["start"]:vs["start"] + vs["count"] * vs["bytes"]] = rows.tobytes()
    return bytes(data)

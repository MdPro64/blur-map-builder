"""Log the camera target matrix (0x10BD608) and the player car position twice a second (for `minutes`), to see where
the camera sits at the race start.  python log_camera.py OUT.txt [minutes]"""
import os
import struct
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "trainer"))
import blur_trainer as T  # noqa: E402

out = open(sys.argv[1], "w", buffering=1)
end = time.time() + 60 * float(sys.argv[2] if len(sys.argv) > 2 else 15)
g = None
while time.time() < end:
    try:
        if g is None or not g.alive():
            g = T.Game()
        cam = struct.unpack("<12f", g.read(0x10BD608, 48))
        try:
            car = T.read_pose(g, T.find_physics(g))["pos"]
        except Exception:
            car = None
        out.write("%.1f cam %s fwd %s car %s\n" % (time.time(), [round(v, 1) for v in cam[9:]],
                                                  [round(v, 2) for v in cam[6:9]],
                                                  [round(v, 1) for v in car] if car else None))
    except (Exception, SystemExit) as e:          # Game() exits when Blur is not running: wait for it
        out.write("%.1f no game (%s)\n" % (time.time(), e))
        g = None
        time.sleep(2)
    time.sleep(0.5)

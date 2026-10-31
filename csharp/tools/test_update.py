# The update of the real programs against a FAKE release on this PC: nothing touches GitHub or a game folder.
#   1. C# to C#: scratch\upd\new holds a program, its updater swaps itself for the "2.0.1" one and starts it.
#   2. Python to C#: the unchanged map_builder\updater.py installs publish\Blur Map Builder.exe under GitHub's dotted name.
#   python tools/test_update.py
import http.server, json, os, shutil, subprocess, sys, threading, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PY = os.path.join(os.path.dirname(ROOT), "map_builder")
EXE = os.path.join(ROOT, "publish", "Blur Map Builder.exe")
T = os.path.join(ROOT, "scratch", "upd")
shutil.rmtree(T, ignore_errors=True)
os.makedirs(os.path.join(T, "www")); os.makedirs(os.path.join(T, "new")); os.makedirs(os.path.join(T, "old"))
PORT = 8765


class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k): super().__init__(*a, directory=os.path.join(T, "www"), **k)
    def log_message(self, *a): pass


shutil.copy(EXE, os.path.join(T, "www", "Blur.Map.Builder.exe"))
rel = {"tag_name": "v2.0.1", "html_url": "", "body": "test", "assets": [{"name": "Blur.Map.Builder.exe", "browser_download_url": "http://localhost:%d/Blur.Map.Builder.exe" % PORT}]}
json.dump(rel, open(os.path.join(T, "www", "rel.json"), "w"))
s = http.server.ThreadingHTTPServer(("localhost", PORT), H)
threading.Thread(target=s.serve_forever, daemon=True).start()
ok = True

# 1
d = os.path.join(T, "new"); mine = os.path.join(d, "Blur.Map.Builder.exe")
shutil.copy(EXE, mine)
open(mine, "ab").write(b"old copy")                  # so the swap can be seen
size0 = os.path.getsize(mine)
env = dict(os.environ, BMB_UPDATE_API="http://localhost:%d/rel.json" % PORT, BMB_TEST="1", BMB_LANG="en")
subprocess.run([mine, "--game", os.path.join(ROOT, "scratch", "game"), "--update-test"], env=env, timeout=120)
time.sleep(4)
log = open(os.path.join(d, "update_test.txt")).read() if os.path.exists(os.path.join(d, "update_test.txt")) else ""
print(log)
good = "install: ok" in log and "new program started" in log and os.path.getsize(mine) == os.path.getsize(EXE) and os.path.getsize(mine + ".old") == size0
print("C# to C#:", "PASS" if good else "FAIL", sorted(os.listdir(d))); ok &= good

# 2
sys.path.insert(0, PY)
import paths, updater
d = os.path.join(T, "old"); mine = os.path.join(d, "Blur.Map.Builder.exe")
open(mine, "wb").write(b"MZ python" + b"\0" * 2_000_000)
paths.GAME = d; paths.FROZEN = True
info = ("v2.0.0", {"Blur Map Builder.exe": rel["assets"][0]["browser_download_url"], "Blur Map Tester.exe": None}, "test")
print("python offers it:", updater._version("v2.0.0") > updater._version("1.1.1"))
err = updater.install(info)
good = err is None and os.path.getsize(mine) == os.path.getsize(EXE) and os.path.exists(mine + ".old")
print("Python to C#:", "PASS" if good else "FAIL %s" % err, sorted(os.listdir(d))); ok &= good
s.shutdown()
shutil.rmtree(T, ignore_errors=True)
sys.exit(0 if ok else 1)

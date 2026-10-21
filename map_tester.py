"""
BLUR MAP TESTER: start Blur REBORN straight into a custom LAN race on the map you last tested / built in the map
builder (Amax QuickStart plugin, amax\\plugins\\quickstart\\target.lua). When the game closes, the quick start is
switched off again, so Blur REBORN started any other way starts normally.

Kept tiny (standard library + tkinter only): it is its own small exe, "Blur Map Tester.exe".
"""
import os
import re
import subprocess
import sys
import time
import tkinter as tk
from tkinter import messagebox

import paths


def set_enabled(on):
    t = paths.QUICKSTART_TARGET
    if not os.path.exists(t):
        return False
    s = open(t, encoding="utf-8").read()
    s2 = re.sub(r"enabled\s*=\s*(true|false)", "enabled = %s" % ("true" if on else "false"), s, count=1)
    if "route_names" not in s2:
        return False                            # no map chosen yet (the map builder writes it)
    with open(t, "w", encoding="utf-8", newline="\n") as f:      # no BOM (the game's Lua)
        f.write(s2)
    return True


def running():
    return paths.game_running()


def main():
    root = tk.Tk()
    root.withdraw()
    if not os.path.exists(paths.REBORN_EXE) or not os.path.exists(os.path.join(paths.PACK_DIR, "root.pak")):
        messagebox.showinfo("Blur Map Tester", "Open the Blur Map Builder first and press Build + install once: the "
                            "tester plays the map you built.")
        return
    if running():
        messagebox.showinfo("Blur Map Tester", "Blur is already running - close it first.")
        return
    if not set_enabled(True):
        messagebox.showinfo("Blur Map Tester", "No map picked yet: in the Blur Map Builder select your map and press "
                            "Play (F5) once - the tester then always starts that map.")
        return
    subprocess.Popen([paths.REBORN_EXE], cwd=paths.GAME)
    # wait for the game to start (intro / login can take minutes), then for it to close -> quick start off
    t0, seen = time.time(), False
    try:
        while True:
            time.sleep(3)
            up = running()
            if up:
                seen = True
            elif seen or time.time() - t0 > 600:
                break
    finally:
        set_enabled(False)


if __name__ == "__main__":
    sys.exit(main())

"""
"Blur Map Builder.exe" - the start of the program:
  (no arguments)        first-start setup (setup_game.py, with a progress window) -> the track designer, plus the
                        update check (updater.py) and crash reports (logs\\crash_*.txt, for MDPRO on Discord)
  --run SCRIPT ARGS...  run one of our scripts in this process (the build and the object library run as their own
                        process: in the exe there is no separate python)
"""
import os
import runpy
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import paths  # noqa: E402


def _std_streams():
    """the exe is a WINDOW program: its sys.stdout / stderr are None, so a script run with --run printed nothing -
    the build window stayed empty and logs\\last_build.txt was missing (user 2026-10-21). Use the pipe the map
    builder gave this process instead (line by line, so the build window shows each step at once)."""
    import ctypes
    import msvcrt
    for name, std in (("stdout", -11), ("stderr", -12)):
        if getattr(sys, name) is not None:
            continue
        h = ctypes.windll.kernel32.GetStdHandle(std)
        if h in (0, -1, None):
            continue
        try:
            fd = msvcrt.open_osfhandle(h, os.O_WRONLY)
            setattr(sys, name, open(fd, "w", buffering=1, encoding="utf-8", errors="replace"))
        except OSError:
            pass


def run_script():
    _std_streams()
    for st in (sys.stdout, sys.stderr):     # a pipe is block-buffered: the build window got everything at the END
        try:                                # (user 2026-10-22) - send each line at once
            st.reconfigure(line_buffering=True)
        except (AttributeError, ValueError):
            pass
    rel = sys.argv[2]
    script = os.path.join(paths.APP, rel)
    sys.argv = [script] + sys.argv[3:]
    sys.path[:0] = [os.path.dirname(script)]
    os.chdir(os.path.dirname(script))
    try:
        runpy.run_path(script, run_name="__main__")
    except SystemExit:
        raise
    except BaseException:                   # into the build window / log - not a Windows "unhandled exception" box
        try:
            traceback.print_exc()
            sys.stderr.flush()
        except (OSError, ValueError):       # nobody reads any more (the map builder closed): just stop
            pass
        os._exit(1)                         # (sys.exit flushed the dead pipe again at exit -> the same box)


# ---------------------------------------------------------------------------------------------- crash reports
def crash_report(exc_type, exc, tb, where="program"):
    """write logs\\crash_<time>.txt and tell the player to send it; returns the file name"""
    paths.ensure_dirs()
    name = os.path.join(paths.LOGS, "crash_%s.txt" % time.strftime("%Y%m%d_%H%M%S"))
    try:
        with open(name, "w", encoding="utf-8") as f:
            f.write("%s %s - crash in the %s\n" % (paths.APP_NAME, paths.VERSION, where))
            f.write("time: %s\nwindows: %s\ngame folder: %s\n\n" % (time.ctime(), sys.getwindowsversion(), paths.GAME))
            f.write("".join(traceback.format_exception(exc_type, exc, tb)))
            build_log = os.path.join(paths.LOGS, "last_build.txt")
            if os.path.exists(build_log):
                f.write("\n---- last build log (end) ----\n")
                f.write("".join(open(build_log, encoding="utf-8", errors="replace").readlines()[-60:]))
    except OSError:
        pass
    return name


def show_crash(root, name):
    from tkinter import messagebox
    messagebox.showerror("%s - something went wrong" % paths.APP_NAME,
                         "Sorry, the map builder hit an error.\n\nA report was saved:\n%s\n\nPlease send that file "
                         "to %s on Discord. Your maps are safe (auto-save)." % (name, paths.DISCORD), parent=root)
    try:
        os.startfile(os.path.dirname(name))
    except OSError:
        pass


# ---------------------------------------------------------------------------------------------- first start
def run_setup(root):
    """the setup window (progress bar); returns True when everything is ready"""
    import tkinter as tk
    from tkinter import ttk, messagebox
    import setup_game
    if not setup_game.needed():
        return True
    win = tk.Toplevel(root)
    win.title("%s - first start" % paths.APP_NAME)
    win.configure(bg="#0b1426")
    win.resizable(False, False)
    tk.Label(win, text="Setting up the map builder from YOUR game files (only the first time)", bg="#0b1426",
             fg="#ffffff", font=("Segoe UI", 11, "bold")).pack(padx=18, pady=(14, 4))
    tk.Label(win, text="Blur.exe and the game's own files are not changed. Takes a few minutes.", bg="#0b1426",
             fg="#9aa8b8").pack(padx=18)
    bar = ttk.Progressbar(win, length=460, maximum=100)
    bar.pack(padx=18, pady=10)
    step = tk.Label(win, text="", bg="#0b1426", fg="#7fd0ff")
    step.pack(padx=18)
    line = tk.Label(win, text="", bg="#0b1426", fg="#9aa8b8", wraplength=460)
    line.pack(padx=18, pady=(2, 14))
    win.update_idletasks()
    win.geometry("+%d+%d" % ((win.winfo_screenwidth() - win.winfo_reqwidth()) // 2,
                             (win.winfo_screenheight() - win.winfo_reqheight()) // 2))
    state = {"done": False, "error": None}
    import queue
    import threading
    q = queue.Queue()

    def work():
        try:
            setup_game.run_all(log=lambda m: q.put(("line", m)),
                               progress=lambda k, n, label: q.put(("step", k * 100 // n, label)))
        except setup_game.SetupError as ex:
            state["error"] = str(ex)
        except Exception as ex:                       # noqa: BLE001 - reported
            state["error"] = "%s\n\nReport: %s" % (ex, crash_report(type(ex), ex, ex.__traceback__, "setup"))
        state["done"] = True

    threading.Thread(target=work, daemon=True).start()
    while not state["done"] or not q.empty():
        try:
            while True:
                item = q.get_nowait()
                if item[0] == "line":
                    line.config(text=item[1][:200])
                else:
                    bar["value"] = item[1]
                    step.config(text=item[2])
        except queue.Empty:
            pass
        win.update()
        time.sleep(0.05)
    win.destroy()
    if state["error"]:
        messagebox.showerror("%s - setup" % paths.APP_NAME, state["error"])
        return False
    return True


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--run":
        return run_script()
    import updater
    updater.cleanup_old()
    sys.argv = [os.path.join(HERE, "road_designer.py")]
    import tkinter as tk
    # the designer module makes its window at import time only under __main__: run it as a module we control
    rd = runpy.run_path(os.path.join(HERE, "road_designer.py"), run_name="road_designer")
    root = tk.Tk()
    root.withdraw()
    paths.set_app_icon(root)

    def tk_error(exc, val, tb):
        show_crash(root, crash_report(exc, val, tb, "designer"))
    root.report_callback_exception = tk_error
    sys.excepthook = lambda t, v, tb: show_crash(root, crash_report(t, v, tb))
    if not run_setup(root):                           # (before the splash: it stays on top)
        root.destroy()
        return 1
    splash_done = rd["show_splash"](root)
    root.geometry("1500x880")
    designer = rd["Designer"](root)
    splash_done(designer.fit)
    designer.check_updates(auto=True)                # newer -> the update window; newest -> button turns green
    root.mainloop()
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""
The UPDATE window (Blur look): what is new -> Update now -> download progress (the window keeps drawing: the download
runs on a worker thread) -> "restarting" -> the new map builder starts by itself. Closing is held off while files
are swapped (before: the window froze on "Update now", players thought it crashed and closed it).
"""
import queue
import threading
import time
import tkinter as tk

import blur_theme as T
import paths
import updater

GREEN = "#58d06a"
WARN = "#ffd23b"
RED = "#ff6b6b"
BAR_W = 440


class UpdateWindow:
    def __init__(self, root, info, install=None, restart=None, on_close=None):
        """info = updater.latest(); install / restart = updater's (a test passes fakes); on_close() when the window
        goes away without updating"""
        self.root, self.info = root, info
        self.install, self.restart = install or updater.install, restart or updater.restart
        self.on_close = on_close
        self.busy = False
        self.q = queue.Queue()
        tag, _, notes = info
        w = self.win = tk.Toplevel(root)
        w.title("%s - update" % paths.APP_NAME)
        w.configure(bg=T.BG)
        w.resizable(False, False)
        w.protocol("WM_DELETE_WINDOW", self.close)
        try:
            w.transient(root)
        except tk.TclError:
            pass

        head = tk.Frame(w, bg=T.PANEL)
        head.pack(fill="x")
        tk.Label(head, text="UPDATE AVAILABLE", bg=T.PANEL, fg=T.HEAD, font=("Segoe UI", 9, "bold")).pack(
            anchor="w", padx=18, pady=(12, 0))
        row = tk.Frame(head, bg=T.PANEL)
        row.pack(fill="x", padx=18, pady=(0, 12))
        tk.Label(row, text="%s %s" % (paths.APP_NAME, tag), bg=T.PANEL, fg=T.TEXT,
                 font=("Segoe UI", 16, "bold")).pack(side="left")
        tk.Label(row, text="you have %s" % paths.VERSION, bg=T.PANEL, fg=T.MUTED, font=("Segoe UI", 10)).pack(
            side="left", padx=(10, 0), pady=(6, 0))
        if updater.repo().lower() != paths.GITHUB_REPO.lower():
            tk.Label(head, text="from github.com/%s (your update source)" % updater.repo(), bg=T.PANEL, fg=WARN,
                     font=("Segoe UI", 9)).pack(anchor="w", padx=18, pady=(0, 10))
        tk.Frame(w, bg=T.ORANGE, height=2).pack(fill="x")

        body = tk.Frame(w, bg=T.BG)
        body.pack(fill="both", padx=18, pady=(12, 0))
        tk.Label(body, text="WHAT'S NEW", bg=T.BG, fg=T.HEAD, font=("Segoe UI", 8, "bold")).pack(anchor="w")
        txt = tk.Text(body, width=66, height=9, bg=T.FIELD, fg=T.TEXT, relief="flat", wrap="word", bd=0,
                      font=("Segoe UI", 9), padx=10, pady=8, highlightthickness=1, highlightbackground=T.EDGE)
        notes = (notes or "(no notes)").replace("\r", "").replace("**", "").replace("## ", "").replace("`", "")
        txt.insert("1.0", "\n".join("• " + ln[2:] if ln.startswith(("* ", "- ")) else ln
                                    for ln in notes.strip().split("\n")))
        txt.configure(state="disabled")
        txt.pack(fill="x", pady=(4, 0))

        # bottom part: the question (buttons) OR the progress, swapped in place
        self.foot = tk.Frame(w, bg=T.BG)
        self.foot.pack(fill="x", padx=18, pady=(12, 16))
        self._ask()
        w.update_idletasks()
        w.minsize(BAR_W + 36 + 30, 0)                 # same width while asking and while downloading
        if root.winfo_viewable():
            x = root.winfo_rootx() + (root.winfo_width() - w.winfo_reqwidth()) // 2
            y = root.winfo_rooty() + (root.winfo_height() - w.winfo_reqheight()) // 3
        else:
            x = (w.winfo_screenwidth() - w.winfo_reqwidth()) // 2
            y = (w.winfo_screenheight() - w.winfo_reqheight()) // 3
        w.geometry("+%d+%d" % (max(0, x), max(0, y)))
        w.lift()
        w.focus_force()

    # ------------------------------------------------------------------------------------------ parts
    def _clear(self):
        for c in self.foot.winfo_children():
            c.destroy()

    def _ask(self, error=None):
        self._clear()
        if error:
            tk.Label(self.foot, text=error, bg=T.BG, fg=RED, wraplength=BAR_W, justify="left",
                     font=("Segoe UI", 9, "bold")).pack(anchor="w", pady=(0, 8))
        else:
            tk.Label(self.foot, text="Your maps are kept. The map builder restarts by itself when the update is done.",
                     bg=T.BG, fg=T.MUTED, font=("Segoe UI", 9)).pack(anchor="w", pady=(0, 10))
        row = tk.Frame(self.foot, bg=T.BG)
        row.pack(fill="x")
        tk.Button(row, text="Close" if error else "Later", command=self.close, bg=T.GLASS, fg=T.TEXT,
                  font=("Segoe UI", 10)).pack(side="right")
        tk.Button(row, text="⬇ Try again" if error else "⬇ Update now", command=self.start,
                  bg=T.ORANGE, fg="white", font=("Segoe UI Symbol", 10, "bold")).pack(side="right", padx=(0, 8))

    def _progress(self):
        self._clear()
        self.step = tk.Label(self.foot, text="Connecting to GitHub...", bg=T.BG, fg=T.TEXT,
                             font=("Segoe UI", 10, "bold"))
        self.step.pack(anchor="w")
        self.bar = tk.Canvas(self.foot, width=BAR_W, height=16, bg=T.BG, highlightthickness=0)
        self.bar.pack(anchor="w", pady=(6, 4))
        self.bar.create_line(8, 8, BAR_W - 8, 8, width=14, capstyle="round", fill=T.EDGE)
        self.bar.create_line(8, 8, BAR_W - 8, 8, width=10, capstyle="round", fill=T.FIELD)
        self.fill = self.bar.create_line(8, 8, 8, 8, width=10, capstyle="round", fill=T.BLUE, state="hidden")
        self.detail = tk.Label(self.foot, text="", bg=T.BG, fg=T.MUTED, font=("Segoe UI", 9))
        self.detail.pack(anchor="w")
        self.warn = tk.Label(self.foot, text="⚠ Please don't close the map builder - it restarts by itself.",
                             bg=T.BG, fg=WARN, font=("Segoe UI Symbol", 9, "bold"))
        self.warn.pack(anchor="w", pady=(8, 0))

    def _set_bar(self, frac, colour=None):
        frac = max(0.0, min(1.0, frac))
        x = 8 + (BAR_W - 16) * frac
        self.bar.coords(self.fill, 8, 8, x, 8)
        self.bar.itemconfigure(self.fill, state="normal" if frac > 0 else "hidden", fill=colour or T.BLUE)

    # ------------------------------------------------------------------------------------------ actions
    def close(self):
        if self.busy:                               # flash the note instead of closing in the middle of a swap
            for k, col in enumerate((T.ORANGE, WARN, T.ORANGE, WARN)):
                self.win.after(150 * k, lambda c=col: self.warn.winfo_exists() and self.warn.config(fg=c))
            return
        self.win.destroy()
        if self.on_close:
            self.on_close()

    def start(self):
        self.busy = True
        self._progress()
        self._root_close = self.root.protocol("WM_DELETE_WINDOW")
        self.root.protocol("WM_DELETE_WINDOW", self.close)
        try:
            self.win.grab_set()
        except tk.TclError:
            pass
        names = [n for n in updater.ASSETS if self.info[1].get(n)]
        self.t0 = {}

        def progress(name, got, total):
            self.q.put(("bytes", name, got, total))

        def work():
            try:
                err = self.install(self.info, progress)
            except Exception as ex:              # noqa: BLE001 - shown in the window
                err = "Update failed: %s" % ex
            self.q.put(("done", err))

        self.names = names
        threading.Thread(target=work, daemon=True).start()
        self._poll()

    def _poll(self):
        last = None
        try:
            while True:
                item = self.q.get_nowait()
                if item[0] == "done":
                    return self._finished(item[1])
                last = item
        except queue.Empty:
            pass
        if last:
            _, name, got, total = last
            key = updater._key(name)
            k = next((i for i, n in enumerate(self.names) if updater._key(n) == key), 0)
            self.t0.setdefault(name, (time.time(), got))
            t, g0 = self.t0[name]
            speed = (got - g0) / max(0.3, time.time() - t)
            self.step.config(text="Downloading %d of %d:  %s" % (k + 1, len(self.names), name))
            mb = 1 << 20
            if total:
                self._set_bar(got / total)
                self.detail.config(text="%.1f MB of %.1f MB   ·   %.1f MB/s" % (got / mb, total / mb, speed / mb))
            else:
                self.detail.config(text="%.1f MB   ·   %.1f MB/s" % (got / mb, speed / mb))
        self.win.after(80, self._poll)

    def _finished(self, err):
        try:
            self.win.grab_release()
        except tk.TclError:
            pass
        if err:
            self.busy = False
            self.root.protocol("WM_DELETE_WINDOW", self._root_close or self.root.destroy)
            return self._ask(error=err)
        self._set_bar(1.0, GREEN)
        self.step.config(text="✔ Update installed - restarting the map builder...", fg=GREEN,
                         font=("Segoe UI Symbol", 10, "bold"))
        self.detail.config(text="Your maps are kept. The new version opens in a few seconds.")
        self.warn.config(text="")
        self.win.after(1200, self._restart)

    def _restart(self):
        self.restart()
        self.root.destroy()


def source_window(root, on_saved=None):
    """where updates come from: MDPRO's GitHub page, or another one (if MDPRO stops making updates, someone else
    can carry on). Test = asks that page for its newest release before saving."""
    w = tk.Toplevel(root)
    w.title("%s - update source" % paths.APP_NAME)
    w.configure(bg=T.BG)
    w.resizable(False, False)
    try:
        w.transient(root)
    except tk.TclError:
        pass
    head = tk.Frame(w, bg=T.PANEL)
    head.pack(fill="x")
    tk.Label(head, text="UPDATE SOURCE", bg=T.PANEL, fg=T.HEAD, font=("Segoe UI", 9, "bold")).pack(
        anchor="w", padx=18, pady=(12, 0))
    tk.Label(head, text="Where the map builder gets its updates", bg=T.PANEL, fg=T.TEXT,
             font=("Segoe UI", 14, "bold")).pack(anchor="w", padx=18, pady=(0, 12))
    tk.Frame(w, bg=T.ORANGE, height=2).pack(fill="x")
    body = tk.Frame(w, bg=T.BG)
    body.pack(fill="x", padx=18, pady=12)
    tk.Label(body, text="A GitHub page that publishes releases with \"Blur Map Builder.exe\" and \"Blur Map "
                        "Tester.exe\". Normally MDPRO's - if MDPRO stops making updates, enter the page of whoever "
                        "carries on (owner/name or the github.com link).", bg=T.BG, fg=T.MUTED, wraplength=BAR_W,
             justify="left", font=("Segoe UI", 9)).pack(anchor="w")
    var = tk.StringVar(value=updater.repo())
    row = tk.Frame(body, bg=T.BG)
    row.pack(fill="x", pady=(10, 4))
    tk.Label(row, text="github.com/", bg=T.BG, fg=T.TEXT, font=("Segoe UI", 10)).pack(side="left")
    ent = tk.Entry(row, textvariable=var, bg=T.FIELD, fg=T.TEXT, insertbackground=T.TEXT, relief="flat",
                   font=("Segoe UI", 10), width=36, highlightthickness=1, highlightbackground=T.EDGE)
    ent.pack(side="left", fill="x", expand=True, ipady=3)
    status = tk.Label(body, text="Default: %s" % paths.GITHUB_REPO, bg=T.BG, fg=T.MUTED, wraplength=BAR_W,
                      justify="left", font=("Segoe UI", 9))
    status.pack(anchor="w", pady=(2, 0))
    tk.Label(body, text="\u26A0 Only use a page you trust: the map builder downloads and runs programs from it.",
             bg=T.BG, fg=WARN, font=("Segoe UI Symbol", 9, "bold")).pack(anchor="w", pady=(8, 0))
    foot = tk.Frame(w, bg=T.BG)
    foot.pack(fill="x", padx=18, pady=(4, 16))
    box = []

    def test():
        r = updater.parse_repo(var.get())
        if not r:
            status.config(text="Not a GitHub page - write it as owner/name (e.g. %s)." % paths.GITHUB_REPO, fg=RED)
            return
        status.config(text="Asking github.com/%s ..." % r, fg=T.MUTED)
        del box[:]
        threading.Thread(target=lambda: box.append(updater.latest(r)), daemon=True).start()

        def wait():
            if not w.winfo_exists():
                return
            if not box:
                return w.after(150, wait)
            info = box[0]
            if info is None:
                status.config(text="No release found at github.com/%s (or no internet)." % r, fg=RED)
            elif not all(info[1].get(n) for n in updater.ASSETS):
                status.config(text="github.com/%s %s has no map builder programs in its release." % (r, info[0]),
                              fg=RED)
            else:
                status.config(text="\u2714 github.com/%s - newest release %s (you have %s)." % (
                    r, info[0], paths.VERSION), fg=GREEN)
        wait()

    def save():
        r = updater.parse_repo(var.get())
        if not r:
            return test()
        updater.set_repo(r)
        w.destroy()
        if on_saved:
            on_saved()

    def default():
        var.set(paths.GITHUB_REPO)
        status.config(text="Default: %s (press Save)" % paths.GITHUB_REPO, fg=T.MUTED)

    tk.Button(foot, text="Cancel", command=w.destroy, bg=T.GLASS, fg=T.TEXT, font=("Segoe UI", 10)).pack(side="right")
    tk.Button(foot, text="Save", command=save, bg=T.ORANGE, fg="white", font=("Segoe UI", 10, "bold")).pack(
        side="right", padx=(0, 8))
    tk.Button(foot, text="Test", command=test, bg=T.BLUE, fg="white", font=("Segoe UI", 10)).pack(
        side="right", padx=(0, 8))
    tk.Button(foot, text="MDPRO's (default)", command=default, bg=T.GLASS, fg=T.TEXT, font=("Segoe UI", 10)).pack(
        side="left")
    w.update_idletasks()
    w.minsize(BAR_W + 36, 0)
    owner = root.winfo_toplevel()
    if owner.winfo_viewable():
        x = owner.winfo_rootx() + (owner.winfo_width() - w.winfo_reqwidth()) // 2
        y = owner.winfo_rooty() + (owner.winfo_height() - w.winfo_reqheight()) // 3
    else:
        x, y = (w.winfo_screenwidth() - w.winfo_reqwidth()) // 2, (w.winfo_screenheight() - w.winfo_reqheight()) // 3
    w.geometry("+%d+%d" % (max(0, x), max(0, y)))
    ent.focus_set()
    ent.bind("<Return>", lambda e: save())
    w.bind("<Escape>", lambda e: w.destroy())
    return w

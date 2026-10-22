"""
UPDATES from GitHub: the latest release of paths.GITHUB_REPO. A release carries the two programs as assets
("Blur Map Builder.exe", "Blur Map Tester.exe"); an update downloads the newer ones next to the old ones, swaps them
(a running exe can be renamed on Windows) and starts the new map builder. The player's maps are never touched
(they live in "Blur Map Builder\\maps").
"""
import json
import os
import re
import subprocess
import sys
import threading
import urllib.request

import paths

API = "https://api.github.com/repos/%s/releases/latest"
ASSETS = ("Blur Map Builder.exe", "Blur Map Tester.exe")
SOURCE_FILE = os.path.join(paths.DATA, "update_source.json")     # the player's choice (kept through updates)


def repo():
    """where updates come from: the player's own choice (update_source.json; if MDPRO stops making updates,
    someone else can carry on) or paths.GITHUB_REPO"""
    try:
        r = json.load(open(SOURCE_FILE, encoding="utf-8")).get("repo")
        if r and parse_repo(r):
            return parse_repo(r)
    except (OSError, ValueError, AttributeError):
        pass
    return paths.GITHUB_REPO


def parse_repo(text):
    """"owner/name" from "owner/name" or a github.com link; None if it is neither"""
    m = re.match(r"^\s*(?:https?://)?(?:www\.)?(?:github\.com/)?([A-Za-z0-9-]+)/([A-Za-z0-9._-]+?)(?:\.git)?(?:/.*)?\s*$",
                 text or "")
    return "%s/%s" % (m.group(1), m.group(2)) if m else None


def set_repo(r):
    """save the update source (None / the default = back to MDPRO's)"""
    r = parse_repo(r) if r else None
    if not r or r.lower() == paths.GITHUB_REPO.lower():
        try:
            os.remove(SOURCE_FILE)
        except OSError:
            pass
        return paths.GITHUB_REPO
    os.makedirs(os.path.dirname(SOURCE_FILE), exist_ok=True)
    json.dump({"repo": r}, open(SOURCE_FILE, "w", encoding="utf-8"))
    return r


def _version(tag):
    return tuple(int(x) for x in re.findall(r"\d+", tag or "")[:3]) or (0,)


def latest(source=None):
    """(version tag, {asset name: download url}, release notes) of the newest release of source (default: repo()),
    or None (offline / none)"""
    req = urllib.request.Request(API % (source or repo()), headers={"Accept": "application/vnd.github+json",
                                                                    "User-Agent": paths.APP_NAME})
    try:
        with urllib.request.urlopen(req, timeout=8) as r:
            rel = json.load(r)
    except (OSError, ValueError):
        return None
    # (GitHub turns spaces in asset names into dots: "Blur.Map.Builder.exe" - matched to ours either way)
    by_key = {_key(a.get("name")): a.get("browser_download_url") for a in rel.get("assets") or []}
    assets = {n: by_key.get(_key(n)) for n in ASSETS}
    return rel.get("tag_name"), assets, (rel.get("body") or "").strip()


def _key(name):
    return (name or "").lower().replace(" ", "").replace(".", "")


def _local_name(name):
    """the name this program has in the player's game folder: the download keeps GitHub's dotted name
    ("Blur.Map.Builder.exe") unless the player renamed it - update THAT file, not a second copy next to it"""
    try:
        for f in os.listdir(paths.GAME):
            if f.lower().endswith(".exe") and _key(f) == _key(name):
                return f
    except OSError:
        pass
    return name


def newer(info):
    return info is not None and _version(info[0]) > _version(paths.VERSION)


def check_async(callback):
    """callback(info) on a worker thread when a NEWER release exists (use root.after to get back to tk)"""
    def run():
        info = latest()
        if newer(info):
            callback(info)
    threading.Thread(target=run, daemon=True).start()


def cleanup_old():
    """remove the .old files a previous update left (they could not be deleted while running)"""
    for name in ASSETS:
        old = os.path.join(paths.GAME, _local_name(name) + ".old")
        try:
            if os.path.exists(old):
                os.remove(old)
        except OSError:
            pass


def install(info, progress=None):
    """download + swap the programs (only when running as the exe); restart() then starts the new map builder.
    progress(name, bytes so far, total bytes) is called on THIS thread (run it on a worker: the window keeps
    drawing). Returns an error text or None"""
    if not paths.FROZEN:
        return "Updates install into the exe version only (this is the source version: git pull instead)."
    _, assets, _ = info
    todo = [(_local_name(n), assets[n]) for n in ASSETS if assets.get(n)]
    if not todo:
        return "The release has no program files."
    new_files = []
    for name, url in todo:
        dst = os.path.join(paths.GAME, name + ".new")
        try:
            req = urllib.request.Request(url, headers={"User-Agent": paths.APP_NAME})
            with urllib.request.urlopen(req, timeout=60) as r, open(dst, "wb") as f:
                total = int(r.headers.get("Content-Length") or 0)
                got = 0
                while True:
                    block = r.read(1 << 16)
                    if not block:
                        break
                    f.write(block)
                    got += len(block)
                    if progress:
                        progress(name, got, total)
        except OSError as ex:
            return ("Download failed - check your internet connection and try again. Nothing was changed.\n(%s: %s)"
                    % (name, getattr(ex, "reason", None) or ex))
        if os.path.getsize(dst) < 1_000_000 or open(dst, "rb").read(2) != b"MZ":
            return "The downloaded %s is not a program - update stopped, nothing changed." % name
        new_files.append(name)
    for name in new_files:                       # swap: running exe -> .old, new -> its name
        cur = os.path.join(paths.GAME, name)
        try:
            if os.path.exists(cur + ".old"):
                os.remove(cur + ".old")
            if os.path.exists(cur):
                os.replace(cur, cur + ".old")
            os.replace(cur + ".new", cur)
        except OSError as ex:
            return "Could not replace %s: %s" % (name, ex)
    return None


def restart():
    """start the (new) map builder"""
    # the new exe must unpack ITSELF: without the reset it took over this (old) exe's temp folder (_MEI...), which
    # this exe deletes when it closes - the update "failed" and 3D / AI path then crashed (user report 2026-10-21)
    env = {k: v for k, v in os.environ.items() if not k.startswith(("_PYI_", "_MEI"))}
    env["PYINSTALLER_RESET_ENVIRONMENT"] = "1"
    subprocess.Popen([os.path.join(paths.GAME, _local_name(ASSETS[0]))], cwd=paths.GAME, env=env)

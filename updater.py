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


def _version(tag):
    return tuple(int(x) for x in re.findall(r"\d+", tag or "")[:3]) or (0,)


def latest():
    """(version tag, {asset name: download url}, release notes) of the newest release, or None (offline / none)"""
    req = urllib.request.Request(API % paths.GITHUB_REPO, headers={"Accept": "application/vnd.github+json",
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
        old = os.path.join(paths.GAME, name + ".old")
        try:
            if os.path.exists(old):
                os.remove(old)
        except OSError:
            pass


def install(info, progress=None):
    """download + swap the programs (only when running as the exe); then start the new map builder. Returns an
    error text or None"""
    if not paths.FROZEN:
        return "Updates install into the exe version only (this is the source version: git pull instead)."
    _, assets, _ = info
    todo = [(n, assets[n]) for n in ASSETS if assets.get(n)]
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
                    if progress and total:
                        progress(name, got / total)
        except OSError as ex:
            return "Download failed (%s): %s" % (name, ex)
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
    subprocess.Popen([os.path.join(paths.GAME, ASSETS[0])], cwd=paths.GAME)
    return None

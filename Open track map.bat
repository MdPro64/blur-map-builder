@echo off
rem Opens the live track map + route recorder (F7 in the game = start/stop recording).
cd /d "%~dp0"
start "" pythonw track_map.py

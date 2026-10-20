@echo off
cd /d "%~dp0"
python map_editor.py
if errorlevel 1 pause

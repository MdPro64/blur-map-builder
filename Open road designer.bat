@echo off
cd /d "%~dp0"
rem the 3D view needs two free packages (PyOpenGL, pyopengltk): install them once if they are missing
python -c "import OpenGL, pyopengltk" 2>nul || python -m pip install --user PyOpenGL pyopengltk
python road_designer.py
if errorlevel 1 pause

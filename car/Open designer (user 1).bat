@echo off
rem Custom car side project: the designer from this checkout, working on the J:lur - user 1 game + its maps
set BMB_GAME=J:lur - user 1
set BMB_INSTALLED=1
cd /d "%~dp0.."
python road_designer.py

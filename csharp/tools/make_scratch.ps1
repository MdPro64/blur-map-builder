# Makes the test area scratch\game: the Python build and the C# tests run there, so nothing is written to a game folder.
#   powershell -ExecutionPolicy Bypass -File tools\make_scratch.ps1 ["J:\blur - user 1"]
# base = a LINK to the game's base files (only read); maps = a COPY. Delete the link with:  cmd /c rmdir "scratch\game\Blur Map Builder\base"
param([string]$Game = "J:\blur - user 1")
$root = Split-Path -Parent $PSScriptRoot
$s = Join-Path $root "scratch\game"; $d = Join-Path $s "Blur Map Builder"
New-Item -ItemType Directory -Force "$d\work", "$d\logs", "$s\levels\showroom", "$s\rebrn", (Join-Path $root "scratch\ui"), (Join-Path $root "scratch\cs_work"), (Join-Path $root "scratch\cs_levels\showroom") | Out-Null
if (-not (Test-Path "$d\base")) { cmd /c mklink /J "$d\base" "$Game\Blur Map Builder\base" | Out-Null }
if (-not (Test-Path "$d\maps")) { Copy-Item -Recurse "$Game\Blur Map Builder\maps" "$d\maps" }
Copy-Item "$Game\levels\showroom\Level.levelphysics" "$s\levels\showroom\Level.levelphysics"
Copy-Item "$Game\levels\showroom\Level.levelphysics" (Join-Path $root "scratch\cs_levels\showroom\Level.levelphysics")
"ready: $s"

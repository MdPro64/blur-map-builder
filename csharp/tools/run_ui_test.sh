#!/bin/bash
# starts the app on the scratch copy of the maps, drives every tab, writes screenshots + timings. Nothing goes to a game folder.
R="E:\blur - bak\map_builder_csharp"
export DOTNET_ROOT="$R\.dotnet" BMB_GAME="$R\scratch\game" BMB_WORK="$R\scratch\cs_work" BMB_PACKS="$R\scratch\cs_work\rebrn" BMB_LEVELS="$R\scratch\cs_levels" BMB_PREVIEW="$R\scratch\preview" BMB_TEST=1 BMB_TEST_GLB="$R\scratch\sample.glb"
cd "/e/blur - bak/map_builder_csharp"
rm -f scratch/ui/ui_error.txt
timeout 300 "src/Bmb.App/bin/Release/net10.0-windows/Blur Map Builder.exe" --selftest "$R\scratch\ui"
echo "exit $?"
cat scratch/ui/ui_error.txt 2>/dev/null; cat scratch/ui/ui_timing.txt 2>/dev/null

#!/bin/bash
# the unchanged Python build on the scratch copy (nothing goes to a game folder). usage: run_py_build.sh OUTNAME [nocars]
S="E:\blur - bak\map_builder_csharp\scratch\game"
export BMB_GAME="$S" BMB_INSTALLED=1 PYTHONDONTWRITEBYTECODE=1
[ "$2" = nocars ] && export BMB_NO_CARS=1
cd "/e/blur - bak/map_builder"
start=$(date +%s.%N)
python -B flat_earth/build.py > "/e/blur - bak/map_builder_csharp/scratch/$1.log" 2>&1
code=$?
end=$(date +%s.%N)
echo "exit $code seconds $(python -c "print(round($end-$start,1))")" | tee -a "/e/blur - bak/map_builder_csharp/scratch/$1.log"
W="/e/blur - bak/map_builder_csharp/scratch/game/Blur Map Builder/work"
mkdir -p "/e/blur - bak/map_builder_csharp/scratch/$1"
mv "$W/gamedata_track.pak" "$W/root_track.pak" "/e/blur - bak/map_builder_csharp/scratch/$1/"
cp -r "/e/blur - bak/map_builder_csharp/scratch/game/levels" "/e/blur - bak/map_builder_csharp/scratch/$1/"
cp -r "$W/flat_earth" "/e/blur - bak/map_builder_csharp/scratch/$1/"

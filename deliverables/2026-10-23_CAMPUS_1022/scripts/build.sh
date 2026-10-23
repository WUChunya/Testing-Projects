#!/bin/bash
# Rebuild the CAMPUS 1022 model from the user's file. Usage: build.sh <CAMPUS_2026_1022.3dm> <repo> <workdir>
# Then export: dotnet run (Program.cs) <workdir>/c22_park.json <workdir>/c22_park_settings.json <outdir> [drawings], with
# PD_LABEL_NAMED=1 for the labelling of this round; svgs_to_pdf.mjs + merge_pdf.py make the PDF.
set -e
S=$(cd "$(dirname "$0")" && pwd); src=$1; repo=$2; W=$3; T=$repo/tools/fixture_from_3dm.py
mkdir -p $W
python3 -I $T $src $W/c22.json --full >/dev/null
python3 -I $S/c22_clean.py $W/c22.json $src $W/c22_clean.3dm
python3 -I $T $W/c22_clean.3dm $W/c22_clean.json --full >/dev/null
python3 -I $S/c22_park.py $W/c22_clean.json $W/c22_clean.3dm $W/c22_park.3dm
python3 -I $T $W/c22_park.3dm $W/c22_park.json --full | tail -1

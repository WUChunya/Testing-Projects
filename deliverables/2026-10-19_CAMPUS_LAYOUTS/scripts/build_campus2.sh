#!/usr/bin/env bash
# Build the plugin-ready CAMPUS 261019 model from the user's files.
# Usage: build_campus2.sh <261019_Campus_Layouts.3dm> <20261016_Site_Plan_Town_1.100.ai> <repo> <workdir> <base settings.json>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
src3dm=$1; ai=$2; repo=$3; w=$4; base=$5
mkdir -p "$w"
python3 -I "$here/extract_ai.py" "$ai" "$w/town.json"
python3 -I "$here/town2_to_3dm.py" "$w/town.json" "$src3dm" "$w/c2_site.3dm" "$here/campus2_cuts.json"
python3 -I "$here/campus2_settings.py" "$w/c2_site.3dm" "$w/c2_pd0.3dm" "$base" "$here/campus2_cuts.json"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/c2_pd0.3dm" "$w/c2_pd0.json" --full >/dev/null
python3 -I "$here/campus2_solids.py" "$w/c2_pd0.json" "$w/c2_pd0.3dm" "$w/c2_pd.3dm" "$here/campus2_names.json"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/c2_pd.3dm" "$w/c2_pd.json" --full

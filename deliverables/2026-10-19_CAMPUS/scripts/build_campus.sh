#!/usr/bin/env bash
# Build the plugin-ready CAMPUS model from the user's files.
# Usage: build_campus.sh <CAMPUS_2026_1019.3dm> <20261016_Site_Plan_Town_1.100.ai> <repo> <workdir>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
src3dm=$1; ai=$2; repo=$3; w=$4
mkdir -p "$w"
python3 -I "$here/extract_ai.py" "$ai" "$w/town.json"
python3 -I "$here/town_fit.py" "$w/town.json" "$src3dm" | head -1
python3 -I "$here/town_to_3dm.py" "$w/town.json" "$src3dm" "$w/campus_site.3dm"
python3 -I "$here/campus_settings.py" "$w/campus_site.3dm" "$w/campus_pd0.3dm"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/campus_pd0.3dm" "$w/campus_pd0.json" --full >/dev/null
python3 -I "$here/campus_solids.py" "$w/campus_pd0.json" "$w/campus_pd0.3dm" "$w/campus_pd.3dm" | tail -3
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/campus_pd.3dm" "$w/campus_pd.json" --full

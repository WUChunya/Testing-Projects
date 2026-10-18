#!/usr/bin/env bash
# Rebuild the revised RIVER model from the user's files.
# Usage: build_model.sh <RIVER_2026_1016_FINAL.3dm> <20261016_Site_Plan_River_1.100.ai> <repo> <workdir>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
src3dm=$1; ai=$2; repo=$3; w=$4
mkdir -p "$w"
python3 -I "$here/extract_ai.py" "$ai" "$w/site.json"
python3 -I "$repo/tools/fixture_from_3dm.py" "$src3dm" "$w/original.json" >/dev/null
python3 -I "$here/site_to_3dm.py" "$w/site.json" "$src3dm" "$w/site.3dm"
python3 -I "$here/update_settings.py" "$w/site.3dm" "$w/site_settings.json"
python3 "$here/revise_model.py" "$w/original.json" "$w/site.3dm" "$w/rev.3dm" "$repo/styles/program_legend.json"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/rev.3dm" "$w/rev.json" >/dev/null
python3 "$here/waterfront.py" "$w/rev.json" "$w/rev.3dm" "$w/park.3dm"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/park.3dm" "$w/park.json"

#!/usr/bin/env bash
# RIVER 2026-10-20 check-in → cleaned model with the riverfront park on the new outline, site plan + plans exported.
# Usage: build_r20.sh <RIVER_2026_1020_checkin.3dm> <repo> <workdir>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; src=$1; repo=$2; w=$3
mkdir -p "$w"
python3 -I "$repo/tools/fixture_from_3dm.py" "$src" "$w/r20.json" --full >/dev/null
python3 -I "$here/r20_clean.py" "$w/r20.json" "$src" "$w/r20_clean.3dm"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/r20_clean.3dm" "$w/r20_clean.json" --full >/dev/null
python3 "$here/waterfront2.py" "$w/r20_clean.json" "$w/r20_clean.3dm" "$w/r20_park.3dm"
python3 -I "$here/r20_output.py" "$w/r20_park.3dm" "$w/r20_pd.3dm" "$w/r20_pd_settings.json"
python3 -I "$repo/tools/fixture_from_3dm.py" "$w/r20_pd.3dm" "$w/r20_pd.json" --full

#!/usr/bin/env bash
# RIVER 2026-10-21 (VP comments) → edited model with the riverfront park, site plan + plans exported.
# Usage: build_r21.sh <RIVER_2026_1020_checkin.3dm> <repo> <workdir>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; src=$1; repo=$2; w=$3
fx() { python3 -I "$repo/tools/fixture_from_3dm.py" "$1" "$2" --full >/dev/null; }
mkdir -p "$w"
fx "$src" "$w/r20.json"
python3 -I "$here/r20_clean.py" "$w/r20.json" "$src" "$w/r20_clean.3dm"
fx "$w/r20_clean.3dm" "$w/r20_clean.json"
python3 -I "$here/r21_edits.py" "$w/r20_clean.json" "$w/r20_clean.3dm" "$w/r21_edit.3dm"
fx "$w/r21_edit.3dm" "$w/r21_edit.json"
python3 -I "$here/r20_interior.py" "$w/r21_edit.json" "$w/r21_edit.3dm" "$w/r21_int.3dm"
fx "$w/r21_int.3dm" "$w/r21_int.json"
python3 -I "$here/r21_more.py" "$w/r21_int.json" "$w/r21_int.3dm" "$w/r21_more.3dm"
fx "$w/r21_more.3dm" "$w/r21_more.json"
python3 "$here/waterfront3.py" "$w/r21_more.json" "$w/r21_more.3dm" "$w/r21_park.3dm"
python3 -I "$here/r20_output.py" "$w/r21_park.3dm" "$w/r21_pd.3dm" "$w/r21_pd_settings.json"
fx "$w/r21_pd.3dm" "$w/r21_pd.json"

#!/usr/bin/env bash
# RIVER 2026-10-21 DRAFT_1 → spiral stairs, auditorium furniture, gardens / terraces, riverfront park, site paving.
# Usage: build_d1.sh <RIVER_2026_1021_DRAFT_1.3dm> <repo> <workdir>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; src=$1; repo=$2; w=$3
fx() { python3 -I "$repo/tools/fixture_from_3dm.py" "$1" "$2" --full >/dev/null; }
mkdir -p "$w"
fx "$src" "$w/d1.json"
python3 -I "$here/d1_site.py" "$w/d1.json" "$src" "$w/d1a.3dm"
fx "$w/d1a.3dm" "$w/d1a.json"
python3 "$here/waterfront7.py" "$w/d1a.json" "$w/d1a.3dm" "$w/d1b.3dm"
fx "$w/d1b.3dm" "$w/d1b.json"
python3 -I "$here/d1_plaza.py" "$w/d1b.json" "$w/d1b.3dm" "$w/d1c.3dm"

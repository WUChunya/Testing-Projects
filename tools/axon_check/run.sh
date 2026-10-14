#!/bin/bash
# 03 stacked axon vs the model (Downtown fixture, user settings, NE view): dumps the axon, renders the fixture with a
# z-buffer (what Rhino shows), rasterises both by program / slab class and lists the regions where they disagree as
# "our item | model object". Needs numpy, pillow, scipy. Usage: tools/axon_check/run.sh [workdir]
# Expected noise: the basement (the model render has no ground) and 1 ft slab edges (03 draws no slab thickness).
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
here="$root/tools/axon_check"
W="${1:-/tmp/axon_check}"
mkdir -p "$W/dump" "$W/shots"
cd "$root"
PD_DUMP="$W/dump" PD_USER=1 PD_ONLY=AxonStacked dotnet test src/PD.Core.Tests --filter DumpForReview -v q --nologo | tail -1
python3 -I "$here/model_iso.py" tests/fixtures/downtown.json "$W/shots/zref_al.png" 45 1e9 PROGRAM 2400 >/dev/null
F="$W/dump/03_Axon_Stacked_AXON_STACKED.svg"
read -r ox oy < <(python3 -I - "$F" <<'P'
import sys, re
s = open(sys.argv[1]).read()
m = re.search(r'id="AXS_OUTLINE"[^>]*d="([^"]+)"', s)
pts = [tuple(map(float, p)) for p in re.findall(r'(-?[\d.]+),(-?[\d.]+)', m.group(1))]
print(min(a for a, b in pts), min(b for a, b in pts))
P
)
# 4.84 = model render px per drawing pt (2400 px render of the 1/64" axon); the outline's corner pins the offset.
python3 -I "$here/ourclass.py" "$F" "$W/ourcls.svg" 4.84 "$ox" "$here" "$oy" 2400 2669
IDS=1 python3 -I "$here/ourclass.py" "$F" "$W/ourid.svg" 4.84 "$ox" "$here" "$oy" 2400 2669
node tools/svg_shot.mjs "$W/ourcls.svg" "$W/shots/ourcls.png" 2400
node tools/svg_shot.mjs "$W/ourid.svg" "$W/shots/ourid.png" 2400
python3 -I "$here/regions.py" "$here" "$W" tests/fixtures/downtown.json

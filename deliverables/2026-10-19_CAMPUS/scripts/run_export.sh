#!/usr/bin/env bash
# Plugin export on Linux (PD.Core harness) + review PNGs. Usage: run_export.sh <fixture.json> <settings.json> <outdir>
set -euo pipefail
S=$(cd "$(dirname "$0")/.." && pwd)
rm -rf "$3"; (cd $S/harness && PD_WRITE="$3" dotnet run -c Release -- "$1" "$2" 2>&1 | grep -v warn | tail -6)
mkdir -p "$3/../png_$(basename $3)"; rm -f "$3/../png_$(basename $3)"/*
(cd "$3/_preview" && node $S/scripts/svgs_to_png.mjs "$3/../png_$(basename $3)" 1400 *.svg)

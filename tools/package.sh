#!/usr/bin/env bash
# Builds Release and packs dist/ProgramDiagram-<ver>.zip (unzip, drag the .rhp into Rhino 8)
# and dist/programdiagram-<ver>-rh8_0-win.yak (Yak package, VERIFY-WIN).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
ver=$(grep -oP '(?<=<Version>)[^<]+' "$root/src/PD.Rhino/PD.Rhino.csproj")
out="$root/src/PD.Rhino/bin/Release/net7.0-windows"
dotnet build "$root/src/PD.Rhino" -c Release -v q --nologo >/dev/null
stage=$(mktemp -d)
mkdir -p "$stage/ProgramDiagram" "$root/dist"
cp "$out"/ProgramDiagram.rhp "$out"/PD.Core.dll "$out"/NetTopologySuite.dll "$stage/ProgramDiagram/"
rm -f "$root"/dist/*.zip "$root"/dist/*.yak
(cd "$stage" && python3 -I -m zipfile -c "$root/dist/ProgramDiagram-$ver.zip" ProgramDiagram)
cp "$root/src/PD.Rhino/yak/manifest.yml" "$stage/ProgramDiagram/"
(cd "$stage/ProgramDiagram" && python3 -I -m zipfile -c "$root/dist/programdiagram-$ver-rh8_0-win.yak" manifest.yml ProgramDiagram.rhp PD.Core.dll NetTopologySuite.dll)
rm -rf "$stage"
ls -la "$root/dist"

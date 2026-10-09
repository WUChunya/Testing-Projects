# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Next session: **S1 Core geometry** (see `docs/DEV_PROMPT.md`).
- S0 done (2026-10-09): `PD.sln` (Core / Core.Tests / Rhino) builds with 0 warnings; 7 fixture/style smoke tests green.
  `tests/fixtures/template.json` (0.77 MB, all meshes kept, coords in mm rounded to 1 mm, Core converts to ft).
  `styles/program_legend.json`, `style_default.json`, `layer_keywords.json` embedded in PD.Core (`StyleResources`).
- Env: .NET 8 SDK via `apt-get install dotnet-sdk-8.0` (dot.net installer host is blocked by the network policy);
  `pip install rhino3dm`. Tests target net7.0 with `RollForward=Major` (only the 8.0 runtime is installed).

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels → keep as tall space; ≥3 → split per level.
- Single surfaces on program layers: flagged, user chooses (ignore / facade paint / extrude).
- Facade: stacked axon = 30% translucent veil; exploded axon = hidden.
- Label font: Century Gothic (configurable). Platform: Windows + Rhino 8 only.
- RhinoCommon pinned to 8.0.23304.9001 (works on any Rhino 8), Eto 2.8.2, both compile-only; NU1701 suppressed.
- Layer keywords match the layer's own name (last `::` segment) by regex, first match wins; program layers match legend aliases.
- Fixture keeps source units; `SourceObject` DTO = id, layerPath, type, name, role, programKey, bbox, isClosed, faceCount, volume, mesh, points.

## Windows test checklist (for the user)
- (none yet)

## Known issues
- (none yet)

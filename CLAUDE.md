# Program Diagram — Rhino 8 plugin (Windows)

Spec: `docs/PRD.md` (Chinese). Progress/handoff: `docs/PROGRESS.md`. Session plan: `docs/DEV_PROMPT.md`.
Talk to the user in Chinese; write code, comments, commits in English.

## Layout
- `src/PD.Core` (net7.0, NO Rhino refs): data model, units/localize, level clustering, mesh slicing, 2D cleanup/overlap (NetTopologySuite), styles, layout, writers (JSX/SVG/CSV), charts, entourage. Must be testable on Linux.
- `src/PD.Core.Tests` (xUnit): runs against `tests/fixtures/*.json`.
- `src/PD.Rhino` (net7.0-windows, RhinoCommon 8 + Eto): plugin, commands, panel, conduits, 3D extraction/cleanup (cap, mesh, section). Compiles on Linux (`EnableWindowsTargeting=true`), runs only on the user's Windows machine.
- `illustrator/`: JSX runtime library embedded as resource. `styles/`: default style/legend/keyword JSON. `tools/`: Python helpers (rhino3dm).

## Commands
- Build: `dotnet build PD.sln -v q --nologo 2>&1 | tail -15`
- Test: `dotnet test PD.sln -v q --nologo 2>&1 | tail -20`
- Fixture: `python3 -I tools/fixture_from_3dm.py tests/fixtures/TEMPLATE_AUTOMATION_TESTING.3dm tests/fixtures/template.json`

## Rules
- Rhino → Core boundary is plain DTOs (meshes, polylines, layer info). All logic that can live in Core lives in Core.
- Internal units: feet; convert at the boundary. Coordinates localized to building bbox min.
- Every user-visible default lives in `styles/*.json`, never hard-coded.
- AI output: global swatches, paragraph styles, stable object names `L03_LAB_01`; never touch `_MANUAL`/`OVERLAY` layers.
- Token economy: read PRD sections by heading (`grep -n '^#' docs/PRD.md`, then `sed -n`), never whole large files; never `Read` .3dm or fixture JSON (query with python/jq); pipe long output through `tail`; edit, don't rewrite; no subagents unless asked; final reply ≤10 lines, details go to `docs/PROGRESS.md`.

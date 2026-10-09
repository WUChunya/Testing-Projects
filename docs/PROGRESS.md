# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Next session: **S3 Output engine + Section** (see `docs/DEV_PROMPT.md`). Swatches/legend come from `PdSession.Programs` (custom included).
- Rev 1 (v0.1.1): custom programs on ⑤ (add / delete / aliases / create from Program layers with no key), linked to ① layer
  combo and ④ per-object program (`ObjectPrograms`). Program layer without key → UNASSIGNED. 41 tests green.
- Windows run of v0.1.0 (user, 2026-10-09): rhp installs from a user folder; T1 ✓ 11/11 layers; T3 ✓ 11 levels (Rhino tight bbox
  gives 104'-2"/116'-8"/130'-2", within 0.5 ft); T4 ✓ every level total = sum of programs; 2 single surfaces → user chose Facade.
- S2 done: `.rhp` with `ProgramDiagram` (panel ①–⑧), `ProgramDiagramLevels` (level preview), `ProgramDiagramPickSection`.
  Build: `tools/package.sh` → `dist/*.zip`. ⑦ Export writes only areas.csv + settings.json until S3.
- S1 done: Core pipeline `Analyzer.Run` (roles → localize → masses → levels → span/overlap → issues). 29 tests green: T1 12/12, T2 5+2, T3 (11 levels, 3 outliers flagged), T4 areas add up.
- Env: `apt-get install dotnet-sdk-8.0` (dot.net blocked), `pip install rhino3dm`; tests `RollForward=Major`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → keep as tall space (area counted on base level, upper = open-to-below); else split.
- Single surfaces on program layers: flagged, user chooses (ignore / facade / extrude `singleSurfaceExtrudeFt`).
- Facade: stacked axon = 30% translucent veil; exploded axon = hidden. Label font: Century Gothic. Windows + Rhino 8 only.
- Localize moves XY only; Z stays (it is the level datum). Stray = XY gap > max(500 ft, 3× building diagonal).
- Levels: slab tops anchor; mass/facade bottoms snap down ≤1'-6"; one hop from a snapped candidate joins (flag scattered);
  ≥3 slab-less bottoms form a level (L1 here); facade bottoms count only if repeated ≥2. Topmost main level = roof.
- PRD 7.1 says 3 four-sided tubes; data has one 4-face LAB missing top *and* a side (non-planar hole) → closed along section (`SectionClosed`, flagged).
- Geometry/level/output defaults in `styles/rules_default.json`; RhinoCommon 8.0.23304 + Eto 2.8.2 compile-only.
- Program keys are immutable (object ids, AI swatches); labels editable. Custom aliases beat legend aliases in auto-fill.
  Legend styles (Shell stroke, Void dash, Unassigned hatch) are re-applied to saved schemes that lack them.
- Stable ids `L03_LAB_01` (base level, program key, counter by position). Settings JSON in DocumentUserText `ProgramDiagram/settings`.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.1.1.zip` into a user folder (not Program Files) (Properties → Unblock first); drag `ProgramDiagram.rhp` into Rhino 8. Panel "Program Diagram" registers (icon = stacked bars). VERIFY-WIN icon.
2. Open the test model, run `ProgramDiagram`: command line reports ~21 masses, 11 levels.
3. ① 12 layers listed, roles pre-filled (LOUNGE → INTERACTION, UTILITY → BUILDING_SUPPORT). Change one role, Analyze, reopen file: choice kept.
4. ② Status counts: Rhino CapPlanarHoles shows "已封口" for the open boxes; 2 single surfaces listed; stray slab mesh listed; double-click zooms.
5. ③ Levels L1 0'-0" … L11 130'-3"; tick Preview → translucent planes + labels in viewports. Edit a name, Apply; Re-detect restores.
6. ④ IDs like L01_LOBBY_01; edit a label. ⑤ swatches visible; Pick colour opens Rhino dialog. ⑥ Pick a line → ✓/✗ crossing.
6b. ⑤ add "Clean Room" → appears in ① and ④ Program combos; set UTILITY to it; ④ change one OFFICE mass → ID becomes L0x_CLEAN_ROOM_01; delete → Unassigned.
7. ⑧ Items listed; double-click selects/zooms; ignore tick persists. ⑦ Export enabled only after all 8 confirmed; writes areas.csv.
8. Optional: drag the `.yak` into Rhino (VERIFY-WIN that Rhino 8 installs it).

## Known issues
- Section-closed volume ≈ mid area × height (overlap priority only). Underground context trimming (PRD 3.5) and overlap-by-layer-priority rule not implemented yet (S3/S4 drawing stage).

# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Next session: **S4 Plan + Axon** (see `docs/DEV_PROMPT.md`). Reuse `ExportContext`, `PageMap`-style mapping, `Drawing` model, `Exporter.BuildDrawings`.
- S3 done (v0.2.0): `Drawing` model → `SvgWriter` (preview) + `JsxWriter` (runtime + data literal) + `Exporter` (program_areas.csv, diagram.json,
  export_log.txt, `_preview/*.svg`, ProgramDiagram.jsx); `IllustratorLauncher` COM; golden `tests/golden/section_A.svg`.
- Rev 5 (v0.3.0): `Views/ProjectedView` = Make2D-like opaque view (masses light-grey base; one AI layer + global swatch per
  slab/facade source layer, glass white; near hides far via trim by nearer lower-layer items). Used behind section cuts and by
  new `ElevationDrawing` (⑥ type = Elevation or ELEV:: layers; ▼ level markers, dashed level lines). 57 tests green.
- Rev 4 (v0.2.2): text + annotation offsets scale with drawing scale (reference 1" = 45'-0" = v0.2.0 sizes); level column, height dim,
  visible level lines back; beyond (ours + others) = white fill + dashed grey, painted far→near, facade grid clipped by nearer masses;
  cut context = white fill + 2.5 pt black; ground line = 2 × slab, centred on grade; basements draw like other levels. 56 tests green.
- Rev 3 (v0.2.1): true scale (any Bluebeam-style scale), "Fit content" artboard, visual 3 pt slabs, centred bold labels, optional PNG.
- Rev 1–2: custom programs (⑤ ↔ ① ↔ ④); span "Single" = base level only; edits and Export re-analyse first.
- Windows runs (user): v0.1.0/0.1.1 install from a user folder; T1 ✓; T3 ✓ 11 levels; T4 ✓; Linux Core reproduces the user's areas.csv exactly.
- Env: `apt-get install dotnet-sdk-8.0` (dot.net blocked), `pip install rhino3dm`; tests `RollForward=Major`; node 22 for `--check`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space (base level counts); else split. Facade: stacked 30% veil, exploded hidden.
- Localize XY only (Z = level datum). Levels: slab tops anchor, bottoms snap down ≤1'-6", one-hop join, ≥3 slab-less bottoms form a level.
- One 4-face LAB is missing top *and* a side → closed along section (`SectionClosed`, flagged).
- Defaults live in `styles/` (`rules_default.json` geometry/output/artboards, `style_default.json` section/page/fonts with PostScript names).
- Program keys immutable (ids `L03_KEY_01`, swatches `PRG_<KEY>`); custom aliases beat legend aliases.
- Section bars come from the resolved plan footprints ∩ cut line (same overlap result as areas); outline = union of bar bands.
- AI update rule: generated layers drop their *named* items and redraw; unnamed items, `_MANUAL`, `OVERLAY` untouched; swatches recoloured in place.
- Item names: `SEC_<sec>_<stableId>`, `_LBL`, `SEC_<sec>_OUTLINE|SLAB_<z>_<u>|VIEW_<KIND>_nnn|GROUND`; elevations `ELV_<name>_…`.
- Elevation: neighbours straddling the view line are omitted; our building always shown. Layers `VIEW_<own layer name>`.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.3.0.zip` to a user folder, replace old files, restart Rhino.
2. Open the test model; ⑥ keep section A; confirm all tabs; ⑦ Export (auto Illustrator ticked).
3. Illustrator starts, writes `02_Section_A.ai`; message shows illustrator_log.txt "OK". VERIFY-WIN COM launch.
4. Open the .ai/.png: true scale, labels centred in blocks (VERIFY-WIN glyph centring), PD_* layers + FACADE/_MANUAL/OVERLAY, global PRG_* swatches.
5. Draw something unnamed in PD_PROGRAM and something on _MANUAL; change a mass; export again → both kept, bars updated.
6. ⑥ add a line south of the building, type Elevation; ⑦ tick Elevation; export → `07_Elevation_*.ai`: VIEW_MASS, VIEW_SLAB, VIEW_GLASS layers.
7. Earlier checks (①–⑧, custom programs) as before.

## Known issues
- Not yet: street names, building name above, key plan, trees, stair hatch, Unassigned hatch (grey) — next revision candidates.
- Section-closed volume ≈ mid area × height. Underground context trimmed in sections only. Layer-priority overlap rule not implemented.

# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Next session: **S4 Plan + Axon** (see `docs/DEV_PROMPT.md`). Reuse `ExportContext`, `PageMap`-style mapping, `Drawing` model, `Exporter.BuildDrawings`.
- S3 done (v0.2.0): `Drawing` model → `SvgWriter` (preview) + `JsxWriter` (runtime + data literal) + `Exporter` (program_areas.csv, diagram.json,
  export_log.txt, `_preview/*.svg`, ProgramDiagram.jsx); `IllustratorLauncher` COM; Section A golden `tests/golden/section_A.svg`. 47 tests green.
  S3 gaps: key plan, street names, stair hatch (Circ-Vertical), basement pit, Unassigned hatch (solid grey for now).
- Rev 2: span "Single" on a multi-level mass = base level only; span edits and Export re-analyse first (v0.1.1 exported stale results).
- Rev 1 (v0.1.1): custom programs on ⑤, linked to ① layer combo and ④ per-object program (`ObjectPrograms`); no key → UNASSIGNED.
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
- Item names: `SEC_<sec>_<stableId>`, `_LBL`, `SEC_<sec>_OUTLINE|SLAB_nn|BEYOND_nnn|LVL_<L>`.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.2.0.zip` to a user folder, replace old files, restart Rhino.
2. Open the test model; ⑥ keep section A; confirm all tabs; ⑦ Export (auto Illustrator ticked).
3. Illustrator starts, writes `02_Section_A.ai`; message shows illustrator_log.txt "OK". VERIFY-WIN COM launch.
4. Open the .ai: layers PD_GROUND…PD_ANNOTATION, FACADE, _MANUAL, OVERLAY; swatches PRG_LAB etc. are global; text uses "PD Label"
   (Century Gothic, fallback Arial). Rotated texts (height dim, property line) sit right. VERIFY-WIN rotation pivot.
5. Draw something unnamed in PD_PROGRAM and something on _MANUAL; change a mass; export again → both kept, bars updated.
6. Recolour swatch PRG_LAB in ⑤, export → existing .ai recolours. Compare with `_preview/02_Section_A.svg`.
7. Earlier checks (①–⑧, custom programs) as before.

## Known issues
- Golden SVG title/scale text present in markup but not seen in the headless screenshot (preview only; verify in Illustrator).
- Section-closed volume ≈ mid area × height. Underground context trimmed in sections only. Layer-priority overlap rule not implemented.

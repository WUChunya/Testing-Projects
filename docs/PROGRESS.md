# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 11 (v0.6.1, 129 tests): v0.6.1 fixes the export crash "Operation does not support GeometryCollection arguments":
  a clipped projection came back as a GeometryCollection and the next overlay refused it. Every area result now goes through
  `MeshSlicer.Polygonal` (lines through `Lineal`), the geometry guards also catch `ArgumentException`, and a drawing that
  still fails is reported as a note instead of losing the whole export.
- Rev 11 (v0.6.0, 128 tests): **S5 Charts + Entourage** plus S1–S4 fixes from the user's `261012_Downtown_Massing.3dm`.
  Charts (`src/PD.Core/Charts`): `ProgramTable` (model GSF or Excel ASF via ClosedXML, template writer, `program_table.csv`),
  `05_Program_Chart.ai` (PRD 5.5 dashed department boxes, sub-group rows, area-proportional rectangles, shelf packing),
  `06_Program_Pie.ai` (PRD 5.6 donut 43 %, 12 o'clock clockwise in scheme order, legend + TOTAL, stacked bar).
  Entourage (`src/PD.Core/Entourage`): seeded Poisson disk, density per program (`style.entourage`), `ENTOURAGE` layer in
  plans / sections / elevations / axons, people #9A9A9A, modelled people (role `Entourage`) reused; section trees (SITE_STYLE §4).
- Downtown fixes: `FLOOR PLATE` → NC.Slab (levels were missing); layers under `PROGRAM::` stay programs even when the name
  matches nothing (Seminar Room, Support, Green Roof); new program `GREEN_ROOF`; Make2D / clipping-plane layers ignored by path;
  new roles `NC.Roof` and `Entourage`; GRID / OUTLINE are drafting layers (Ignore); CADMAPPER / walkway / lot keywords.
  Flat masses (≤ 2 ft: roof gardens, terraces) join the level under their top instead of being dropped; mass bottoms snap up to
  6" below a slab top (`slabSnapBelowFt`); only our own objects can be stray (context spreads over the whole site).
  Plans: one leader label per program per level, leader labels clamped to the sheet, legend columns sized to the longest name.
- Fixture tool: coordinates are always integer mm (`units` = Millimeters, `sourceUnits` keeps the model's), extrusions without a
  render mesh fall back to the Brep / bbox, oversized fixtures drop 2D output layers first. `tests/fixtures/downtown.json` added.
- Next: S6 acceptance + docs (`docs/USER_GUIDE.md`, `docs/MODELING_CONVENTION.md`, T1–T7).
  PRD v0.8; history `docs/CHANGELOG.md`; goldens: section_A, elevation_S, plan_L1, axon_*, program_chart, program_pie,
  downtown_plan_L1 / section_DT / axon_stacked / program_chart / program_pie.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off by default (`axon.facadeVeil`).
- Flat mass ≤ 2 ft = slab-like (roof garden, paving): one level, by its top face. Stray detection applies to NC / Program only.
- Defaults live in `styles/` (`rules_default.json`, `style_default.json` incl. `charts` and `entourage`, `layer_keywords.json` v3).
- Scales: plans/axons 1" = 45'-0", sections 1" = 16'-0"; charts are absolute points (`ctx.ForChart()`).
- Entourage seed is saved with the project (tab 8 "Re-randomize"); density 0 for Core / Stair / Shell / Mechanical.
- Program keys immutable (`L03_KEY_01`, swatches `PRG_<KEY>`); AI update: named items redrawn, `_MANUAL`/`OVERLAY`/unnamed kept.
- Env: apt dotnet-sdk-8.0, pip rhino3dm, node 22 + global playwright (--disable-gpu; screenshot fullPage:false).

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.6.1.zip` over the old files (it now ships ClosedXML and its dependencies), restart Rhino.
2. Open `261012_Downtown_Massing.3dm`, tab 1: `FLOOR PLATE` = NC.Slab, `COLUMNS` = NC.Structure, every `PROGRAM::*` = Program
   (Green Roof → Green Roof / Sky Garden, Support → Building Support, Seminar Room → Assembly), `PEOPLE / CARS` = Entourage,
   `NE ISO::…` / `1.80 [0]::…` / GRID = Ignore. Use the new "Show: NC / BG" filter and the Group column.
3. Tab 3: 13 levels (B1 at −21'-11", L1 at 0'-2", L12 at 194'-0"), no mezzanine, every level from a floor plate.
4. Tab 9: the roof garden at 212 ft is listed as a stray program mass (it sits 4,000 ft from the tower in the model) —
   keep it on tab 2 and it lands on L12 (28,800 SF). Check the other 8 stray program masses.
5. Tab 8: "Program Excel" + "Save Excel template…", "Draw entourage", "Re-randomize entourage"; tick ProgramChart and Pie.
6. Charts: `05_Program_Chart.ai` (dashed boxes, totals) and `06_Program_Pie.ai` (donut, legend, bar) — VERIFY-WIN text sizes.
7. illustrator_log.txt: all OK; send any WARN lines.

## Known issues
- Not yet: street names/arrows, flow/view/service icons, plaza stipple / lawn hatch, key plan, Unassigned hatch.
- Entourage symbols are built-in placeholders; the company `entourage.ai` symbols are not read yet.
- Context buildings modelled as one merged mesh cannot be extruded per building; section-closed volume ≈ mid area × height.
- Plan people are dots at 1" = 45'; switch entourage off for small-scale sheets.

# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 12 (v0.7.0, 133 tests), from the user's Downtown export (details in `docs/CHANGELOG.md`, PRD v0.9):
  Illustrator: swatch names ≤ 31 chars (cut names collided → "name was not found"), swatch / style failures fall back
  and WARN, transient COM faults retried once; elevations look towards our building (`AutoSide`). Tabs 1 / 4: Ctrl /
  Shift multi-select (`MultiSelect<T>`). New `00_Site_Plan.ai` (`PlanDrawing.BuildSite`) and `03_Site_Axon.ai`
  (`AxonKind.Site`, `axon.site`). Plans: `LevelKind` ground / upper (site + white veil) / basement (earth); label
  shrink to `plan.labelMinPt`, once per program. Sections: bars snapped to bands and centred, one slab thickness
  (`TypicalStory`), one slab per level (`SlabRanges`), `LevelMarkers` shared with elevations. Axons: pairwise hidden
  faces (`DepthOnPlane`, topological order) + `AxonFaces.Hide` for trees / people. Thin at-grade program = site paving.
- v0.6: S5 charts (`src/PD.Core/Charts`), entourage (`src/PD.Core/Entourage`), NetTopologySuite GeometryCollection fix.
- Fixtures: `template.json`, `downtown.json` (user model, mm). Goldens incl. site_plan / site_axon and downtown_*.
- Next: S6 acceptance + docs (`docs/USER_GUIDE.md`, `docs/MODELING_CONVENTION.md`, T1–T7).

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Flat mass ≤ 2 ft: raised = program on the level under its top; at grade = site paving / planting. Stray = NC only.
- Defaults live in `styles/` (`rules_default.json`, `style_default.json` incl. plan site/veil, axon.site, charts,
  entourage; `layer_keywords.json` v3). Scales: plans/axons 1" = 45', sections 1" = 16', charts absolute pt.
- Entourage only outdoors on the site plan / site axon (plus sections / elevations); seed saved with the project.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` (site axon) / unnamed.
- Env: apt dotnet-sdk-8.0, pip rhino3dm, node 22 + global playwright (--disable-gpu).

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.7.0.zip` over the old folder (whole folder: ClosedXML DLLs), restart Rhino.
2. Tab 1 / 4: Ctrl- or Shift-select rows, untick Use (or change Role / Program) on one → all selected follow
   (VERIFY-WIN: Eto grid keeps the selection while a cell is edited; else use the "Selected rows" buttons).
3. Tab 7: elevations with "Auto side" ticked look at the building whichever way the line was drawn.
4. Export all: `illustrator_log.txt` has no ERROR (03_Axon_Stacked fixed); `export_log.txt` no "not written".
5. `00_Site_Plan.ai` and `03_Site_Axon.ai` exist; upper plans show the veiled site, basements grey earth.
6. Section: equal slabs, one per level, bars centred, ▼ markers left. Exploded axon: labels from the left corner.

## Known issues
- Not yet: street names / arrows, flow / view / service icons (draw on `ANNOTATION`), key plan, Unassigned hatch.
- Entourage symbols are placeholders (company `entourage.ai` not read); plan people are dots at 1" = 45'.
- Site axon context at real height can hide a low building (template model); lower `axon.site.maxContextHeightFt`.

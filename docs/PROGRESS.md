# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 18 (v0.8.4, 210 tests, `Rev084Tests`, user settings fixture `downtown_settings.json` + `PD_USER=1`):
  exposed slabs / flat glass as roof / canopy (`Analysis/ExposedSurfaces`, style `exposed`) in plans and axons;
  NC.PropertyLine role (plan dash-dot, 04 site = property + 50 ft, section dashes); legend symbols
  (`Drawing/AnnotationSymbols`, style `annotation`) on plan / site legends + axon LEGEND artboard + north/scale;
  screen trace skips flat faces; 03 level line 0.5 pt, tree gap 1 pt; medians end 20 ft back; 04 exploded strokes
  per level; site axon range ×2; tree / people colours (`Style.ApplyColors`); fixed artboards fit plans / sections;
  `Localizer` ignores curves (Break Room section lines). Context style in plans = axon (white, grey outline).
- Rev 17 (v0.8.3, 198 tests, `Rev083Tests` + `ScreenModel` = Downtown with a roof screen, `PD_DUMP_SCREEN`):
  streetscape `Plans/SiteLayout` (sidewalk 4.5 ft, street trees 1.5 ft off the kerb, medians on roads > 50 ft, style
  `site`) in plans / axons / sections; site plan roof artboard; people only on walkable hardscape
  (`PlanBackground.Walkable`); screens solid (`Analysis/ScreenEnclosures`, style `screen`) in axons / plans / sections;
  exploded pit walls with B1; floor key axons = cutaway (`axon.levels`); site axon fills its page (`axon.site.fillPage`);
  axon view corner / V marker (`Output.AxonView`, Rhino `ProgramDiagramPlaceAxonView`); settings side file + export copy.
- Rev 16 (v0.8.2): site curves, trees in greens, hatches. Rev 14–15 (v0.8.0–1): user rev 0.8 + S6.
- Next: next Windows round — opening message, feature map, tuning keys, review workflow and open questions in
  **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_ONLY) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16'. Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.8.4.zip` over the old folder (whole folder), restart Rhino.
2. Tab 1: property line layer → NC.PropertyLine (plans dash-dot, 04 site edge, sections dashed).
3. Uncovered slabs / flat glass show as roof / glass hatch in plans and axons; screen has no diagonal wall.
4. Legends: 01_Plans LEGEND artboard symbol column, 00_Site_Plan LEGEND & SYMBOLS, axon `_LEGEND` + north/scale.
5. Tab 7 Colors: Trees / People colour (Default = style); 11x17 plans / sections fill the artboard.
6. Send WARN / ERROR lines from illustrator_log.txt and NOTE lines from export_log.txt.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1/64".
- Elevations draw no trees (sections do). Text widths are estimates.

# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 17 (v0.8.3, 198 tests, `Rev083Tests` + `ScreenModel` = Downtown with a roof screen, `PD_DUMP_SCREEN`):
  streetscape `Plans/SiteLayout` (sidewalk 4.5 ft, street trees 1.5 ft off the kerb, medians on roads > 50 ft, style
  `site`) in plans / axons / sections; site plan roof artboard; people only on walkable hardscape
  (`PlanBackground.Walkable`); screens solid (`Analysis/ScreenEnclosures`, style `screen`) in axons / plans / sections;
  exploded pit walls with B1; floor key axons = cutaway (`axon.levels`); site axon fills its page (`axon.site.fillPage`);
  axon view corner / V marker (`Output.AxonView`, Rhino `ProgramDiagramPlaceAxonView`); settings side file + export copy.
- Rev 16 (v0.8.2): site curves as areas / lines, trees in greens, green / water hatch, stacked 0.5 pt floor lines.
- Rev 15 (v0.8.1): site plan inside the AI canvas; 03 without basements; exploded basements under the site plate.
- Rev 14 (v0.8.0): user rev 0.8 + S6. Fixture `downtown.json` = Downtown 2026-10-13 (B1–L11). Map: HANDOFF §3.
- Next: next Windows round — opening message, feature map, tuning keys, review workflow and open questions in
  **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_ONLY) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16'. Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.8.3.zip` over the old folder (whole folder), restart Rhino.
2. Tab 1: LOT FILL → BG.Hardscape, CADMAPPER PARK → BG.Landscape, ROAD LINES → BG.Road (blocks / parks come from these).
3. Site plan: sidewalks, street trees, medians; 2nd artboard SITE PLAN ROOF. Site axon fills the page.
4. Roof screen layer → NC.Screen: axons show solid walls, white floor, open top; L11 plan floor; section dashed top.
5. Tab 8 "Axons seen from" + "Place view in plan…" (V marker); "Load settings…"; `<model>.programdiagram.json` beside the 3dm.
6. Send WARN / ERROR lines from illustrator_log.txt and NOTE lines from export_log.txt.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: street names / arrows, flow / view / service icons (draw on `ANNOTATION`), key plan, Unassigned hatch.
- Elevations draw no trees (sections do). Text widths are estimates.

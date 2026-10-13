# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 15 (v0.8.1, 174 tests): Windows feedback on v0.8.0 (`Rev081Tests`). Site plan was missing: artboard > Illustrator
  canvas (runtime.jsx throws) → `SiteExtent` counts only NC site objects within `plan.siteMarginFt`, capped by
  `page.maxArtboardPt`. 03 stacked = site axon treatment (`axon.stacked.fullHeightBlocks`, level lines, outer
  silhouette only); 03 shows no basements (`showBasements` false, `AxonDrawing.AboveGrade`). Exploded A / B: site at L1,
  basements dropped under the whole site plate (`DropBasements`, `axon.exploded.basementClearFt`), A digs the hole
  with excavation walls. User clarified: "site follows L1; B1 reads as underground".
- Rev 14 (v0.8.0): user rev 0.8 + S6 (fonts, NC roles, site plan extent, plan / section / axon / elevation details).
  Fixture `downtown.json` = Downtown 2026-10-13 (no roof slab, B1–L11). Feature map: `docs/HANDOFF.md` §3.
- Next: next Windows round — opening message, feature map, tuning keys, review workflow and open questions in
  **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_ONLY) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16'. Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.8.1.zip` over the old folder (whole folder), restart Rhino.
2. `00_Site_Plan.ai` is created again (illustrator_log.txt: no "larger than the Illustrator canvas").
3. `03_Axon_Stacked` / `03_Site_Axon`: no B1; floors touch (no slab gaps); 2 pt lines between floors, heavy line
   only on the outside.
4. `04_Axon_Exploded` (and B): site at L1; B1 hangs below the whole site plate, core connectors run through the hole.
5. Send WARN / ERROR lines from illustrator_log.txt and NOTE lines from export_log.txt.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: street names / arrows, flow / view / service icons (draw on `ANNOTATION`), key plan, Unassigned hatch.
- Elevations draw no trees (sections do). Text widths are estimates.

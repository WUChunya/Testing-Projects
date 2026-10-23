# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.
## Status
- CAMPUS 1022 (`deliverables/2026-10-23_CAMPUS_1022/`, README): names from the layout pages, cleanup, riverside park;
  new style keys `plan.labelLeaders` / `plan.labelNamedOnly` (off by default; on for that output, `CampusLabelTests`).
- Rev 34 (v0.10.0): axons draw roof gardens / terraces at height, climbing ground as slope bands, tall spaces coloured.
- Rev 33 (v0.9.9, `Rev099Tests`): axons put mezzanine blocks on their main level; canopies / top roofs kept, never over blocks.
- Rev 32 (v0.9.8): settings re-read from the model on EndOpenDocument (a stale start-up session exported defaults).
- Rev 30 (v0.9.6, `Rev096Tests`): our site above a block / slab shows from above (`SiteAbove`): roof view gets terraces,
  roof gardens and the slope garden; site plan yields only to climbing ground (`slopedSiteMinRiseFt`); background by height.
- Rev 27 (v0.9.3, `Rev093Tests`): axon style after the user's reference — exploded A = neutral slab + program volumes
  (`axon.exploded.volumeHeightFt` 8 ft), open space (`axon.openPrograms` CIRCULATION / INTERACTION) a flat layer on the floor
  in exploded A / B and floor keys; axon labels larger (`fonts.planeLabel*`, last resort `planeLabelMin`).
- RIVER models: `deliverables/2026-10-21_RIVER_VP/`, `2026-10-21_RIVER_DRAFT1_SITE/`, `2026-10-22_RIVER_1022/` (READMEs).
- Rev 26 (v0.9.2): fixed artboards centred on the property line, Fit content when larger; fixture tool `--full`.
- Rev 25 (v0.9.1): CW markup V2 (plan grid / overhead dashes, section site cuts); Rev 24 (v0.9.0): CW markup on the RIVER
  PDF (site names `plan.siteNameRoles`, modelled people, exploded B trees). Deliverables `2026-10-18/19_RIVER_MARKUP*`.
- Rev 23 (v0.8.9): `MeshSlicer.Factory` = GeometryOverlay.NG; Rev 22 (v0.8.8) resumable Illustrator run; Rev 14–15 user rev 0.8.
- Open (HANDOFF §7): "一楼的镂空" = the L1 plate hole (drawn since 0.8.6) or the open ground floor under the podium?
- Next: next Windows round — opening message, feature map, tuning keys, review workflow and open questions in
  **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_USER / PD_ONLY / PD_ARTBOARD) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16' (RIVER/CAMPUS docs: 1"=100' / 50', 11x17). Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.9.5.zip` over the old folder (whole folder), restart Rhino.
2. 04 exploded: each level a pale slab with program volumes on it; circulation / interaction flat on the slab, no sides.
3. 04 exploded B and floor keys: circulation / interaction flat on the floor; labels larger, white. Volume height and
   slab colour: `axon.exploded.volumeHeightFt` / `plateFill` (0 = old plates); open list `axon.openPrograms`.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1"=100' (fit content).
- Elevations draw no trees (sections do). Text widths are estimates.

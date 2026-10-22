# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.
## Status
- Rev 28 (v0.9.4, `Rev094Tests`): glass = envelope, the rest inside each main storey is synthetic CIRCULATION
  (`GlassEnvelope`); thin masses ≤ 2.5 ft on their floor, indoors never paving; INTERACTIVE alias; exploded volumes at block
  height; section slabs only inside the cut building, mezzanine slabs only under their blocks; plan names one size.
- Rev 27 (v0.9.3, `Rev093Tests`): axon style after the user's reference — exploded A = neutral slab + program volumes
  (`axon.exploded.volumeHeightFt` 8 ft), open space (`axon.openPrograms` CIRCULATION / INTERACTION) a flat layer on the floor
  in exploded A / B and floor keys; axon labels larger (`fonts.planeLabel*`, last resort `planeLabelMin`).
- RIVER 1021 (model only, plugin unchanged): VP comments → `deliverables/2026-10-21_RIVER_VP/` (README lists each comment).
  `scripts/build_r21.sh`: west bar + cores slid 24 ft east (passage), service road + parking gap, terraces, MEP, names.
- Rev 26 (v0.9.2, 241 tests, `Rev092Tests`, fixtures `river_tk.json`, `campus.json`): fixed artboards centre on the property
  line (`page.centreOn`) and a drawing too large for them gets Fit content at the same scale (`page.fitContentWhenLarger`);
  open-to-below claims its area first; level overlays keep polygons only (RIVER TK crash); area CSV total = program sum;
  fixture tool explodes blocks (`--full` keeps every mesh); JSX falls back to its own folder. Deliverable (RIVER dropped)
  `2026-10-19_CAMPUS/` (11x17, 1"=100', sections 1"=50').
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
1. Unzip `ProgramDiagram-0.9.4.zip` over the old folder (whole folder), restart Rhino.
2. 04 exploded: each level a pale slab with program volumes on it; circulation / interaction flat on the slab, no sides.
3. 04 exploded B and floor keys: circulation / interaction flat on the floor; labels larger, white. Volume height and
   slab colour: `axon.exploded.volumeHeightFt` / `plateFill` (0 = old plates); open list `axon.openPrograms`.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1"=100' (fit content).
- Elevations draw no trees (sections do). Text widths are estimates.

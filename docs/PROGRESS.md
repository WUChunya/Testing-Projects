# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 20 (v0.8.6, 220 tests, `Rev086Tests`): 03 axons draw every open slab at its level (`Area`, floor edges of
  recessed floors as in Rhino), blocks split under plates (`AxonDrawing.UnderPlates`), roof plates lose what a screen
  floor covers (`UnderScreens`); glass see-through (own pass, `FACADE` layer, 50 %: `exposed.glassOpacity`,
  `facade.stackedAxonOpacity`), facade glass walls in 03 axons (`FacadeGlass`, `axon.facadeVeil*`); slivers dropped in
  `AxonFaces.Render`; exploded basements under `AXON_CONNECTOR_BELOW` (`Sheet.UnderSite`); openings = holes of
  slabs + blocks (`SeenBelow.OpeningsAt`, the L1 plate hole).
- Rev 19 (v0.8.5): coordinates snapped to 1 mm (Windows overlay failures), `SeenBelow` plans, roof view tiers.
- Rev 18 (v0.8.4): exposed slabs / glass, NC.PropertyLine, legend symbols, tree / people colours, artboard fit.
- Rev 17 (v0.8.3): streetscape `Plans/SiteLayout`, roof-view site plan, solid screens (`ScreenEnclosures`), floor key cutaway.
- Rev 16 (v0.8.2): site curves, trees in greens, hatches. Rev 14–15 (v0.8.0–1): user rev 0.8 + S6.
- Next: next Windows round — opening message, feature map, tuning keys, review workflow and open questions in
  **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_ONLY) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16'. Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.8.6.zip` over the old folder (whole folder), restart Rhino.
2. 03 axons: floor plates round recessed floors (L4, L7) as in Rhino; podium glass walls 50 %; no overlaps.
3. 04 exploded: site over the B1 → L1 shafts, shafts over B1. Site plan / L1: the plate hole = OPEN TO BELOW.
4. Plans: canopy glass 50 %. export_log "CHECK axons: … failed" lines → send them.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1/64".
- Elevations draw no trees (sections do). Text widths are estimates.

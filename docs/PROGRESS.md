# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 22 (v0.8.8, 224 tests, `Rev087RuntimeTests`): Windows export (11x17) died twice with RPC_E_SERVERFAULT, no log.
  Runtime now resumable (`illustrator_progress.txt`: skips saved files and the one Illustrator died on, logs its last
  step); Rhino retries while each run gets further. Root cause still unknown → need the user's log (HANDOFF §7).
- Rev 21 (v0.8.7, 223 tests, `Rev087Tests`): hidden-face cycles broken inside the real cycle (`AxonFaces.CycleBreaker`),
  coplanar touching faces unordered — 03 now matches the model (`tools/axon_check/run.sh`: z-buffer render of the fixture
  vs our axon); thin masses re-sliced at mid-height (B1 circulation plate was lost); facade glass in 04 exploded (each
  storey on its plate) and floor keys, flat glass 50 % there (`facade.explodedAxon`); NTS assertion → overlay fallback.
- Rev 20 (v0.8.6): open slabs at their level, glass see-through 50 % (`FACADE`), exploded B1 under shafts, plate openings.
- Rev 19 (v0.8.5): coordinates snapped to 1 mm (Windows overlay failures), `SeenBelow` plans, roof view tiers.
- Rev 18 (v0.8.4): exposed slabs / glass, NC.PropertyLine, legend symbols, tree / people colours, artboard fit.
- Rev 16–17 (v0.8.2–3): site curves, greens, hatches, streetscape, roof-view site plan, solid screens. Rev 14–15: user rev 0.8 + S6.
- Open (asked the user, HANDOFF §7): "一楼的镂空" = the L1 plate hole (drawn since 0.8.6) or the open ground floor
  under the podium / atrium (would need an overhead dashed outline on the site plan)?
- Next: next Windows round — opening message, feature map, tuning keys, review workflow and open questions in
  **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_USER / PD_ONLY / PD_ARTBOARD) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16'. Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.8.8.zip` over the old folder (whole folder), restart Rhino.
2. 03 axons: L4 plate edge all round the recessed L4, L2 plate over the L1 lobby, no fragments by the podium.
3. 04 exploded: B1 complete (circulation corridor, no hole); glass walls 50 % on the plates; floor keys show the glass.
4. If Illustrator crashes again: send illustrator_log.txt / illustrator_progress.txt. Site plan: is the L1 opening (OPEN TO BELOW, small at 1/64") what "镂空" meant? Screenshot if not.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1/64".
- Elevations draw no trees (sections do). Text widths are estimates.

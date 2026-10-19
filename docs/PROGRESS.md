# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 25 (v0.9.1, 234 tests): CW markup V2. Plan grid dashes between same-name blocks (`plan.grid*`), dashed overhang of
  the floor above (`plan.overhead*`); section cuts raised NC.Hardscape/Green/BG.Road solids (`section.siteCut*`), backs and
  names massing cuts; site axon without NC.Massing; exploded A without tall links; storey glass ≥4 ft in its storey.
  Sections C/D, `deliverables/2026-10-19_RIVER_MARKUP_V2/`.
- Rev 24 (v0.9.0, 230 tests, `Rev089Tests` River_*, fixture `river.json` + `river_settings.json`): CW markup on the RIVER PDF.
  Exploded B hides site trees/people behind lifted plates, only trees on the site shape; section skips T.O. PENTHOUSE at a
  marked level; plan names our site areas (`plan.siteNameRoles`); LAWN/MEADOW greens unplanted; modelled people drawn.
  Model + 28-page PDF + scripts: `deliverables/2026-10-18_RIVER_MARKUP/`. Fixture tool meshes rhino3dm-written extrusions.
- Rev 23 (v0.8.9): `MeshSlicer.Factory` = GeometryOverlay.NG (RIVER "non-noded intersection"); `deliverables/2026-10-17_RIVER/`.
- Rev 22 (v0.8.8): resumable Illustrator run after RPC_E_SERVERFAULT (`illustrator_progress.txt`); root cause → HANDOFF §7.
- Rev 18–21 (v0.8.4–7): exposed slabs/glass, property line, 1 mm snap, see-through glass, hidden-face cycle breaker.
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
1. Unzip `ProgramDiagram-0.9.1.zip` over the old folder (whole folder), restart Rhino.
2. 03 axons: L4 plate edge all round the recessed L4, L2 plate over the L1 lobby, no fragments by the podium.
3. 04 exploded: B1 complete (circulation corridor, no hole); glass walls 50 % on the plates; floor keys show the glass.
4. If Illustrator crashes again: send illustrator_log.txt / illustrator_progress.txt. Site plan: is the L1 opening (OPEN TO BELOW, small at 1/64") what "镂空" meant? Screenshot if not.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1/64".
- Elevations draw no trees (sections do). Text widths are estimates.

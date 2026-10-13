# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 19 (v0.8.5, 216 tests, `Rev085Tests`, fixture = Downtown 2026-10-13 rev B with property line, settings NE view):
  local coordinates snapped to 1 mm (`cleanup.coordSnapFt`, raw Rhino doubles broke the overlays → blank screen inside,
  leftover faces, trees through walls on Windows); hidden faces trimmed only by drawn faces, snapped 3rd overlay
  fallback, failures → export_log CHECK; `ExposedSurface.Sky` (no higher block over it) for 03 axons, blocks stop
  under open roof slabs (`AxonDrawing.UnderRoofs`); plans draw lower floors as top views + openings to below
  (`Plans/SeenBelow`); roof view tiers include roof slabs; exploded shafts under the site on `AXON_CONNECTOR_BELOW`.
- Rev 18 (v0.8.4, 210 tests, `Rev084Tests`, user settings fixture `downtown_settings.json` + `PD_USER=1`):
  exposed slabs / flat glass as roof / canopy (`Analysis/ExposedSurfaces`, style `exposed`) in plans and axons;
  NC.PropertyLine role (plan dash-dot, 04 site = property + 50 ft, section dashes); legend symbols
  (`Drawing/AnnotationSymbols`, style `annotation`) on plan / site legends + axon LEGEND artboard + north/scale;
  screen trace skips flat faces; 03 level line 0.5 pt, tree gap 1 pt; medians end 20 ft back; 04 exploded strokes
  per level; site axon range ×2; tree / people colours (`Style.ApplyColors`); fixed artboards fit plans / sections;
  `Localizer` ignores curves (Break Room section lines). Context style in plans = axon (white, grey outline).
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
1. Unzip `ProgramDiagram-0.8.5.zip` over the old folder (whole folder), restart Rhino.
2. 03 axons: podium roof covers its program; roof screen open with white floor; no trees on the building.
3. Plans: L1 opening = OPEN TO BELOW; L4+ show the podium roof / glass / lower plates; SITE PLAN ROOF outline.
4. 04 exploded: shafts B1 → L1 behind the site plate.
5. export_log.txt: any "CHECK axons: … overlay(s) failed" line → send it (with the NTS version it names).

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: automatic flow / view icon placement (legend symbols only), key plan, Unassigned hatch. 04 exploded > 11x17 at 1/64".
- Elevations draw no trees (sections do). Text widths are estimates.

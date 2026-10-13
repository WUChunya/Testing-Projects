# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 14 (v0.8.0, 168 tests): user rev 0.8 + **S6 done**. Fixture `downtown.json` = Downtown 2026-10-13 (no roof slab, B1–L11).
  Fonts Futura PT caps (Heavy/Book, Arial fallback). Roles NC.Interior/Furniture/Massing/Screen (`PlanLinework`, section
  `MassingCut`, axon `ScreensAndMassing`; screens = `ViewKind.Facade`, never extruded). Site plan extent `plan.siteExtentFactor` 5.
  Plans: `BG_SITE_EDGE` (hardscape 1 pt / road 1.5 pt), `PlanLegendSheet` artboard, plain level tag, `cleanup.sealGapFt` 0.2
  (`LevelAssignment.Seal`, areas counted before). Sections: centred labels (`SectionBars.Fit`, 3 letters), `Penthouse` row
  grey + T.O. marker, random `AlongLine`. Axons: `AxonFaces.SplitAtNeighbours` (roof core fix), site axon `FullHeight` +
  `AXON_LEVEL_LINE`, exploded / levels site at L1 with excavation (`GroundAndExcavation`, `Excavation`), trees `HideBehind`.
  Elevations: same `LevelMarkers` as sections, dashed lines across the drawing. S6: `S6AcceptanceTests` (T5–T7; T7 runs
  runtime.jsx in node on a mock doc), `docs/USER_GUIDE.md`, `docs/MODELING_CONVENTION.md`, PRD v1.0.
- Next: Windows test-and-tune of v0.8.0 in a new session — opening message, feature map, tuning keys, review workflow and
  open questions in **`docs/HANDOFF.md`**. Review: `DumpForReview` (PD_DUMP / PD_ONLY) + `node tools/svg_shot.mjs`.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off.
- Penthouse = top row only SHELL/CORE/STAIR/BUILDING_SUPPORT (`section.penthousePrograms`). Exploded site follows L1 (ground).
- Defaults in `styles/` (rules, style incl. plan/section/axon.site/entourage/charts/roles). Scales 1" = 45' / sections 16'. Env: HANDOFF §5.
- Program keys immutable; AI update redraws named items, keeps `_MANUAL` / `OVERLAY` / `ANNOTATION` / unnamed.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.8.0.zip` over the old folder (whole folder), restart Rhino. Install Futura PT (Book, Heavy).
2. Tab 1: new roles NC.Screen / Interior / Furniture / Massing in the list; layers named SCREEN, INTERIOR, FURNITURE,
   NC::MASSING pre-fill. Export: plan lines grey, massing dashed, screen a line (no block) in plans and axons.
3. Every text Futura PT capitals (WARN in illustrator_log.txt if the font is missing → Arial).
4. Site plan ≈ 11× the site each way; plans: hardscape / road edges, LEGEND & SYMBOLS artboard, LEVEL X without fill.
5. Section: names centred, CON/LAB style short names, grey PENTHOUSE without roof slab, T.O. PENTHOUSE marker.
6. Site axon: no slab gaps, 2 pt black lines between floors (layer AXON_LEVEL_LINE), heavy line only outside.
7. Exploded / level axons: site at L1, B1 below in the dug-out hole (levels: dashed site + grey walls), roof core upright.
8. Elevation: level column and dashed lines like the section, across the whole drawing.
9. Symbols PD_PERSON_* / PD_TREE_* in the Symbols panel (VERIFY-WIN symbols.add / symbolItems.add + transform).
10. Re-export after editing a mass: `_MANUAL` drawings untouched; send any WARN / ERROR lines.

## Known issues
- Plan people = built-in top-view symbol (office library has side views only).
- Not yet: street names / arrows, flow / view / service icons (draw on `ANNOTATION`), key plan, Unassigned hatch.
- Elevations draw no trees (sections do). Text widths are estimates. Open: "site context follows B1" read as site at L1.

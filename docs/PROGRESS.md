# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 10 (v0.5.0, 89 tests): styles unified to `docs/SITE_STYLE.md` / `docs/AXON_STYLE.md` (now merged into `style_default.json`).
  Fonts: all Arial (`fonts.*` incl. opacity / tracking / leading; `ParaStyle` carries them; `DItem.SizePt`, `GlyphAnchor`).
  Section §4: `ExportContext.ForSection()` (1" = 16', text scale 1), bars 0.68×floor, white slab strips, right labels + "01" numbers.
  Plan §2: roles paints, building shadows, 3 pt outline, white room lines, 19/13 pt names, title tag. Stacked §1: floor lines,
  folds (`AxonFaces.Folds`), 2.5 silhouette, trees. Exploded A (AXON_STYLE): pitch max(1.5×floor, 0.7×top depth), front-corner
  labels, connectors (CORE/STAIR + tall). Exploded B (`AxonKind.ExplodedB`, planometric): translucent blocks, atria, shafts, site plane.
- Roles: NC.Core/Stair (masses, program CORE/STAIR), NC.Slab/Facade (were BG.*, renamed on load), NC.Structure/Green/Hardscape/
  Tree/Water, BG.*; NC by path segment NC/NEW/PROJECT/PROPOSED (`layer_keywords.json` v2, `first` rules before program match).
- Next session: **S5 Charts + Entourage** — paste the "Next-session message" from `docs/DEV_PROMPT.md` (updated for v0.5.0).
  PRD v0.8; history `docs/CHANGELOG.md`; goldens: section_A, elevation_S, plan_L1, axon_stacked/exploded/exploded_B/levels_L3.
- Style sources: SITE_STYLE / AXON_STYLE merged into `style_default.json`; `site_style.json` / `axon_style.json` = reference-sheet inputs only.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade veil off by default (`axon.facadeVeil`).
- Defaults live in `styles/` (`rules_default.json` output/scales; `style_default.json` fonts/section/plan/axon/roles/view).
- Scales: plans/axons 1" = 45'-0" (new default), sections 1" = 16'-0" (tab 8 "Section scale"). Old saves keep their stored scale.
- Program keys immutable (`L03_KEY_01`, swatches `PRG_<KEY>`); AI update: named items redrawn, `_MANUAL`/`OVERLAY`/unnamed kept.
- Item names: `SEC_*`, `ELV_*`, `PLN_<lvl>_*`/`PLO_*`, `AXS_*`, `AXE_*`, `AXB_*`, `AXK_<lvl>_*`. Stale layers purged: `PD_*`, `VIEW_*`, `BG_*`, `AXON_*`, `FACADE`.
- Axon context = low white blocks (`axon.contextHeightFt` 8; 0 = real height but merged context meshes become huge boxes).
- Env: apt dotnet-sdk-8.0, pip rhino3dm, node 22 + global playwright (--disable-gpu; screenshot fullPage:false).

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.5.0.zip` over the old files, restart Rhino.
2. Tab 1: Role list shows NC.* and BG.*; CORE / STAIR / COLUMN / GREEN ROOF / NC::TREE layers pre-fill; old BG.Facade/BG.Slab show as NC.*.
3. Tab 8: set Scale 1" = 45'-0" (old saves keep 1/32"), Section scale 1" = 16'-0"; tick AxonExplodedB (new).
4. Fonts: every text in Arial (ArialMT / Arial-BoldMT); white axon labels at 70 % (B 90 %) opacity (VERIFY-WIN text opacity).
5. Section: labels right-aligned and floor numbers left in bars (VERIFY-WIN `PD.anchorOnGlyphs`), slabs white strips.
6. Exploded A: labels start/end at the front corner (VERIFY-WIN left/right justification in `PD.planeText`).
7. illustrator_log.txt: all OK; send any WARN lines.

## Known issues
- Not yet: street names/arrows, people, flow/view/service icons, plaza stipple / lawn hatch, key plan, Unassigned hatch.
- Context buildings modelled as one merged mesh cannot be extruded per building; NC.Green etc. only flat in axons (roof gardens not lifted).
- Section-closed volume ≈ mid area × height; layer-priority overlap rule not implemented.

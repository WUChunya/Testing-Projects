# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Rev 9 (v0.4.1, 72 tests): artboards laid out in rows centred on the canvas (`Drawing.LayoutArtboards`; plans .ai failed: off-canvas);
  runtime logs per-item WARN instead of failing the file. Axons rewritten on `AxonFaces` (prism faces, near→far trimming with
  OverlayNGRobust): stacked thin lines + plane labels + low white context; exploded plates spaced until clear, CORE connectors,
  tall spaces stretched; new `AxonLevels` (04_Axon_Levels.ai, plate + dashed ghost). Plan title = marker + "Level 1"; light labels on dark fills.
- Next session: **S5 Charts + Entourage** — entry prompt in `docs/DEV_PROMPT.md`. PRD v0.7; history `docs/CHANGELOG.md`.
- S4 (v0.4.0): `Plans/PlanDrawing` (01_Plans.ai: artboard per level + PLANS OVERVIEW; shared extent; legend, scale bar, north,
  background via `PlanBackground`, glass line, open-to-below void, `PlanLabels` MIC/long-side or leader); `Axons/AxonDrawing`
  (layers per exploded plate `AXON_L*`, plane text via `DItem.Matrix`). Goldens: plan_L1, axon_stacked, axon_exploded, axon_levels_L3.
- Axon style spec (proposal, not yet wired): `docs/AXON_STYLE.md` + `styles/axon_style.json` + sheet `docs/reference/axon_exploded_reference.svg`
  (`tools/axon_style_reference.py`). 1"=45' on ARCH D; diffs vs `style_default.json › axon` in AXON_STYLE §11 await user OK.
  Text now measured from the Atlanta axon (Myriad Pro, white 70%, x2.118 on ARCH D); legend has core/facade/transport/flow/annotation.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade: stacked 30% veil, exploded hidden.
- Defaults live in `styles/` (`rules_default.json` output/folder/Illustrator flags; `style_default.json` section/plan/axon/page/fonts).
- Program keys immutable (`L03_KEY_01`, swatches `PRG_<KEY>`); AI update: named items redrawn, `_MANUAL`/`OVERLAY`/unnamed kept.
- Item names: `SEC_*`, `ELV_*`, plans `PLN_<lvl>_<id>` / overview `PLO_<lvl>_*`, axons `AXS_<lvl>_<id>_TOP|SIDEnnn`, `AXE_<lvl>_*`.
- Env: apt dotnet-sdk-8.0, pip rhino3dm, node 22 + global playwright (--disable-gpu). Stale layers purged: `PD_*`, `VIEW_*`, `BG_*`, `AXON_*`, `FACADE`.
- Axon: isometric from SW or active viewport direction (tab 8). Style = Atlanta reference; context height 8' (`contextHeightFt`, 0 = real).
- Plan: all levels share one extent (building + 60'); model +Y = north; level title "Level {n}" for Ln names.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.4.1.zip` over the old files, restart Rhino; panel shows tabs 1–9 in English.
2. Tab 6: Read SEC:: layers / Pick section line. Tab 7: Read ELEV:: layers / Pick elevation line (VERIFY-WIN both pick commands).
3. Tab 8: tick Plan, Section, Elevation, AxonStacked, AxonExploded, AxonLevels (new; old saves must tick it).
4. Export with Illustrator closed: Illustrator runs, saves all .ai, then quits; no file left open (VERIFY-WIN Quit). With Illustrator open: it stays open.
5. 02_Section_*.ai: no grey view lines in the white gaps between colour blocks (layer PD_PROGRAM_BG).
6. 01_Plans.ai now exists (artboards in rows); illustrator_log.txt shows OK (any WARN lines: send them to me).
7. 04_Axon_Exploded.ai: labels lie on the plates and stay editable text (VERIFY-WIN `PD.planeText` matrix transform).
8. Tick "Axons: use the active viewport's view direction", set a Rhino parallel view, export → axon matches the viewport.

## Known issues
- Not yet: Unassigned hatch, street names/arrows, trees/people, key plan, stair hatch; axon context buildings, mullion lines, vertical connectors.
- Fixture has no CORE program, so exploded plates are not connected (log note). Street arrows/names not yet (no road layer).
- Section-closed volume ≈ mid area × height; layer-priority overlap rule not implemented.

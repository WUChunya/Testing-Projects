# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Next session: **S5 Charts + Entourage** — entry prompt in `docs/DEV_PROMPT.md`. PRD v0.7; history `docs/CHANGELOG.md`.
- S4 done (v0.4.0, 68 tests): `Plans/PlanDrawing` (01_Plans.ai: artboard per level + PLANS OVERVIEW; shared extent; legend, scale bar, north,
  background via `PlanBackground`, glass line, open-to-below void, `PlanLabels` MIC/long-side or leader); `Axons/AxonDrawing`
  (03 stacked: prisms from `ProgramBlocks`, faces trimmed near→far, sides `PRG_*_SIDE1/2`, silhouette, facade veil 30 %;
  04 exploded: plate layer per level `AXON_L*`, plane text via `DItem.Matrix`, level column). Goldens: plan_L1, axon_stacked, axon_exploded.
- Rev 8 (with S4): section `PD_PROGRAM_BG` white under colour blocks; English UI; tabs 6 Sections / 7 Elevations split
  (`ProgramDiagramPickElevation`); export folder = model folder/`YYYY MMDD_Conceptual Program Diagram_HHMMSS` (`OutputFolder`),
  Illustrator runs in background and quits if the plugin started it (`quitIllustratorWhenDone`).
- S3 (v0.2–0.3.2): Drawing → SvgWriter/JsxWriter/Exporter, runtime.jsx, Section, Elevation, ProjectedView.
- Env: apt dotnet-sdk-8.0, pip rhino3dm, node 22 + global playwright (launch args --disable-gpu; screenshot fullPage:false).

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels and ≤1.6× story → tall space; else split. Facade: stacked 30% veil, exploded hidden.
- Defaults live in `styles/` (`rules_default.json` output/folder/Illustrator flags; `style_default.json` section/plan/axon/page/fonts).
- Program keys immutable (`L03_KEY_01`, swatches `PRG_<KEY>`); AI update: named items redrawn, `_MANUAL`/`OVERLAY`/unnamed kept.
- Item names: `SEC_*`, `ELV_*`, plans `PLN_<lvl>_<id>` / overview `PLO_<lvl>_*`, axons `AXS_<lvl>_<id>_TOP|SIDEnnn`, `AXE_<lvl>_*`.
- Stale generated layers purged: `PD_*`, `VIEW_*`, `BG_*`, `AXON_*`, `FACADE`.
- Axon: isometric from SW (style axon azimuth/elevation) or active viewport direction (tab 8 checkbox). Exploded spacing 1.5 × story.
- Plan: all levels share one extent (building + 60'); model +Y = north; level title "Level {n}" for Ln names.

## Windows test checklist (for the user)
1. Unzip `ProgramDiagram-0.4.0.zip` over the old files, restart Rhino; panel shows tabs 1–9 in English.
2. Tab 6: Read SEC:: layers / Pick section line. Tab 7: Read ELEV:: layers / Pick elevation line (VERIFY-WIN both pick commands).
3. Tab 8: "Save in" empty → info shows `<model folder>\YYYY MMDD_Conceptual Program Diagram_HHMMSS`; tick Plan, Section, Elevation, AxonStacked, AxonExploded.
4. Export with Illustrator closed: Illustrator runs, saves all .ai, then quits; no file left open (VERIFY-WIN Quit). With Illustrator open: it stays open.
5. 02_Section_*.ai: no grey view lines in the white gaps between colour blocks (layer PD_PROGRAM_BG).
6. 01_Plans.ai: one artboard per level + PLANS OVERVIEW; legend/scale bar/north; labels rotated along long sides.
7. 04_Axon_Exploded.ai: labels lie on the plates and stay editable text (VERIFY-WIN `PD.planeText` matrix transform).
8. Tick "Axons: use the active viewport's view direction", set a Rhino parallel view, export → axon matches the viewport.

## Known issues
- Not yet: Unassigned hatch, street names/arrows, trees/people, key plan, stair hatch; axon context buildings, mullion lines, vertical connectors.
- Exploded labels under the next plate are hidden (expected); raise `axon.explodeFactor` for more space.
- Section-closed volume ≈ mid area × height; layer-priority overlap rule not implemented.

# Development prompt

## How to use
Open a new Claude Code session on this repo and paste the **Kickoff message** below. For each following session, paste the **Next-session message**. One session = one stage (S0…S6) to keep context small.

---

## Kickoff message (paste into the new session)

```
You are the lead developer of the "Program Diagram" Rhino 8 plugin. Spec: docs/PRD.md. Conventions: CLAUDE.md. Plan: docs/DEV_PROMPT.md. Handoff: docs/PROGRESS.md.
If docs/PRD.md is missing, run: git fetch origin claude/axon-diagram-workflow-sroy86 && git merge origin/claude/axon-diagram-workflow-sroy86
Do stage S0 from docs/DEV_PROMPT.md now. Follow the token rules in CLAUDE.md. Use PRD §9 defaults (listed in PROGRESS.md) instead of asking. Ask me only if blocked.
At the end: run tests, update docs/PROGRESS.md, commit, push, and reply in Chinese in ≤10 lines.
```

## Next-session message (current: after S6, v0.8.0 — Windows acceptance and fixes)

```
You are the lead developer of the "Program Diagram" Rhino 8 plugin (repo branch claude/axon-diagram-workflow-sroy86).
Read, in this order and only these: CLAUDE.md, docs/PROGRESS.md, docs/CHANGELOG.md, then the PRD sections you need
(grep -n '^#' docs/PRD.md, then sed -n). PRD is v1.0; S0-S6 are done (plugin v0.8.0, all tests green).
Style: styles/style_default.json (fonts = Futura PT caps; roles incl. NC.Interior / Furniture / Massing / Screen; plan,
section incl. penthouse, axon incl. axon.site / excavation, entourage, charts); read styles by key, never whole.
Fixtures: tests/fixtures/template.json and downtown.json (rev 2026-10-13, no roof slab). DowntownTests.DumpForReview
writes every drawing as SVG with PD_DUMP=<dir> (PD_ONLY=Plan,Section,... to limit).
Task: fix what the Windows test of v0.8.0 shows (results below), reusing what exists. Keep: true scale, stable item
names, _MANUAL / OVERLAY untouched, NC vs BG drawn differently, every default in styles/*.json.
Talk to me in Chinese; code/commits in English. At the end: run tests, update PROGRESS.md (<=40 lines) and
CHANGELOG.md, bump the version, run tools/package.sh, commit, push, send me the zip, reply in Chinese in <=10 lines.
[Paste Windows test results / screenshots here]
```

---

## Environment facts
- Dev container is Linux: no Rhino, no Illustrator. Install the .NET 8 SDK (`dotnet-install.sh --channel 8.0`); if the network blocks it, read the environment docs and tell the user. Target `net7.0` / `net7.0-windows`. Rhino 8 runs .NET 7+.
- NuGet: `RhinoCommon` 8.x and `Eto.Forms` (compile-only, `<ExcludeAssets>runtime</ExcludeAssets>`, `Private=false`), `NetTopologySuite`, `ClosedXML`, `xunit`.
- Python `rhino3dm` reads the test model. Every object in it has cached render meshes, so the fixture can export meshes.
- Verification on Linux = Core unit tests + SVG previews. Rhino and Illustrator behaviour is verified by the user on Windows, using the checklist you write in PROGRESS.md.

## Architecture (fixed; do not re-debate)
1. **Rhino adapter** (`PD.Rhino`):
   - Read layers and objects; explode block instances.
   - Apply 3D cleanup (CapPlanarHoles, single-surface detection).
   - Mesh every solid and hand DTOs to Core: `SourceObject{id, layerPath, role, programKey, name, mesh, volume, isClosed, bbox}`.
   - Panel tabs ①–⑧ (PRD §3), DisplayConduit previews, settings saved as JSON in DocumentUserText.
2. **Core** (`PD.Core`):
   - Units and localize; outlier removal.
   - Level clustering from three sources (PRD B1).
   - Mesh-plane slicer, producing closed polylines with gap closing.
   - Per-level and per-section 2D resolution: smaller volume wins, done with NTS Difference.
   - Span rule (keep tall space vs split per level).
   - Drawing model `Drawing{artboards, layers, items(path|text|symbol), swatches, paraStyles}`.
3. **Writers** (Core): `JsxWriter` (embeds the Drawing as JSON plus `illustrator/runtime.jsx`), `SvgWriter` (preview), `CsvWriter`.
4. **Illustrator launch** (`PD.Rhino`): COM `Illustrator.Application.DoJavaScriptFile`, with a manual fallback.

## Stages (each ends with: tests green, PROGRESS.md updated, commit and push)

| Stage | Read PRD sections | Build | Done when |
|---|---|---|---|
| **S0 Bootstrap** ✅ | §6, §7.1, §5.1 | Install SDK; create `PD.sln` with the 3 projects; `tools/fixture_from_3dm.py` writes `tests/fixtures/template.json`: per object layer, type, bbox, isClosed, faceCount, and mesh (coordinates rounded to 1 mm, file < 5 MB, otherwise drop the CONTEXT meshes and keep bboxes); `styles/program_legend.json`, `styles/style_default.json`, `styles/layer_keywords.json` from PRD §5.1 and A1 | `dotnet build` passes; a fixture smoke test passes |
| **S1 Core geometry** ✅ | B1, §3.5, §7.2–7.3 | Units and localize; outliers; mesh slicer; level clustering; span rule; 2D overlap resolution; area stats | Tests for T3 (levels match §7.2 within 0.5 ft, outlier elevations flagged) and T4 (areas add up after resolution) pass on the fixture |
| **S2 Rhino plugin shell** ✅ | §1, A1–A3, §3 | `.rhp` project; `ProgramDiagram` command; Eto dockable panel with tabs ①–⑧ (functional, plain UI); layer role auto-fill (T1); facade detection; 3D cleanup (T2); level-plane conduit; settings persistence; Yak manifest | Builds on Linux; Windows checklist written for: install, open the test model, walk all tabs |
| **S3 Output engine + Section** ✅ (+ Elevation, v0.3.x) | §5.0, §5.1, §5.3, F8 notes | `runtime.jsx` (layers, global swatches, para styles, named items, in-place update, `_MANUAL` protection); `SvgWriter`; COM launcher; Section generator (cut bars, slab poche, beyond dashed, ground, property line, height dims, facade) | A golden SVG of Section A from the fixture looks right (one Playwright screenshot, viewed once); JSX passes a syntax check (`node --check` after stripping `#target`) |
| **S4 Plan + Axon** ✅ | §5.2, §5.4, A3 | Plan per level plus overview sheet, labels (NTS MaximumInscribedCircle), legend, scale bar; stacked and exploded axon (affine projection, painter's sort, facade veil, level leaders) | Golden SVGs for L1, Axon Stacked, Axon Exploded |
| **S5 Charts + Entourage** ✅ (+ site plan / site axon, v0.7.0) | §5.5, §5.6, §4 | Excel template and reader (ClosedXML); program diagram with shelf packing; donut pie; CSV; entourage (seeded Poisson disk, density by program, `ENTOURAGE` layer, symbols from `entourage.ai` with built-in placeholders) | Golden SVGs; same seed gives the same layout |
| **S6 Acceptance + docs** ✅ | §7.3, §8 | Run T1–T7 (Linux part); write `docs/USER_GUIDE.md` (Chinese, short) and `docs/MODELING_CONVENTION.md` (one page); finalize the Windows checklist | All Linux-checkable criteria pass; the user signs off the Windows checks |

## Token-saving rules (in addition to CLAUDE.md)
- Before coding a stage, read only that stage's PRD sections and PROGRESS.md. Do not re-read earlier stages' code unless an interface change needs it; use `grep -n` to find signatures.
- Prefer small focused files (< 300 lines). Create files with one Write; change them with Edit.
- Visual checks: at most one screenshot per golden image, and only at the end of a stage.
- If a RhinoCommon or Illustrator API is uncertain, write it, mark it `// VERIFY-WIN`, and list it in the Windows checklist instead of researching at length.
- Do not explain the code in chat. Put decisions in PROGRESS.md as one line each.

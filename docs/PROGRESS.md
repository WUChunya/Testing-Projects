# Progress / Handoff

Update at the end of every session: keep ≤40 lines total, newest first. Replace stale lines instead of appending forever.

## Status
- Done: axon style spec `docs/AXON_STYLE.md` + `styles/axon_style.json` + reference sheet `docs/reference/axon_exploded_reference.svg` (`tools/axon_style_reference.py`).
- Next session: **S0 Bootstrap** (see `docs/DEV_PROMPT.md`); keep `styles/axon_style.json` as the axon defaults.

## Decisions (defaults until the user says otherwise — PRD §9)
- Overlap: smaller volume wins. Span: 2 levels → keep as tall space; ≥3 → split per level.
- Single surfaces on program layers: flagged, user chooses (ignore / facade paint / extrude).
- Facade: stacked axon = 30% translucent veil; exploded axon = hidden.
- Axon: 1"=45' on ARCH D 36x24, iso 30/30 unforeshortened; exploded pitch = max(1.5*floor h, 0.7*top-face depth) (proposed, AXON_STYLE §10).
- Label font: Century Gothic (configurable). Platform: Windows + Rhino 8 only.

## Windows test checklist (for the user)
- (none yet)

## Known issues
- (none yet)

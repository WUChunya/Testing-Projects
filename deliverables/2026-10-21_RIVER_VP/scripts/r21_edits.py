"""RIVER 2026-10-21 VP comments → model edits (run after r20_clean.py, before r20_interior.py).

Usage: python3 -I r21_edits.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
Nothing is deleted: replaced objects move to OUTSIDE BUILDING (LEFTOVERS).
Building
- West volume (all floors, with its two cores, glass, slabs, roof mechanical) slides 24 ft east instead of giving its
  west bay to the passage (user override of "ACCESS TO RIVER WALK"); the freed 24 ft strip is open from grade to sky.
  Where the slid bar now overlaps the diagonal wing, the diagonal wing's pieces give way.
- Terraces (PROGRAM::Terrace → GREEN_ROOF): L2 north, L3 north-west and over the auditorium, L4 round the executive
  office; green roof on the parking structure.
- One roof MEP enlarged (south-west end), the two "square" roof slabs off, auditorium seating, simple stair symbols,
  room names per the comments.
Site
- Service road (24 ft) at grade: street → north between loading and the garden slope → under-building gap → east strip.
  The parking structure stops short of the building (gap) and gets rounded corners.
- Larger loading court, secondary drop-off lay-by, larger drop-off turnaround at +20' with trees, gardens, ramp above
  marked (dashed) on L1, access walk to the river walk through the freed strip.
"""
import sys, math
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *
from r20_envelope import envelope

M = Model(sys.argv[1], sys.argv[2])
F = M.find
DX = 24.0
P_STRIP = box(20, 41, 45 + DX, 230)                # freed passage, grade to sky
W = box(30, 30, 256, 200).difference(Polygon([(30, -60), (160, -60), (160, 41), (146, 49.5), (30, 49.5)]))
ZROUND = box(30, 28, 305, 215)

# ------------------------------------------------------------------ 1. west volume slides east
BAR = ['f44c5e3e', 'd7cd610a', 'c845a383', 'aafe3315', '46c55168', 'c0d7f49a', '6f3d9d4b',     # L1 + cores
       '7b049ff8', '48d8d0fd', '5c1f3380', 'b912d790',                                          # L2
       'b98069fb', 'd5517183', 'ba7c6d5b',                                                      # L3
       '46097e83', '7bb75395', '0b2d6a85',                                                      # roof slab, bar plates
       'fa0544be', 'd8387456', '690dd2a9', '5720ab27', 'c165ad0f', '24f158c4', '65d5cf14']      # slab curves, glass marks
BAR = [F(p) for p in BAR]
mech_w = [o['id'] for o in M.fx if o['layer'] == 'PROGRAM::Support' and o['name'] == 'MECHANICAL'
          and o['bbox'][2] / FT > 51 and o['bbox'][3] / FT < 240]
assert len(mech_w) == 1
BAR += mech_w
fp = {}
for oid in BAR:
    o = M.byid[oid]
    if o.get('mesh'): fp[oid] = affinity.translate(footprint(o), DX)
    M.translate(oid, DX)
M.log.append(f"west volume: {len(BAR)} objects moved {DX:.0f} ft east (2 cores, roof mechanical, slabs)")


def zr(o):
    return o['bbox'][2] / FT, o['bbox'][5] / FT


# The diagonal wing gives way where the slid bar now sits (same storey).
trimmed = []
for o in M.fx:
    if not o['layer'].startswith('PROGRAM::') or o['layer'] in ('PROGRAM::Glass',) or o['id'] in BAR or not o.get('mesh'): continue
    if o['bbox'][3] / FT < 150 or o['bbox'][0] / FT > 340 or o['bbox'][1] / FT > 260: continue
    z0, z1 = zr(o)
    g = footprint(o)
    if g is None: continue
    over = unary_union([fp[b] for b in fp if M.byid[b]['layer'].startswith('PROGRAM::') and zr(M.byid[b])[0] < z1 - 1 and zr(M.byid[b])[1] > z0 + 1])
    if g.intersection(over).area > 2:
        M.replace_solid(o['id'], g.difference(over).buffer(-0.05, join_style=2).buffer(0.05, join_style=2))
        trimmed.append(o['id'][:8])
M.log.append(f"diagonal wing pieces trimmed by the slid bar: {', '.join(trimmed)}")

# ------------------------------------------------------------------ 2. envelope (glass) re-drawn per storey
STOREYS = [(0, 18, 0.0, 18.0, False), (20, 34, 19.9, 33.9, True), (36, 50, 36.0, 50.0, True)]
fx_old = M.fx
glass_new = 0
for z0, z1, g0, g1, upper in STOREYS:
    env, _, _ = envelope(fx_old, z0, z1)
    new = unary_union([env, affinity.translate(env.intersection(W), DX)]).difference(P_STRIP)
    # L1's south face is the loading edge (glass ends there): not rounded.
    zr_ = ZROUND if upper else unary_union([box(30, 52, 305, 215), box(30, 28, 80, 52)])
    opened = rounded(new, 9.0)
    new = unary_union([new.difference(zr_), opened.intersection(zr_)]).buffer(0)
    old, keep_lines = [], []
    for o in fx_old:
        if o['layer'] != 'PROGRAM::Glass' or not o.get('mesh') or o['id'] in BAR: continue
        b = [v / FT for v in o['bbox']]
        if b[5] - b[2] < 1 or b[5] < z0 + 0.5 or b[2] > z1 - 0.5: continue
        segs = unary_union([LineString(t[:, :2]) if Polygon(t[:, :2]).area < 0.5 else Polygon(t[:, :2]).boundary for t in triangles(o)])
        if not segs.intersects(ZROUND): continue
        old.append(o['id']); keep_lines.append(segs)
    oldl = unary_union(keep_lines)
    allowed = unary_union([oldl.buffer(2.5), affinity.translate(oldl.intersection(W), DX).buffer(2.5), box(40, 36, 75, 46), box(60, 160, 90, 192)]
                          + ([box(250, 28, 300, 100)] if upper else []))
    edge = unary_union(lines_of(new.buffer(-0.75, join_style=2))).intersection(allowed)
    edge = linemerge(lines_of(edge)) if lines_of(edge) else edge
    pieces = [l for l in lines_of(edge) if l.length > 3]
    # Free-standing glass (one side open, e.g. at the loading court) is not on the envelope edge: kept where it was,
    # unless the passage or the slid bar now takes its place.
    free = oldl.difference(W).difference(P_STRIP).difference(new.buffer(-1.5)).difference(unary_union(pieces).buffer(2.0))
    freel = [l for l in lines_of(linemerge(lines_of(free)) if lines_of(free) else free) if l.length > 1.5]
    pieces += freel
    for oid in old: M.retire(oid)
    M.add_walls([l.simplify(0.05) for l in pieces], g0, g1, 'PROGRAM::Glass')
    glass_new += len(pieces)
    M.log.append(f"glass {z0}–{z1}': {len(old)} pieces → {len(pieces)} re-drawn on the slid envelope ({new.area:,.0f} sf)")

# ------------------------------------------------------------------ 3. slabs: passage cut, bar part slid
# "DELETE SQUARE LINES" (L4): the two roof slabs drawn as squares round the roof mechanicals go.
SQUARES = [F('66cc4cc4'), F('f47c7e68')]
for oid in SQUARES: M.retire(oid)
M.log.append("roof slabs 66cc4cc4, f47c7e68 (the L4 'square lines') → LEFTOVERS")
for o in fx_old:
    if o['layer'] != 'FLOOR PLATE' or o['id'] in BAR + SQUARES or not o.get('mesh'): continue
    b = [v / FT for v in o['bbox']]
    if b[0] > 70 or b[1] > 200 or b[4] < 41: continue
    g = footprint(o)
    if g is None or not g.intersects(P_STRIP): continue
    new = unary_union([g, affinity.translate(g.intersection(W), DX)]).difference(P_STRIP)
    new = unary_union([new.difference(ZROUND), rounded(new, 9.0).intersection(ZROUND)]).buffer(0)
    M.replace_solid(o['id'], new)
    M.log.append(f"slab {o['id'][:8]} ({b[2]:.0f}'): passage strip cut, bar part slid")

exec(open(__file__.rsplit("/", 1)[0] + "/r21_site.py").read())
M.write(sys.argv[3])
print("\n".join(M.log))

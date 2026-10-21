"""RIVER 1021 edits that need the glass envelope (run after r20_interior.py): terraces, green roof, larger MEP,
security at the main entry, room names per the VP comments.

Usage: python3 -I r21_more.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
"""
import sys, math
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *

M = Model(sys.argv[1], sys.argv[2])
FP = {o['id']: footprint(o) for o in M.fx if o.get('mesh') and o['layer'].startswith('PROGRAM::')}
Z = {o['id']: (o['bbox'][2] / FT, o['bbox'][5] / FT) for o in M.fx}
env = {round(Z[i][0]): FP[i] for i in FP if M.byid[i]['layer'] == 'PROGRAM::Circulation'}
rooms = lambda z: unary_union([FP[i] for i in FP if Z[i][0] <= z <= Z[i][1] and FP[i] is not None
                               and M.byid[i]['layer'] not in ('PROGRAM::Circulation', 'PROGRAM::Glass')])

def at(x, y, z, layer=None):
    hits = [i for i in FP if FP[i] is not None and Z[i][0] <= z <= Z[i][1] and FP[i].contains(Point(x, y))
            and M.byid[i]['layer'] not in ('PROGRAM::Circulation', 'PROGRAM::Glass') and (layer is None or M.byid[i]['layer'] == layer)]
    assert len(hits) == 1, (x, y, z, [(h[:8], M.byid[h]['layer']) for h in hits])
    return hits[0]

# ------------------------------------------------------------------ terraces (GREEN_ROOF: "TERRACE" is one of its names)
TER = 'PROGRAM::Terrace'
M.role(TER, 'Program', 'GREEN_ROOF', '')
aud = FP[M.find('54da442f')]
mech = [i for i in FP if M.byid[i]['name'] == 'MECHANICAL' and Z[i][0] > 51 and M.byid[i]['bbox'][0] / FT > 240]
assert len(mech) == 1
l4 = unary_union([FP[i] for i in FP if Z[i][0] > 51 and M.byid[i]['layer'] != TER])
clean = lambda g: unary_union([p for p in parts(rounded(g.buffer(-1, join_style=2), 2)) if p.area > 150])
terraces = [
    ('L2', 20.0, clean((env[0].difference(env[20])).intersection(box(225, 295, 335, 375)))),
    ('L3 north-west', 36.0, clean((env[20].difference(env[36])).intersection(box(140, 165, 262, 262)))),
    ('L3 auditorium roof', 36.0, clean((env[20].difference(env[36])).intersection(box(440, 425, 660, 600)).difference(aud.buffer(4)))),
    ('L4', 52.0, clean(env[36].difference(l4.buffer(2)).intersection(box(360, 300, 546, 432)))),
]
for name, z, g in terraces:
    M.add_solid(g, z, z + 1.0, TER, 'TERRACE')
    M.log.append(f"terrace {name}: {g.area:,.0f} sf at {z:.0f}'")
# Green roof on the parking structure (its top deck is at +50'; drawn with L4).
lot = rounded(box(509, -45, 635, 245), 15)
green = rounded(lot.buffer(-6), 8)
M.add_solid(green, 52.0, 53.0, TER, 'GREEN ROOF')
M.log.append(f"green roof on the parking structure: {green.area:,.0f} sf")

# ------------------------------------------------------------------ one roof MEP larger (south-west end)
mid = mech[0]
ext = Polygon([(308, 296), (235, 199), (238, 161), (257, 149), (297, 146), (385, 252)])
big = rounded(unary_union([FP[mid], ext]), 6)
M.replace_solid(mid, big)
slab = M.find('a178177a')
M.replace_solid(slab, big.buffer(1.0, join_style=1))
M.log.append(f"diagonal roof MEP {FP[mid].area:,.0f} → {big.area:,.0f} sf (south-west end extended), its roof slab follows")

# ------------------------------------------------------------------ security at the main entry (L2)
free = env[20].difference(rooms(25).buffer(3))
for cx, cy in [(478, 262), (490, 270), (500, 260), (470, 280), (520, 262)]:
    g = box(cx - 12, cy - 8, cx + 12, cy + 8)
    if free.contains(g):
        M.add_solid(g, 20.0, 34.0, 'PROGRAM::Support', 'SECURITY')
        M.log.append(f"security room 24 x 16 ft by the main entry at ({cx}, {cy})")
        break
else:
    M.log.append("security: no free spot by the main entry (skipped)")

# ------------------------------------------------------------------ names
BY_ID = [('bc7cf7cb', 'CORE LAB'), ('44e0241d', 'CORE LAB'), ('ac413b40', 'CORE LAB'), ('d1ac6ca0', 'SERVICE'),       # L1
         ('5fdd6984', 'PROTOTYPING / CORE LAB'), ('3d57ff08', 'PROTOTYPING / CORE LAB'), ('5c1b8d28', 'PROTOTYPING / CORE LAB'),
         ('b9302eeb', 'CORE LABS'), ('151b5509', 'INCUBATOR'), ('456274f9', 'OFFICE'), ('48d8d0fd', 'OFFICE'),          # L2
         ('7b049ff8', 'RESEARCH'),
         ('b98069fb', 'RESEARCH'), ('d5517183', 'OFFICE'), ('2ddc96f4', 'COMPUTATIONAL')]                               # L3
BY_POINT = [((252, 160, 25), 'COLLAB'), ((245, 190, 42), 'COLLAB')]                  # trimmed collab pieces (new ids)
for p, name in BY_ID:
    oid = M.find(p); M.st['names'][oid] = name
    M.log.append(f"name {p} ({M.byid[oid]['layer'].split('::')[-1]}, {Z[oid][0]:.0f}') → {name}")
for (x, y, z), name in BY_POINT:
    oid = at(x, y, z); M.st['names'][oid] = name
    M.log.append(f"name {oid[:8]} ({M.byid[oid]['layer'].split('::')[-1]}, {z}') → {name}")

M.write(sys.argv[3])
print("\n".join(M.log))

"""CAMPUS 1022 → program comments (COM_261021_Atrium_Scheme, 4 pages = L1 … L4).

Usage: python3 -I c23_comments.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
The comment pages are the model's own 1" = 100' layout pages: a point (x, y) in PDF points maps to model feet by
model = (x/72 - 7.029)·100 + 3134.3, ((792 - y)/72 - 3.659)·100 + 2100.5 (fitted on the floor outline, see c22_clean).
Removed / replaced objects go to OUTSIDE BUILDING (LEFTOVERS), as before.

- All floors ("if the core moves one bay, floor plate more efficient", red box one bay west of the west cores on
  L1 / L3 / L4): the eight west core shafts move one bay (24 ft) west; nothing else may overlap them. The offices
  next to them (blue boxes, L3 / L4, and L5 where the same core runs) and the L1 Combinable Classrooms (purple box)
  grow one bay west into the space the core left on the other side.
- L1 "cafe is open": the Coffee Kiosk becomes an open program (Interaction / Collab, no enclosure).
- L1 "open": the grand stair's ground flight moves one bay east (the pasted stair), the gallery side stays open.
- L1 "something cool": the east end of the south wing (cloud) becomes INNOVATION SHOWCASE (Interaction / Collab).
- L2 "flip": the curved grand stair rising from L2 is mirrored left ↔ right inside the atrium opening.
- L3 "wider": the atrium opening grows to the drawn outline; "deeper": the two rooms along the courtyard
  (Seminar Room, Break Room strips) become one bay deep (the drawn box).
- L4 "consider different shape for this floor": the atrium opening takes the drawn D shape; the strip along the
  courtyard's east side that the comment fills in circulation colour becomes indoor circulation between the wings.
- The two east stairs (red boxes on L2; user 10-23: they work as cores) become CORE shafts.
- Not changed (question): "additional column line ?" (L3).
"""
import sys, json, math
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *
from shapely import affinity

M = Model(sys.argv[1], sys.argv[2])
st = M.st
names = st.setdefault('names', {}); progs = st.setdefault('objectPrograms', {})
FP = {o['id']: footprint(o) for o in M.fx if o.get('mesh')}
Z = {o['id']: (o['bbox'][2] / FT, o['bbox'][5] / FT) for o in M.fx}
BAY = 24.0


def pg(x, y):
    """Comment-page point (PDF pt) → model feet."""
    return ((x / 72 - 7.029) * 100 + 3134.3, ((792 - y) / 72 - 3.659) * 100 + 2100.5)


def poly(pts): return Polygon([pg(*p) for p in pts]).buffer(0)


LEFT_PATH = "OUTSIDE BUILDING (LEFTOVERS)"
# Program solids by id: footprint, z range, layer, name — kept current as solids are replaced.
SOL = {}
for o in M.fx:
    if '#' in o['id'] or not o['layer'].startswith(('PROGRAM::', 'Shell Space')) or FP.get(o['id']) is None: continue
    if M.m.Layers[M.obj[o['id']].Attributes.LayerIndex].FullPath == LEFT_PATH: continue
    a, b = Z[o['id']]
    if b - a >= 0.5: SOL[o['id']] = dict(g=FP[o['id']], z0=a, z1=b, layer=o['layer'], name=o['name'])


def obj_of(oid):
    if oid not in M.obj: M.obj.update({str(x.Attributes.Id): x for x in M.m.Objects})
    return M.obj[oid]


def masses(z0, z1):
    """Program solids standing on the storey z0 … z1."""
    return [k for k, v in SOL.items() if v['z0'] < z1 - 0.5 and v['z1'] > z0 + 0.5]


def reshape(oid, g, why):
    v = SOL.pop(oid)
    pieces = [q for q in parts(g) if q.area >= 4]
    ids = M.add_solid(g, v['z0'], v['z1'], v['layer'], v['name'])
    for n, q in zip(ids, pieces):
        M.remap(oid, n)
        SOL[n] = dict(v, g=q)
    obj_of(oid).Attributes.LayerIndex = M.LEFT
    M.log.append(f"{why}: {oid[:8]} {v['layer'].split('::')[-1]} {v['name']} → {len(ids)} solid(s), {g.area:,.0f} sf")
    return ids


# ------------------------------------------------------------------ cores one bay west (all floors)
CORES = ['8c3fad87', 'a3b1c8cf', 'bbc4ff4c', '458f3347', '4638778a', 'b8e2d336', '9ae8aac2', 'b443e091']
core_ids = [M.find(c) for c in CORES]
for cid in core_ids: M.translate(cid, -BAY)
CORE_NEW = unary_union([affinity.translate(FP[c], -BAY, 0) for c in core_ids])
M.log.append(f"cores: {len(core_ids)} west shafts moved {BAY:.0f} ft west (now x {CORE_NEW.bounds[0]:.0f}–{CORE_NEW.bounds[2]:.0f})")

# The two east stairs (user 10-23: they work as cores): modelled as stair boxes on FLOOR PLATE (2), one per storey;
# now one CORE solid each over the storeys they serve (NE L2–L5, SE L2–L4); the boxes go to LEFTOVERS.
EAST = {'NE': ['a3788dd0', 'fa6ae92f', '2e3aff05'], 'SE': ['82306a5e', '66c28b9f']}
TOP = {74.3: 92.5, 58.3: 72.5}                       # top of the last stair box (slab) → ceiling of that storey
east = []
for k, shorts in EAST.items():
    ids = [M.find(x) for x in shorts]
    bb = [[v / FT for v in M.byid[i]['bbox']] for i in ids]
    fp_ = box(min(b[0] for b in bb), min(b[1] for b in bb), max(b[3] for b in bb), max(b[4] for b in bb))
    z1 = TOP[min(TOP, key=lambda t: abs(t - max(b[5] for b in bb)))]
    M.add_solid(fp_, 21.5, z1, 'CORE', '')
    for i in ids: M.retire(i)
    east.append(fp_)
    M.log.append(f"east stair {k} → CORE x {fp_.bounds[0]:.0f}–{fp_.bounds[2]:.0f}, y {fp_.bounds[1]:.1f}–{fp_.bounds[3]:.1f}, z 21.5–{z1}")
CORE_EAST = unary_union(east)

# rooms growing one bay west into the bay the core left (their west 24 ft copied one bay further west)
GROW = {'2608b475': 'L3 office N', 'd1ca223c': 'L3 office S', 'b96cc094': 'L4 office N', '80d41122': 'L4 office S',
        'd6a7a668': 'L5 office N', 'b2a92464': 'L1 COMBINABLE CLASSROOMS'}
grown = {}
for short, why in GROW.items():
    oid = M.find(short); g = FP[oid]
    x0 = g.bounds[0]
    strip_ = g.intersection(box(x0, -1e5, x0 + BAY, 1e5))
    grown[oid] = unary_union([g, affinity.translate(strip_, -BAY, 0)]).buffer(0.01).buffer(-0.01)

# every program solid gives way to the cores (smaller volume would otherwise win over the tall shafts)
done = set()
for oid in masses(0, 120):
    g = grown.get(oid, SOL[oid]['g'])
    z0_ = SOL[oid]['z0']   # east cores from L2; the SE one stops at L4
    cut = CORE_NEW if SOL[oid]['z1'] < 21.0 else unary_union([CORE_NEW, CORE_EAST if z0_ < 72.4 else CORE_EAST.intersection(box(0, 2300, 1e5, 1e5))])
    if g.intersects(cut.buffer(-0.05)) or oid in grown:
        reshape(oid, g.difference(cut), 'core / grow'); done.add(oid)

# ------------------------------------------------------------------ L1
kiosk = M.find('31dc01b0'); progs[kiosk] = 'INTERACTION'
M.log.append("L1 COFFEE KIOSK → open (Interaction / Collab)")
# "something cool": east end of the south wing, the cloud (740 … 759, 446 … 522 pt) = x 3456 … 3480 on the grid
cloud = poly([(740, 446), (740, 522), (759, 522), (759, 446)])
cool = box(3456.0, max(cloud.bounds[1], 2110.3), 3480.0, min(cloud.bounds[3], 2212.3))
for nid in M.add_solid(cool, 1.5, 20.5, 'PROGRAM::INTERACTIVE', 'INNOVATION SHOWCASE'):
    names[nid] = 'INNOVATION SHOWCASE'; progs[nid] = 'INTERACTION'
M.log.append(f"L1 INNOVATION SHOWCASE {cool.area:,.0f} sf at the east end of the south wing")

# ground flight of the grand stair: one bay east (pasted stair ≈ 31 ft east of the old one; kept inside the L2 opening)
STAIRS = ('Grand Stairs - Copy', 'Grand Stairs - Copy 2')
moved = 0
for o in M.fx:
    if o['layer'] not in STAIRS or '#' in o['id']: continue
    b = [v / FT for v in o['bbox']]
    cx, cy = (b[0] + b[3]) / 2, (b[1] + b[4]) / 2
    if b[2] < 19.0 and b[0] >= 3195 and b[3] <= 3231 and 2300 < cy < 2370:      # the flight itself, not the atrium rail
        M.translate(o['id'], 28.0); moved += 1
M.log.append(f"L1 grand stair ground flight: {moved} pieces moved 28 ft east")

# ------------------------------------------------------------------ L2 "flip"
AX = 3225.5                                        # centre of the L2 atrium opening (x 3198 … 3253)
mirror = r.Transform(1.0)
mirror.M00 = -1.0; mirror.M03 = 2 * AX
flipped = 0
for o in M.fx:
    if o['layer'] not in STAIRS or '#' in o['id']: continue
    b = [v / FT for v in o['bbox']]
    spiral = b[0] >= 3215 and b[3] <= 3252 and b[1] >= 2225 and b[4] <= 2260          # the south spiral stays
    if 19.5 <= b[2] < 40.5 and b[0] > 3185 and b[3] < 3265 and b[1] > 2220 and b[4] < 2375 and not spiral:
        M.obj[o['id']].Geometry.Transform(mirror); flipped += 1
M.log.append(f"L2 curved grand stair mirrored about x = {AX}: {flipped} pieces")


# The two spiral stairs move fully inside the new openings, so the slab edges stay pure lines and arcs
# (user 10-23): the north one (L3 → L4) inside both the L3 and the L4 opening, the south one (L2 → L3) inside the
# L2 and the L3 opening.
for nm, (bx0, by0, bx1, by1, zlo), (dx, dy) in [('north spiral', (3215, 2330, 3255, 2370, 30.0), (-5.0, -19.0)),
                                                ('south spiral', (3215, 2225, 3252, 2260, 14.0), (-7.0, 19.4))]:
    n = 0
    for o in M.fx:
        if o['layer'] not in STAIRS or '#' in o['id']: continue
        b = [v / FT for v in o['bbox']]
        if b[0] >= bx0 and b[3] <= bx1 and b[1] >= by0 and b[4] <= by1 and b[2] >= zlo and b[5] <= 70:
            M.translate(o['id'], dx, dy); n += 1
    M.log.append(f"{nm}: {n} pieces moved ({dx:+.1f}, {dy:+.1f}) ft")

# ------------------------------------------------------------------ slab openings
def outline(path, n=48):
    """Closed outline of straight lines ('L', p0, p1) and true circle arcs ('A', p0, through, p1) → (polygon, the
    same as a Rhino curve builder). User 10-23: real openings are straight lines and true arcs, whatever the
    markup's freehand line."""
    pts = []
    for seg in path:
        if seg[0] == 'L':
            pts += [seg[1]]
        else:
            (x1, y1), (x2, y2), (x3, y3) = seg[1], seg[2], seg[3]
            d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2))
            ux = ((x1 * x1 + y1 * y1) * (y2 - y3) + (x2 * x2 + y2 * y2) * (y3 - y1) + (x3 * x3 + y3 * y3) * (y1 - y2)) / d
            uy = ((x1 * x1 + y1 * y1) * (x3 - x2) + (x2 * x2 + y2 * y2) * (x1 - x3) + (x3 * x3 + y3 * y3) * (x2 - x1)) / d
            R = math.dist((ux, uy), seg[1])
            t1, t2, t3 = (math.atan2(q[1] - uy, q[0] - ux) for q in seg[1:])
            sweep = (t3 - t1) % (2 * math.pi)
            go = 1 if (t2 - t1) % (2 * math.pi) < sweep else -1
            if go < 0: sweep = 2 * math.pi - sweep
            pts += [(ux + R * math.cos(t1 + go * sweep * k / n), uy + R * math.sin(t1 + go * sweep * k / n)) for k in range(n)]
    return Polygon(pts).buffer(0)


def reopen(plate_short, z0, z1, path, why):
    """Slab opening = the outline `path`; the plate is rebuilt as one extrusion whose atrium profile is a Rhino
    polycurve of the same lines and true arcs; rooms of the storey are cut by the opening."""
    hole = outline(path)
    pid = M.find(plate_short); g = FP[pid]
    p = max(parts(g), key=lambda q: q.area)
    holes = [Polygon(h) for h in p.interiors]
    atrium = [h for h in holes if h.intersects(hole)]
    rest = [h for h in holes if not h.intersects(hole)]
    bb = [v / FT for v in M.byid[pid]['bbox']]
    ext = M.extrusion(Polygon(p.exterior, [list(h.exterior.coords) for h in rest]), bb[2], bb[5])
    pln = ext.GetProfilePlane(0.0); o, ax, ay = pln.Origin, pln.XAxis, pln.YAxis
    def loc(q):
        dx, dy, dz = q[0] - o.X, q[1] - o.Y, bb[2] - o.Z
        return r.Point3d(dx * ax.X + dy * ax.Y + dz * ax.Z, dx * ay.X + dy * ay.Y + dz * ay.Z, 0)
    pc = r.PolyCurve()
    for k, seg in enumerate(path):
        if seg[0] == 'L':
            nxt = path[(k + 1) % len(path)][1]
            pc.Append(r.Line(loc(seg[1]), loc(nxt)))
        else:
            pc.Append(r.Arc(loc(seg[1]), loc(seg[2]), loc(seg[3])))
    assert pc.IsClosed and ext.AddInnerProfile(pc), why
    nid = str(M.m.Objects.AddExtrusion(ext, M.attrs(M.byid[pid]['layer'], M.byid[pid]['name'])))
    M.remap(pid, nid); M.retire(pid)
    old = unary_union(atrium)
    desc = "; ".join(f"line {s_[1]}→" if s_[0] == 'L' else f"arc {s_[1]}→{s_[3]} through {s_[2]}" for s_ in path)
    M.log.append(f"{why}: {desc}; {hole.area:,.0f} sf (was {old.area:,.0f}, {old.difference(hole).area:,.0f} sf of it outside)")
    # Rooms of the storey: the old opening filled first (no remnant of its edge beside the new one), then cut.
    for oid in masses(z0, z1):
        g2 = SOL[oid]['g']
        filled = unary_union([Polygon(q.exterior, [h for h in q.interiors if not Polygon(h).intersects(old.buffer(0.5))]) for q in parts(g2)])
        if g2.intersection(hole).area > 1 or filled.area - g2.area > 1:
            reshape(oid, filled.difference(hole), f"{why} cut")
    return hole


def quarter(c, p0, p1):
    """Quarter arc about centre c from p0 to p1 (its midpoint for the 3-point arc)."""
    a0, a1 = math.atan2(p0[1] - c[1], p0[0] - c[0]), math.atan2(p1[1] - c[1], p1[0] - c[0])
    da = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
    R = math.dist(c, p0); m = a0 + da / 2
    return ('A', p0, (c[0] + R * math.cos(m), c[1] + R * math.sin(m)), p1)


# L3 "wider": the drawn outline = straight west edge (563 pt = x 3213) on the lower half, straight east edge on the
# old opening's (x 3259) on the upper half, joined by two quarter circles (R 46 ft = the width); the old opening lies inside.
XW, XE, W_ = 3213.0, 3259.0, 46.0
YB, YT = 2236.0, 2366.0
reopen('3e2b589b', 41.5, 56.5, [('L', (XW, YB), None), quarter((XE, YT - W_), (XW, YT - W_), (XE, YT)),
                                 ('L', (XE, YT), None), quarter((XW, YB + W_), (XE, YB + W_), (XW, YB))], 'L3 atrium wider')
# L3 "deeper": the courtyard-side rooms one bay deep (drawn box 603 … 620 pt = x 3269 … 3293)
for short in ('98c90e57', 'ba8e0bd2'):
    oid = M.find(short); g = SOL[oid]['g']
    reshape(oid, box(3269.0, g.bounds[1], 3293.0, g.bounds[3]), 'L3 deeper')
# L4 "consider different shape": the drawn D shape replaces the oval
# straight west edge on the drawn one (550 pt = x 3195, 329 … 430 pt), closed by one true arc out to its east tip (590 pt)
x0, x1 = pg(550, 0)[0], pg(590, 0)[0]
y0_, y1_ = round(pg(0, 430)[1]), min(round(pg(0, 329)[1]), 2372)
reopen('435582fa', 57.5, 72.5, [('A', (round(x0), y1_), (round(x1), (y0_ + y1_) / 2), (round(x0), y0_)), ('L', (round(x0), y0_), None)], 'L4 atrium D shape')
# L4 east strip between the wings (filled in circulation colour, 742 … 757 pt)
strip_ = box(3463.0, 2230.3, 3480.0, 2410.3)
others = unary_union([SOL[o]['g'] for o in masses(57.5, 72.5) if SOL[o]['layer'] != 'PROGRAM::Circulation'])
for nid in M.add_solid(strip_.difference(others).difference(CORE_EAST), 57.5, 72.5, 'PROGRAM::Circulation'):
    pass
M.log.append(f"L4 east link (circulation) {strip_.difference(others).area:,.0f} sf")

# ------------------------------------------------------------------ south roof shell 40 ft west (user 10-23)
# The south core moved one bay west and stood out of the shell space on the south-wing roof: the shell (and its
# roof slab) is lengthened 40 ft west, same round ends (R = half its width), so it encloses the core again. The
# high trellis over the rooftop terrace would run into it: its south end stops 1 ft clear of the shell.
def stadium(x0, x1, y0, y1, z):
    R = (y1 - y0) / 2; cy = (y0 + y1) / 2; P = lambda x, y: r.Point3d(x, y, z)
    pc = r.PolyCurve()
    pc.Append(r.Line(P(x0 + R, y0), P(x1 - R, y0))); pc.Append(r.Arc(P(x1 - R, y0), P(x1, cy), P(x1 - R, y1)))
    pc.Append(r.Line(P(x1 - R, y1), P(x0 + R, y1))); pc.Append(r.Arc(P(x0 + R, y1), P(x0, cy), P(x0 + R, y0)))
    return pc


def extrude(curve, z0, h, layer, name, old):
    e = r.Extrusion.Create(curve, h, True)
    if e.GetBoundingBox().Max.Z < z0 + 0.01: e = r.Extrusion.Create(curve, -h, True)
    nid = str(M.m.Objects.AddExtrusion(e, M.attrs(layer, name)))
    M.remap(old, nid); M.retire(old)
    return nid


EXT = 40.0
for short, layer in (('1f9a2a67', 'Shell Space'), ('58cfcb92', 'FLOOR PLATE (2)')):
    oid = M.find(short); b = [v / FT for v in M.byid[oid]['bbox']]
    extrude(stadium(b[0] - EXT, b[3], b[1], b[4], b[2]), b[2], b[5] - b[2], layer, M.byid[oid]['name'], oid)
    M.log.append(f"south roof shell {short}: x {b[0]:.0f}–{b[3]:.0f} → {b[0] - EXT:.0f}–{b[3]:.0f}, ends R {(b[4] - b[1]) / 2:.1f} ft, z {b[2]:.1f}–{b[5]:.1f}")
SHELL_N = (M.byid[M.find('1f9a2a67')]['bbox'][4] / FT)
tid = M.find('014ef532'); tb = [v / FT for v in M.byid[tid]['bbox']]
M.add_solid(box(tb[0], SHELL_N + 1.0, tb[3], tb[4]), tb[2], tb[5], 'TRELIS', 'TRELLIS'); M.retire(tid)
M.log.append(f"high trellis: south end y {tb[1]:.0f} → {SHELL_N + 1:.0f}")

M.write(sys.argv[3])
json.dump(st, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(M.log))

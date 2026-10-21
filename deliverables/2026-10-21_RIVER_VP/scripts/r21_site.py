"""Site part of the RIVER 1021 edits (exec'd by r21_edits.py with M, F, fx_old, P_STRIP in scope)."""
import inv as _inv
FX = {o['id']: o for o in _inv.load(sys.argv[1])}          # footprints of the input model (curves and solids)


def fpo(prefix):
    oid = F(prefix); o = M.byid[oid]
    g = footprint(o) if o.get('mesh') else FX[oid]['fp']
    return oid, g


def area_of(g):
    return Polygon(g.coords) if g.geom_type == 'LineString' else g


def recurve(prefix, g, name=None):
    """A closed site curve replaced by the curve(s) of a new area."""
    oid = F(prefix); o = M.byid[oid]
    M.add_area(g, o['layer'], o['name'] if name is None else name, o['bbox'][2] / FT)
    M.retire(oid)


SITE = 'PROJECT SITE'
M.role(f'{SITE}::SERVICE ROAD', 'BG.Road')
M.role(f'{SITE}::DROP OFF', 'BG.Road')
M.role(f'{SITE}::LOADING', 'NC.Hardscape')
M.role(f'{SITE}::WALKS', 'NC.Hardscape')
M.role(f'{SITE}::GARDEN', 'NC.Green')
M.role(f'{SITE}::TREES', 'NC.Tree')
M.role(f'{SITE}::LOADING BAYS', 'NC.Interior', mode='line')
M.role(f'{SITE}::RAMP ABOVE', 'NC.Massing', mode='line')
M.role('DETAIL::STAIR', 'NC.Interior', mode='line')
M.role('DETAIL::SEATING', 'NC.Furniture', mode='line')
M.role('DETAIL::TREADS', 'Ignore', mode='')           # "SIMPLER STAIR": the 3D treads give way to a stair symbol

# ------------------------------------------------------------------ parking structure: gap to the building, rounded
LOT_Y = 256.0
lot = rounded(box(509, -45, 635, LOT_Y), 15)
lot_up = rounded(box(509, -45, 635, 245), 15)
for p in ('22a7ea11', '64d817c9'):
    M.replace_solid(F(p), lot)
for p in ('e1206a6e', '55470d1c', '9c429ac6', 'a57061be', '0d5d8481', '2e60fa32'):
    M.replace_solid(F(p), lot_up)
M.log.append(f"parking structure: stops at y={LOT_Y:.0f}' (L1, L1M; upper decks 245'), 15 ft rounded corners")

# ------------------------------------------------------------------ service road
road_cl = LineString([(349, -80), (349, 120), (462, 268), (649, 268), (649, -80)])
ROAD = road_cl.buffer(12, join_style=1, cap_style=2).intersection(box(-1e3, -78, 1e3, 1e3))
ROAD = rounded(ROAD, 6).intersection(box(-1e3, -78, 1e3, 1e3))
M.add_solid(ROAD, -0.2, 0.0, f'{SITE}::SERVICE ROAD', 'SERVICE ROAD')
# The road runs open to the sky between the building and the parking: the podium parking, its mezzanine deck and the
# mezzanine slab give way (and keep clear of the building's north wing).
NORTH_GAP = box(440, 280, 700, 310)
for p in ('73aa614e', '984bf5cc'):
    oid, g = fpo(p)
    M.replace_solid(oid, g.difference(ROAD.buffer(1)).difference(NORTH_GAP).buffer(-0.05).buffer(0.05))
oid, g = fpo('d7dc4557')
M.replace_solid(oid, unary_union([g.intersection(box(-1e3, -1e3, 509, 1e3)), lot]).difference(ROAD.buffer(1)).difference(NORTH_GAP))
M.log.append("service road 24 ft: street → north at x 349' → diagonal → between building and parking (y 268') → east strip (x 649') → street; podium parking, L1M deck and slab cut back")

# ------------------------------------------------------------------ loading court (larger)
bld1 = unary_union([footprint(o) for o in fx_old if o['layer'].startswith('PROGRAM::') and o.get('mesh') and o['id'] not in BAR
                    and o['layer'] != 'PROGRAM::Glass' and o['bbox'][2] / FT < 1 and o['bbox'][5] / FT > 6]
                   + [affinity.translate(footprint(M.byid[b]), DX) for b in BAR if M.byid[b].get('mesh') and M.byid[b]['bbox'][2] / FT < 1])
_, podium = fpo('73aa614e')
LOAD = box(176, -30, 337, 94).difference(podium).difference(bld1.buffer(0.5)).difference(ROAD.buffer(0.5))
LOAD = rounded(max(parts(LOAD), key=lambda p: p.area), 4)
M.add_area(LOAD, f'{SITE}::LOADING', 'LOADING')
bays = []
for k in range(4):
    x0 = 200 + k * 14
    bays.append(LineString([(x0, 48), (x0, 6), (x0 + 12, 6), (x0 + 12, 48)]))
for b in bays: M.add_line(b, 0.0, f'{SITE}::LOADING BAYS')
M.log.append(f"loading court {LOAD.area:,.0f} sf (was the paving between the gym and the slope), 4 truck bays at the bar's dock")

# ------------------------------------------------------------------ drop-off turnaround at +20' (larger) + gardens
TC, TR, TI = (454.0, 168.0), 44.0, 20.0
ring = Point(TC).buffer(TR, 64).difference(Point(TC).buffer(TI, 64))
# Solid from grade to +20' (filled ground over the podium): seen from above it covers the parking roof below.
M.add_solid(ring, 0.0, 20.0, f'{SITE}::DROP OFF', 'DROP OFF')
M.add_solid(Point(TC).buffer(TI - 0.5, 64), 0.0, 20.6, f'{SITE}::GARDEN', 'DROP OFF GARDEN')
M.retire(F('eb87be3a'))
TURN = Point(TC).buffer(TR + 1, 64)
# Slope garden (the user's lid over the podium parking): clipped by the loading court, the road and the turnaround.
oid = F('862ba795')
M.clip_mesh(oid, box(-1e4, -1e4, 1e4, 1e4).difference(LOAD.buffer(1)).difference(ROAD.buffer(1)).difference(TURN), name='GARDEN')
for p in ('3decda41', 'd2df538c', 'c8d8fae3', '73ed582e'):
    M.clip_mesh(F(p), box(-1e4, -1e4, 1e4, 1e4).difference(TURN))
# East garden strip (north of the road, along the building) and the gap between parking and building.
_, mx3 = fpo('5a69310e')
mx3 = area_of(mx3)
GARDEN_E = unary_union([box(509, 280, 668, 303), box(635, 280, 668, 450)]).intersection(mx3.buffer(-1)).difference(bld1.buffer(1))
GARDEN_E = rounded(GARDEN_E, 3)
M.add_area(GARDEN_E, f'{SITE}::GARDEN', 'GARDEN')
M.log.append(f"drop-off turnaround at +20': ring {TR:.0f}/{TI:.0f} ft (was 18 ft island), planted island; slope garden named GARDEN; east garden strip {GARDEN_E.area:,.0f} sf")

# ------------------------------------------------------------------ secondary drop-off (street lay-by) + access walk
LAYBY = rounded(box(42, -68, 150, -56), 5).union(box(42, -70, 150, -66))
M.add_solid(LAYBY, -0.2, 0.0, f'{SITE}::DROP OFF', 'SECONDARY DROP OFF')
oid, g = fpo('a8557ac6')
recurve('a8557ac6', area_of(g).difference(LAYBY.buffer(0.5)), '')       # the forecourt (its old name was the loading)
DROP = box(42, -55.5, 118, -44).difference(bld1.buffer(1))
M.add_area(DROP, f'{SITE}::WALKS', 'SECONDARY DROP OFF')
for p in ('249104fb',):
    oid, g = fpo(p); M.replace_solid(oid, g.difference(LAYBY.buffer(2)))
oid, g = fpo('7cdbe32c'); recurve('7cdbe32c', area_of(g).difference(LAYBY.buffer(2)))
ACCESS = box(45, 41, 45 + DX, 200).difference(bld1.buffer(0.5))
M.add_area(ACCESS, f'{SITE}::WALKS', '')     # unnamed: a 24 ft strip cannot carry its name without crossing the building
M.log.append("secondary drop-off: street lay-by 108 x 12 ft; drop-off walk along it; access walk to the river walk in the freed strip")

# ------------------------------------------------------------------ ramp above (L1, dashed)
_, rampg = fpo('73ed582e')
ramp_above = rampg.intersection(podium.buffer(2)).union(rampg.intersection(box(-1e3, 60, 1e3, 1e3)))
for p in parts(ramp_above.buffer(0.5).buffer(-0.5)):
    M.add_line(LineString(p.exterior.coords), 0.0, f'{SITE}::RAMP ABOVE', 'RAMP ABOVE')
M.log.append("ramp above the podium parking marked dashed on L1")

# ------------------------------------------------------------------ trees (gardens, turnaround, east strip)
import random as _r
rng = _r.Random(1021)
trees = []
slope_garden = box(361, -60, 418, 150).difference(ROAD.buffer(8)).difference(TURN.buffer(6))
around = [Point(TC[0] + (TR + 9) * math.cos(a), TC[1] + (TR + 9) * math.sin(a)) for a in [math.radians(d) for d in range(0, 360, 30)]]
cands = around + [Point(rng.uniform(361, 418), rng.uniform(-60, 150)) for _ in range(300)] \
    + [Point(x, y) for x in (522, 547, 572, 597, 622) for y in (293,)] + [Point(650, y) for y in range(300, 446, 24)]
_, roadg = fpo('73ed582e')
for c in cands:
    ok_zone = slope_garden.contains(c) or GARDEN_E.buffer(-2).contains(c) or c in around
    if not ok_zone or c.distance(ROAD) < 9 or roadg.buffer(2).contains(c) or LOAD.buffer(6).contains(c) \
            or c.distance(Point(TC)) < TR + 5 or bld1.buffer(6).contains(c) or any(c.distance(q) < 17 for q in trees):
        continue
    trees.append(c)
for c in trees:
    M.m.Objects.AddCircle(r.Circle(r.Point3d(c.x, c.y, 0), rng.uniform(7, 9.5)), M.attrs(f'{SITE}::TREES'))
M.log.append(f"trees: {len(trees)} (slope garden, round the drop-off, east garden)")

# ------------------------------------------------------------------ stair symbols, auditorium seating
tread = unary_union([footprint(o) for o in fx_old if o['layer'] == 'DETAIL::TREADS' and o.get('mesh')])
hull = tread.convex_hull.buffer(0.5, join_style=2)
for z in (0.5, 20.5):
    for l in stair_symbol(hull, 2.0, (0, -1)): M.add_line(l, z, 'DETAIL::STAIR', 'GRAND STAIR')
d = (66 / math.hypot(66, 90), 90 / math.hypot(66, 90))
c0 = (252.0, 166.0)
rect = affinity.rotate(box(c0[0] - 12, c0[1] - 5, c0[0] + 12, c0[1] + 5), math.degrees(math.atan2(d[1], d[0])), origin=c0)
for z in (20.5, 36.5):
    for l in stair_symbol(rect, 1.8, d): M.add_line(l, z, 'DETAIL::STAIR', 'COLLAB STAIR')
_, aud = fpo('54da442f')
ac, ar = aud.centroid, math.sqrt(aud.area / math.pi)
stage_c = (ac.x, ac.y + ar * 0.55)
stage = Point(ac.x, ac.y).buffer(ar - 2, 64).intersection(box(ac.x - ar, stage_c[1], ac.x + ar, ac.y + ar))
seats = [LineString(stage.exterior.coords)]
inner = Point(ac.x, ac.y).buffer(ar - 3, 64)
for k, rad in enumerate(range(12, int(ar * 1.6), 3)):
    arc = LineString([(stage_c[0] + rad * math.cos(a), stage_c[1] + rad * math.sin(a))
                      for a in [math.radians(190 + i * 160 / 60) for i in range(61)]])
    aisle = unary_union([LineString([stage_c, (stage_c[0] + 200 * math.cos(math.radians(a)), stage_c[1] + 200 * math.sin(math.radians(a)))]).buffer(1.6)
                         for a in (238, 302)])
    seats += lines_of(arc.intersection(inner).difference(aisle))
for z in (0.5, 20.5):
    for l in seats:
        if l.length > 2: M.add_line(l, z, 'DETAIL::SEATING', 'AUDITORIUM SEATING')
M.log.append(f"simple stair symbols (grand stair L1/L2, collab stair L2/L3); auditorium seating rows {len(seats) - 1}")

"""Apply the CW markup (RIVER_Program_Diagrams_CW_MARKUP.pdf) to the RIVER model.

Usage: python3 revise_model.py <fixture.json of the source model> <in.3dm> <out.3dm>
Geometry is read from the fixture (feet, raw model coordinates) and written with rhino3dm; plugin settings stored in
the 3dm (ProgramDiagram\\settings) are updated alongside. Every change is listed in CHANGES and printed.
"""
import json, math, sys, uuid
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString, Point, box
from shapely.ops import unary_union
import inv

objs = {o['id']: o for o in inv.load(sys.argv[1])}
model = r.File3dm.Read(sys.argv[2])
KEY = "ProgramDiagram\\settings"
settings = json.loads(model.Strings[0][1])
CHANGES = []

def find(prefix):
    hits = [i for i in objs if i.startswith(prefix)]
    assert len(hits) == 1, (prefix, hits)
    return objs[hits[0]]

def rh_obj(full_id):
    for o in model.Objects:
        if str(o.Attributes.Id) == full_id: return o
    raise KeyError(full_id)

layer_index = {model.Layers[i].FullPath: i for i in range(len(model.Layers))}

def layer(path, color=(0, 0, 0)):
    if path in layer_index: return layer_index[path]
    parent_path, _, name = path.rpartition('::')
    l = r.Layer(); l.Name = name; l.Color = (*color, 255)
    if parent_path: l.ParentLayerId = model.Layers[layer(parent_path)].Id
    i = model.Layers.Add(l)
    layer_index[path] = i
    return i

def attrs(path, name=""):
    a = r.ObjectAttributes(); a.LayerIndex = layer(path)
    if name: a.Name = name
    return a

def ccw(coords):
    p = Polygon(coords)
    return list(p.exterior.coords) if p.exterior.is_ccw else list(p.exterior.coords)[::-1]

def polyline(coords, z):
    pl = r.Polyline(len(coords))
    for c in coords:
        pl.Add(c[0], c[1], z(c[0], c[1]) if callable(z) else z)
    return pl

def parts(g):
    if g is None or g.is_empty: return []
    if isinstance(g, Polygon): return [g]
    return [p for p in getattr(g, 'geoms', []) if isinstance(p, Polygon)]

def add_solid(poly, z0, z1, path, name="", note=""):
    """Closed extrusion of a polygon's outer ring from z0 to z1 (holes are not needed here)."""
    ids = []
    for p in parts(poly):
        if p.area < 20: continue
        crv = r.PolylineCurve(polyline(ccw(p.simplify(0.05).exterior.coords), z0))
        ext = r.Extrusion.Create(crv, z1 - z0, True)
        gid = model.Objects.AddExtrusion(ext, attrs(path, name))
        ids.append(str(gid))
        CHANGES.append(f"+ {path} {name or ''} {p.area:,.0f} sf, z {z0:g}-{z1:g} {note}".rstrip())
    return ids

def add_area(poly, z, path, name="", note=""):
    """Closed (optionally draped) boundary curves of a planar area; holes become separate loops."""
    for p in parts(poly):
        if p.area < 4: continue
        p = p.simplify(0.1)
        for ring in [q.exterior for q in inv.no_holes(p)]:
            model.Objects.AddCurve(r.PolylineCurve(polyline(list(ring.coords), z)), attrs(path, name))
        CHANGES.append(f"+ {path} {name or ''} {p.area:,.0f} sf {note}".rstrip())

def move(prefix, path, why):
    o = find(prefix); ro = rh_obj(o['id'])
    ro.Attributes.LayerIndex = layer(path)
    CHANGES.append(f"~ {o['layer']} {prefix} -> {path} ({why})")

def fp(prefix):
    return find(prefix)['fp']

def set_layer(path, role, key="", mode="color"):
    settings['layers'][path] = {"use": True, "role": role, "programKey": key, "mode": mode}

# ---------------------------------------------------------------- programs
legend = json.load(open(sys.argv[4]))['programs'] if len(sys.argv) > 4 else None
if settings.get('programs') is None and legend:
    settings['programs'] = [{"key": p['key'], "label": p['label'], "fill": p.get('fill'), "order": i, "isCustom": False,
                             "aliases": p.get('aliases', [])} for i, p in enumerate(legend)]
if not any(p['key'] == 'PARKING' for p in settings['programs']):
    settings['programs'].append({"key": "PARKING", "label": "Parking", "fill": "#C3CBD3", "order": len(settings['programs']),
                                 "isCustom": True, "aliases": ["PARKING", "GARAGE", "PARKING GARAGE"]})
    CHANGES.append("+ custom program PARKING (#C3CBD3)")

# 1. The BUILDING SUPPORT bar is the parking garage.
for g in ('7ba11b', '917045', 'e08ab6', '76901b', '747fa6'):
    move(g, 'PROGRAM::Parking', 'parking garage')
    settings['names'][find(g)['id']] = 'PARKING GARAGE'
set_layer('PROGRAM::Parking', 'Program', 'PARKING', '')

# 2. Labels from the markup (plan L1M).
labels = {
    'CORE LAB': ['f44c5e', 'd7cd61', '496299', '8afa85', '6a7d47', 'a475fa'],
    'PROTOTYPING': ['00c189', '886ab6', '260d04'],
    'GALLERY': ['67c455'],
    'SERVICE CORRIDOR': ['afff09', '1bf420', 'cd50a6'],
    'GENERAL SUPPORT': ['2b213a'],
    'EVENT SUPPORT': ['8693bb'],
}
for label, ids in labels.items():
    for i in ids:
        settings['names'][find(i)['id']] = label
    CHANGES.append(f"~ label {label}: {len(ids)} block(s)")

# 3. The three diagonal-bar core labs are double height (L1 + L2): keep them tall instead of splitting.
for i in ('8afa85', '6a7d47', 'a475fa'):
    settings['spanModes'][find(i)['id']] = 'KeepTall'
CHANGES.append("~ span KeepTall: 3 core labs (diagonal bar, 0-34 ft)")

# 4. LOADING: the uncovered corner of the west bar (only outline curves were modelled there).
l1_solids = unary_union([o['fp'] for o in objs.values() if o['layer'].startswith('PROGRAM::') and o['type'] != 'PolylineCurve'
                         and o['closed'] and o['z0'] < 1 and o['z1'] > 10 and o['fp'] is not None and o['fp'].geom_type != 'LineString'])
loading = unary_union([fp('c2e384'), fp('9a17fe')]).difference(l1_solids.buffer(0.01))
loading = max(parts(loading), key=lambda p: p.area)
add_solid(loading, 0, 18, 'PROGRAM::Support', 'LOADING', '(west bar, where only outline curves were)')

# 5. Circulation (light yellow CIRCULATION) inside the glass: L1 0-18, L2 20-34, L3 36-50. Programs are smaller, so
#    the plugin's overlap rule (smaller volume wins) leaves circulation the walkable remainder.
glass1 = find('9fd856')['fp']                                   # L1 glass line (open curve along the river)
garage = fp('7ba11b')
l2_plates = [o['fp'] for o in objs.values() if o['layer'] == 'FLOOR PLATE' and o['type'] != 'PolylineCurve' and 17.5 <= o['z0'] <= 18.1
             and o['id'][:6] not in ('a3c4c9', 'f4881b', 'b076ad') and o['fp'] is not None and o['fp'].geom_type != 'LineString']
building_l1 = unary_union([l1_solids.difference(garage.buffer(1)), *l2_plates])
inside_glass = Polygon(list(glass1.coords)).buffer(0)          # glass line closed by its chord through the building
circ1 = unary_union([building_l1, inside_glass]).buffer(0.5).buffer(-0.5)
circ1 = max(parts(circ1), key=lambda p: p.area)
circ1 = Polygon(circ1.exterior)                                  # no holes: programs and cores carve it
circ1 = circ1.difference(garage.buffer(1))
add_solid(circ1, 0, 18, 'PROGRAM::Circulation', '', '(L1, inside the glass)')
circ2 = unary_union([fp('2987fd'), fp('c81cd8'), fp('b076ad')]).buffer(0.3).buffer(-0.3)
add_solid(circ2, 20, 34, 'PROGRAM::Circulation', '', '(L2 walkable floor inside glass)')
circ3 = unary_union([fp('67835f'), fp('0e780a')]).buffer(0.3).buffer(-0.3)
add_solid(circ3, 36, 50, 'PROGRAM::Circulation', '', '(L3 walkable floor inside glass)')
set_layer('PROGRAM::Circulation', 'Program', 'CIRCULATION', '')

# 6. Stairs are drawn like structure: no name, no area, no slab.
set_layer('DETAIL::TREADS', 'NC.Structure', '', 'grayscale')
set_layer('PROGRAM::Stair', 'NC.Structure', '', 'grayscale')
CHANGES.append("~ roles: DETAIL::TREADS, PROGRAM::Stair -> NC.Structure")

# 7. Levels at the slab tops (the 16/32/48 overrides drew a second slab 4 ft under every plate).
settings['levels'] = [{"name": "L1M", "elevation": 0, "isMezzanine": False}, {"name": "L2", "elevation": 20.0, "isMezzanine": False},
                      {"name": "L3", "elevation": 36.0, "isMezzanine": False}, {"name": "L4", "elevation": 52.0, "isMezzanine": False},
                      {"name": "L5", "elevation": 77.07, "isMezzanine": False}]
CHANGES.append("~ levels: L2 20', L3 36', L4 52', L5 77'-1\" (slab tops)")

# 8. Mechanical houses and their roof slabs: fill the core openings so the house wraps the cores without a gap.
for i in ('045f2f', 'd87327', 'afbfcc', '938132', 'a55bf7', 'a2020f'):
    o = find(i); ro = rh_obj(o['id'])
    ext = ro.Geometry
    outer = ext.Profile3d(0, 0.0)
    if ext.ProfileCount < 2:
        continue
    pts = Polygon([(outer.PointAt(t).X, outer.PointAt(t).Y) for t in
                   [outer.Domain.T0 + (outer.Domain.T1 - outer.Domain.T0) * k / 96 for k in range(96)]])
    add_solid(pts, o['z0'], o['z1'], o['layer'], ro.Attributes.Name, f"(replaces {i}: core openings filled)")
    model.Objects.Delete(ro.Attributes.Id)

# 9. Ramp court: the SITE LANDSCAPE plate is a ramp (street → +18 ft entrance plaza) with parking under it.
slope = lambda x, y: max(0.0, min(18.8, 0.0756 * (y + 49.0)))
ramp = Polygon(find('a358e3')['fp'].exterior)
road = Polygon(find('192b69')['fp'].coords).buffer(0).difference(Point(426.2, 171.0).buffer(15.0))
green_w = Polygon(find('b666b0')['fp'].coords).buffer(0).intersection(ramp)
green_e = Polygon(list(find('38787a')['fp'].coords) + [(526.2, 148.0), (526.2, -49.0)]).buffer(0).intersection(ramp)
plaza = ramp.difference(unary_union([road, green_w, green_e]))
add_area(road, slope, 'PROJECT SITE::ROAD', '', '(driveway up the ramp, draped)')
add_area(unary_union([green_w, green_e]), slope, 'PROJECT SITE::GREEN SPACE', '', '(the two planted strips, draped)')
add_area(plaza, slope, 'PROJECT SITE::PLAZA', '', '(ramp paving, draped)')
for i in ('b666b0', '38787a', '86cdb7', '900dfa', 'd5d92e'):
    move(i, 'SITE LANDSCAPE', 'ramp reference curve')
move('e52a48', 'PROJECT SITE::PLAZA', 'entrance plaza at +18 ft')
rh_obj(find('e52a48')['id']).Attributes.Name = 'MAIN ENTRANCE'
under = ramp.intersection(box(-1e4, 83.0, 1e4, 1e4)).difference(building_l1.buffer(2))
add_solid(under, 0, 9, 'PROJECT SITE::PARKING BELOW', 'PARKING BELOW', '(under the ramp where it clears 10 ft; dashed massing)')
service = Polygon([(16, -58), (125, -58), (125, 56), (16, 56)]).difference(building_l1.buffer(4))
add_area(service, 0.0, 'PROJECT SITE::PLAZA', 'LOADING AND BOH SERVICE', '(service yard)')
set_layer('SITE LANDSCAPE', 'Ignore', '', '')          # the 3D ramp breps stay as reference
set_layer('SITE BASE', 'BG.Base', '', '')
set_layer('greens', 'NC.Green')
set_layer('PROJECT SITE', 'Ignore', '', '')
set_layer('PROJECT SITE::ROAD', 'BG.Road')
set_layer('PROJECT SITE::GREEN SPACE', 'NC.Green')
set_layer('PROJECT SITE::PLAZA', 'NC.Hardscape')
set_layer('PROJECT SITE::PARKING BELOW', 'NC.Massing', '', 'line')
CHANGES.append("~ roles: SITE LANDSCAPE -> Ignore (3D reference), SITE BASE -> BG.Base, greens -> NC.Green")

model.Strings[KEY] = json.dumps(settings)
model.Write(sys.argv[3], 8)
print("\n".join(CHANGES))
json.dump(settings, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)

"""Leftover parcel ground of the DRAFT_1 model (what no road, green, garden, park or building covers)."""
import sys, json
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import inv
from r21_lib import footprint, parts, unary_union
from r20_envelope import envelope
from shapely.geometry import Polygon

def leftover(path):
    raw = json.load(open(path))['objects']
    objs = inv.load(path)
    rid = {o['id']: o for o in raw}
    def fp(o):
        g = o['fp']
        if g is None and rid[o['id']].get('mesh'): g = footprint(rid[o['id']])
        return g
    parcel = next(o for o in objs if o['layer'] == 'Plats::MX3')['fp']
    parcel = Polygon(parcel.coords) if parcel.geom_type == 'LineString' else parcel
    env = envelope(raw, 0, 18)[0]
    masses = [fp(o) for o in objs if o['layer'].startswith('PROGRAM::') and o['layer'] != 'PROGRAM::Glass' and o['z0'] < 1 and o['z1'] > 3]
    site = [fp(o) for o in objs if o['z0'] < 3 and (o['layer'] in ('ROAD', 'greens', 'SITE LANDSCAPE') or o['layer'].startswith('PROJECT SITE::'))]
    cov = unary_union([g for g in masses + site + [env] if g is not None and g.geom_type in ('Polygon', 'MultiPolygon')])
    building = unary_union([g for g in masses + [env] if g is not None and g.geom_type in ('Polygon', 'MultiPolygon')])
    return parcel.difference(cov).buffer(-0.75).buffer(0.75), building, parcel

if __name__ == '__main__':
    left, _, _ = leftover(sys.argv[1])
    for p in sorted(parts(left), key=lambda p: -p.area):
        if p.area > 50: print(round(p.area), [round(v) for v in p.bounds])

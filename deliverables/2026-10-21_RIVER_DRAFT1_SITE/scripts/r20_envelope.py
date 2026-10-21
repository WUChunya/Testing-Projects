"""Glass envelope per storey for the RIVER 1020 model: the area enclosed by PROGRAM::Glass and the program masses.
Usage: python3 -I r20_envelope.py <fixture.json> <out.png>   (prints the envelope area per storey)"""
import json, sys
import numpy as np
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
from shapely.geometry import Polygon, LineString, MultiPolygon
from shapely.ops import unary_union
FT = 304.8
STOREYS = {'L1': (0, 18), 'L2': (20, 34), 'L3': (36, 50)}

def tri_parts(o, z0, z1, whole=False):
    """Plan footprint of the mesh part within [z0, z1]: faces as polygons, vertical faces as segments."""
    m = o.get('mesh')
    if not m: return [], []
    v = np.array(m['v'], float).reshape(-1, 3) / FT
    polys, segs = [], []
    for f in m['f']:
        p = v[f]
        if not whole and (p[:, 2].max() < z0 + 0.5 or p[:, 2].min() > z1 - 0.5): continue
        g = Polygon(p[:, :2]) if len(p) >= 3 else None
        if g is not None and g.is_valid and g.area > 0.5: polys.append(g)
        else:
            xy = p[:, :2]; i, j = np.unravel_index(np.argmax(((xy[:, None] - xy[None]) ** 2).sum(-1)), (len(xy), len(xy)))
            if np.hypot(*(xy[i] - xy[j])) > 0.3: segs.append(LineString([xy[i], xy[j]]))
    return polys, segs

def envelope(objs, z0, z1, gap=1.5):
    masses, glass = [], []
    for o in objs:
        b = [x / FT for x in o['bbox']]
        if b[5] < z0 + 0.5 or b[2] > z1 - 0.5: continue
        if o['layer'] == 'PROGRAM::Glass':
            if b[5] - b[2] < 1: continue                              # flat glass (skylights, the 34' sheet)
            p, s = tri_parts(o, z0, z1); glass += s + [q.boundary for q in p]
        elif o['layer'].startswith('PROGRAM::') and o['isClosed'] and 'Curve' not in o['type'] and o['layer'] != 'PROGRAM::Parking':
            p, s = tri_parts(o, z0, z1, whole=True)
            if p: masses.append(unary_union(p).buffer(0))
    walls = unary_union(glass).buffer(gap / 2)
    solid = unary_union(masses + [walls])
    filled = unary_union([Polygon(g.exterior) for g in getattr(solid, 'geoms', [solid]) if g.geom_type == 'Polygon'])
    return filled.buffer(-gap / 2, join_style=2).buffer(gap / 2, join_style=2), unary_union(masses), unary_union(glass)

if __name__ == '__main__':
    objs = json.load(open(sys.argv[1]))['objects']
    fig, axs = plt.subplots(1, 3, figsize=(24, 9))
    for ax, (n, (z0, z1)) in zip(axs, STOREYS.items()):
        env, ms, gl = envelope(objs, z0, z1)
        for g in getattr(env, 'geoms', [env]): ax.fill(*g.exterior.xy, color='y', alpha=.4)
        for g in getattr(ms, 'geoms', [ms]): ax.fill(*g.exterior.xy, color='grey', alpha=.6)
        for g in getattr(gl, 'geoms', [gl]): ax.plot(*g.xy, color='b', lw=1)
        ax.set_title(f'{n} envelope {env.area:,.0f} sf masses {ms.area:,.0f}'); ax.set_aspect('equal'); ax.set_xlim(-20, 700); ax.set_ylim(-110, 620)
        print(n, round(env.area), round(ms.area))
    plt.savefig(sys.argv[2], dpi=50, bbox_inches='tight')

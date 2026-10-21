"""Colour plot (paving types) of a level band. Usage: plot_d1b.py <fixture.json> <zlo> <zhi> <out.png> [x0 x1 y0 y1]"""
import sys, json
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
import inv
from r21_lib import footprint
raw = {o['id']: o for o in json.load(open(sys.argv[1]))['objects']}
zlo, zhi = float(sys.argv[2]), float(sys.argv[3])
lim = [float(v) for v in sys.argv[5:9]] or [-80, 760, -110, 700]
C = [('CONTEXT::RIVER', '#d6e8f5'), ('GROVE', '#7fae6f'), ('EDGE', '#7fae6f'), ('greens', '#8cc878'), ('LAWN', '#9fd685'), ('PLANTING', '#4f8a4f'), ('PLANTER', '#c86a6a'),
     ('RAIN GARDEN', '#9cc9e6'), ('WATER PLAY', '#7fb9e6'), ('GRAVEL', '#ece8dc'), ('PAVING', '#cfcfcf'), ('PLAZA', '#d6d6d6'), ('PROMENADE', '#d6d6d6'),
     ('GARDENS::PATH', '#e5a99a'), ('GARDEN WALK', '#e5a99a'), ('CYCLE', '#d97a6a'), ('GROVE', '#7fae6f'), ('DECK', '#d9a46a'), ('BOARDWALK', '#c9955c'), ('SEATING STEPS', '#c48a4e'), ('STAGE', '#bbb'),
     ('INLAY', '#6f6f78'), ('KAYAK', '#c9955c'), ('RIVER STEPS', '#d8c8a8'), ('ROAD', '#c8c8c8'), ('PAVILION', '#ffffff'), ('Stair', '#777')]
order = lambda o: next((i for i, (k, _) in enumerate(C) if k in o['layer']), 99)
objs = sorted([o for o in inv.load(sys.argv[1]) if o['z1'] >= zlo - 1 and o['z0'] <= zhi], key=order)
fig, ax = plt.subplots(figsize=(20, 19))
for o in objs:
    L = o['layer']; g = o['fp'] or (footprint(raw[o['id']]) if raw[o['id']].get('mesh') else None)
    if g is None: continue
    if L.startswith('PROGRAM::') and L not in ('PROGRAM::Glass', 'PROGRAM::Stair'):
        if o['z0'] <= zlo + 2 and o['z1'] >= zlo + 3:
            for p in getattr(g, 'geoms', [g]):
                if p.geom_type == 'Polygon': ax.fill(*p.exterior.xy, color='#999', lw=0)
        continue
    col = next((c for k, c in C if k in L), None)
    if 'TREES' in L: col = 'tree'
    if 'PEOPLE' in L or 'FURNITURE' in L or 'LINES' in L: col = 'line'
    if col is None: continue
    for p in getattr(g, 'geoms', [g]):
        if p.geom_type == 'Polygon':
            if col == 'tree': ax.fill(*p.exterior.xy, color='#3f7f3a', alpha=.4, lw=0)
            elif col == 'line': ax.plot(*p.exterior.xy, color='#fff' if 'LINES' in L else '#333', lw=.5)
            else:
                ax.fill(*p.exterior.xy, color=col, lw=.2, ec='#666')
                for h in p.interiors: ax.fill(*h.xy, color='white', lw=0)
        elif p.geom_type == 'LineString': ax.plot(*p.xy, color='#fff' if 'LINES' in L else '#333', lw=.7)
ax.set_aspect('equal'); ax.set_xlim(lim[0], lim[1]); ax.set_ylim(lim[2], lim[3]); ax.set_facecolor('#f4f4f4')
plt.savefig(sys.argv[4], dpi=75, bbox_inches='tight')

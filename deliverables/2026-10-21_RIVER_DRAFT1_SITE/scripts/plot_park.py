"""Colour plot of the ground site (park, gardens, plaza) by paving type. Usage: plot_park.py <fixture.json> <out.png>"""
import sys, json
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
import inv
from r21_lib import footprint
raw = {o['id']: o for o in json.load(open(sys.argv[1]))['objects']}
C = [('GROVE', '#b9dba0'), ('greens', '#8cc878'), ('LAWN', '#9fd685'), ('RAIN GARDEN', '#9cc9e6'), ('WATER PLAY', '#7fb9e6'),
     ('PROMENADE', '#efe6d2'), ('GARDEN WALK', '#e9dcc0'), ('GARDENS::PATH', '#e9dcc0'), ('BOARDWALK', '#c9955c'), ('SEATING STEPS', '#c48a4e'),
     ('GARDENS::SEATING', '#c48a4e'), ('RIVER STEPS', '#d8c8a8'), ('INLAY', '#6f6f78'), ('PAVING INLAY', '#6f6f78'), ('PLAZA', '#f3ead6'),
     ('KAYAK', '#c9955c'), ('ROAD', '#c8c8c8'), ('SITE LANDSCAPE', '#ddd'), ('PAVILION', '#fff'), ('RIVER', '#d6e8f5')]
order = lambda o: next((i for i, (k, _) in enumerate(C) if k in o['layer']), 99)
objs = sorted([o for o in inv.load(sys.argv[1]) if o['z0'] < 3], key=order)
fig, ax = plt.subplots(figsize=(20, 19))
for o in objs:
    L = o['layer']; g = o['fp'] or (footprint(raw[o['id']]) if raw[o['id']].get('mesh') else None)
    if g is None: continue
    if L.startswith('PROGRAM::') and o['z1'] > 3 and L != 'PROGRAM::Glass':
        for p in getattr(g, 'geoms', [g]):
            if p.geom_type == 'Polygon': ax.fill(*p.exterior.xy, color='#888', lw=0)
        continue
    col = next((c for k, c in C if k in L), None)
    if 'TREES' in L: col = 'tree'
    if 'PEOPLE' in L or 'FURNITURE' in L: col = 'line'
    if col is None: continue
    for p in getattr(g, 'geoms', [g]):
        if p.geom_type == 'Polygon':
            if col == 'tree': ax.fill(*p.exterior.xy, color='#5a9a4a', alpha=.45, lw=0)
            elif col == 'line': ax.plot(*p.exterior.xy, color='#333', lw=.4)
            else:
                ax.fill(*p.exterior.xy, color=col, lw=.2, ec='#555')
                for h in p.interiors: ax.fill(*h.xy, color='white', lw=0)
        elif p.geom_type == 'LineString': ax.plot(*p.xy, color='#333', lw=.6)
ax.set_aspect('equal'); ax.set_xlim(-80, 760); ax.set_ylim(-110, 700); ax.axis('off')
plt.savefig(sys.argv[2], dpi=75, bbox_inches='tight')

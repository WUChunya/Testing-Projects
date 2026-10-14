# Z-buffer reference render of the Downtown fixture (what Rhino shows): NE iso by default; also writes a class map
# (*_cls.png) and the triangle id buffer (*_tri.npy / .pkl) for regions.py. Downtown-specific (bbox, layer names).
import json, math, sys
import numpy as np
from PIL import Image
src, out = sys.argv[1], sys.argv[2]
az = float(sys.argv[3]) if len(sys.argv) > 3 else 45
zmax = float(sys.argv[4]) if len(sys.argv) > 4 else 1e9
skip = sys.argv[5].split(',') if len(sys.argv) > 5 and sys.argv[5] else []
d = json.load(open(src))
col = {'FLOOR PLATE': '#d8d8d8', 'PROGRAM::Glass': '#66aaff', 'PROGRAM::Core': '#888888', 'PROGRAM::Lab': '#f0a030',
       'PROGRAM::Lab Support': '#f5c870', 'PROGRAM::Enclosed Office': '#70b8cc', 'PROGRAM::Conference': '#c070a0',
       'PROGRAM::Break Room': '#f0b0d0', 'PROGRAM::Circulation': '#e0e070', 'PROGRAM::Seminar Room': '#906090',
       'PROGRAM::Shell Space': '#ffffff', 'PROGRAM::Support': '#bbbbbb', 'PROGRAM::Green Roof': '#90d090', 'PROGRAM': '#ff4040'}
X0, X1, Y0, Y1 = 3100 * 304.8, 3430 * 304.8, 2270 * 304.8, 2580 * 304.8
e = math.radians(35.264); a = math.radians(az)
c = np.array((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
r = np.array((-c[1], c[0], 0)); r /= np.linalg.norm(r)
u = np.cross(r, -c)
tris = []
for o in d['objects']:
    lay = o['layer']
    if lay not in col or not o.get('mesh') or lay in skip: continue
    b = o['bbox']
    if b[3] < X0 or b[0] > X1 or b[4] < Y0 or b[1] > Y1: continue
    if b[2] / 304.8 > zmax: continue
    if b[5] / 304.8 < 1.5 and b[2] / 304.8 > -2: continue  # at-grade paving / thin plates
    V = np.array(o['mesh']['v'], float).reshape(-1, 3) / 304.8
    for f in o['mesh']['f']:
        for k in range(1, len(f) - 1):
            tris.append((V[[f[0], f[k], f[k + 1]]], lay, o['id']))
P = np.array([t[0] for t in tris])  # n,3,3
sx = P @ r; sy = -(P @ u); sd = P @ c
W = int(sys.argv[6]) if len(sys.argv) > 6 else 1400
ab = P[..., 2] >= -0.5
mnx, mxx, mny, mxy = sx[ab].min(), sx[ab].max(), sy[ab].min(), sy[ab].max()
s = (W - 20) / (mxx - mnx); H = int((sy.max() - mny) * s) + 20
sx = (sx - mnx) * s + 10; sy = (sy - mny) * s + 10
img = np.ones((H, W, 3)); zb = np.full((H, W), -1e18); idb = np.full((H, W), -1)
gimg = np.zeros((H, W, 3)); gz = np.full((H, W), -1e18)
import os; sys.path.insert(0, os.path.dirname(__file__)); from classes import LAYER, color
cimg = np.ones((H, W, 3)) * 255
oid_b = np.full((H, W), -1)
def rgb(h): h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
light = np.array((0.3, 0.5, 0.8)); light /= np.linalg.norm(light)
for i, (tp, lay, oid) in enumerate(tris):
    n = np.cross(tp[1] - tp[0], tp[2] - tp[0]); L = np.linalg.norm(n)
    if L < 1e-9: continue
    n /= L; shade = 0.55 + 0.45 * abs(n @ light)
    x, y, z = sx[i], sy[i], sd[i]
    x0, x1 = int(max(0, math.floor(x.min()))), int(min(W - 1, math.ceil(x.max())))
    y0, y1 = int(max(0, math.floor(y.min()))), int(min(H - 1, math.ceil(y.max())))
    if x1 < x0 or y1 < y0: continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    den = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
    if abs(den) < 1e-12: continue
    w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / den
    w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / den
    w2 = 1 - w0 - w1
    m = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
    zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
    if lay == 'PROGRAM::Glass':
        sub = gz[y0:y1 + 1, x0:x1 + 1]; upd = m & (zz > sub)
        sub[upd] = zz[upd]; gimg[y0:y1 + 1, x0:x1 + 1][upd] = rgb(col[lay]) * shade
        continue
    sub = zb[y0:y1 + 1, x0:x1 + 1]; upd = m & (zz > sub + 1e-4)
    sub[upd] = zz[upd]; img[y0:y1 + 1, x0:x1 + 1][upd] = rgb(col[lay]) * shade
    oid_b[y0:y1 + 1, x0:x1 + 1][upd] = i
    cimg[y0:y1 + 1, x0:x1 + 1][upd] = color(LAYER[lay]) if lay in LAYER else (255, 255, 255)
    idb[y0:y1 + 1, x0:x1 + 1][upd] = hash(oid) % 1000003 * 10 + (0 if abs(n[2]) > 0.9 else 1 + int(abs(n[0]) > abs(n[1])))
# edges: id or depth jumps
edge = np.zeros((H, W), bool)
edge[:, 1:] |= idb[:, 1:] != idb[:, :-1]; edge[1:, :] |= idb[1:, :] != idb[:-1, :]
img[edge] *= 0.25
gm = gz > zb
img[gm] = img[gm] * 0.5 + gimg[gm] * 0.5
print('map', mnx, mny, s, list(r), list(u))
np.save(out.replace('.png', '_tri.npy'), oid_b)
import pickle; pickle.dump([(t[1], t[2], [float(v) for v in t[0][:, 2]]) for t in tris], open(out.replace('.png', '_tri.pkl'), 'wb'))
Image.fromarray(cimg.astype(np.uint8)).save(out.replace('.png', '_cls.png'))
Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(out)

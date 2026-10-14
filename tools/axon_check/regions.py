import sys, numpy as np, pickle, json
sys.path.insert(0, sys.argv[1]); from classes import CLS, color
from PIL import Image
from scipy import ndimage
from collections import Counter
S = sys.argv[2]
a = np.array(Image.open(S + '/shots/ourcls.png').convert('RGB')).astype(int)
b = np.array(Image.open(S + '/shots/zref_al_cls.png').convert('RGB')).astype(int)
oi = np.array(Image.open(S + '/shots/ourid.png').convert('RGB')).astype(int)
ids = json.load(open(S + '/ourid.svg.json'))
tri = np.load(S + '/shots/zref_al_tri.npy'); info = pickle.load(open(S + '/shots/zref_al_tri.pkl', 'rb'))
d = json.load(open(sys.argv[3])); bb = {o['id']: [round(v / 304.8, 1) for v in o['bbox']] for o in d['objects']}
H = min(a.shape[0], b.shape[0]); a, b, oi, tri = a[:H], b[:H], oi[:H], tri[:H]
pal = {color(c): c for c in CLS}; pal[(255, 255, 255)] = 'NONE'
def lab(img):
    out = np.full(img.shape[:2], 'X', dtype=object)
    for k, v in pal.items(): out[(img == np.array(k)).all(-1)] = v
    return out
la, lb = lab(a), lab(b)
valid = (la != 'X') & (lb != 'X')
diff = valid & (la != lb) & ~((la == 'SCREEN') & ((lb == 'SLAB') | (lb == 'CORE')))
diff = ndimage.binary_opening(diff, iterations=3)
L, n = ndimage.label(diff)
oidx = oi[..., 0] * 65536 + oi[..., 1] * 256 + oi[..., 2]
for r in sorted(range(1, n + 1), key=lambda r: -(L == r).sum()):
    m = L == r
    if m.sum() < 1500: continue
    ys, xs = np.nonzero(m)
    print('REGION size', m.sum(), 'box', xs.min() // 2, ys.min() // 2, xs.max() // 2, ys.max() // 2)
    cnt = Counter()
    for y, x in zip(ys[::7], xs[::7]):
        k = oidx[y, x]; ours = ids[k - 1] if 0 < k <= len(ids) else 'none'
        t = tri[y, x]
        if t >= 0:
            lay, oid, zs = info[t]; bo = bb[oid]; ref = f'{lay[-14:]} x{bo[0]}-{bo[3]} y{bo[1]}-{bo[4]} z{bo[2]}-{bo[5]}'
        else: ref = 'none'
        cnt[(ours, ref)] += 1
    for (o, rf), k in cnt.most_common(4): print('   ', k, o, '|', rf)

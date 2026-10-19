"""Register the Town site plan page (pt, y down) to the CAMPUS model (ft) on the four parcel outlines (SURROUNDING::LOT).
Usage: python3 -I town_fit.py <town.json> <campus.3dm>  → prints s, rotation, t (ICP + Umeyama similarity)."""
import json, math, sys
import numpy as np
import rhino3dm as r

site = json.load(open(sys.argv[1]))
solid = [d["rings"][0] for d in site if d["layer"] == "Site Plan" and d["stroke"] and not d["dashes"].strip("[] 0")]
m = r.File3dm.Read(sys.argv[2])
mv = []
for o in m.Objects:
    if m.Layers[o.Attributes.LayerIndex].FullPath != "SURROUNDING::LOT": continue
    b = o.Geometry.ToBrep(False)
    z = max(b.Vertices[i].Location.Z for i in range(len(b.Vertices)))
    mv += [(b.Vertices[i].Location.X, b.Vertices[i].Location.Y) for i in range(len(b.Vertices)) if b.Vertices[i].Location.Z > z - 0.5]
MV = np.array(mv)
A = np.array([(x, -y) for rg in solid for x, y in rg])
s, R, t = 100 / 72, np.eye(2), np.array([2340.8, 3057.7])
for it in range(30):
    T = (s * (R @ A.T)).T + t
    dist = np.linalg.norm(T[:, None, :] - MV[None, :, :], axis=2)
    j, dm = dist.argmin(1), dist.min(1)
    keep = dm < (15 if it < 5 else 4)
    a, b = A[keep], MV[j[keep]]
    ma, mb = a.mean(0), b.mean(0)
    U, S_, Vt = np.linalg.svd((b - mb).T @ (a - ma)); D = np.diag([1, np.sign(np.linalg.det(U @ Vt))]); R = U @ D @ Vt
    s = (S_ * np.diag(D)).sum() / ((a - ma) ** 2).sum(); t = mb - s * R @ ma
T = (s * (R @ A.T)).T + t
dm = np.linalg.norm(T[:, None, :] - MV[None, :, :], axis=2).min(1)
print(json.dumps({"s": s, "rot": math.degrees(math.atan2(R[1, 0], R[0, 0])), "t": list(t), "kept": int(keep.sum()), "n": len(A),
                  "median": float(np.median(dm)), "maxKept": float(dm[dm < 4].max())}))
print(np.round(dm, 1).tolist())

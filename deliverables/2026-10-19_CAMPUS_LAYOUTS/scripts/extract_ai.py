"""Flatten the site plan .ai (PDF) drawings into rings per (layer, fill) in page points (y down)."""
import json, sys
import pymupdf

def bez(p0, c1, c2, p3, n=8):
    out = []
    for i in range(1, n + 1):
        t = i / n; u = 1 - t
        out.append((u**3*p0.x + 3*u*u*t*c1.x + 3*u*t*t*c2.x + t**3*p3.x, u**3*p0.y + 3*u*u*t*c1.y + 3*u*t*t*c2.y + t**3*p3.y))
    return out

def rings(d):
    rs, cur, last = [], [], None
    for it in d["items"]:
        k = it[0]
        if k == "re":
            r = it[1]; rs.append([(r.x0, r.y0), (r.x1, r.y0), (r.x1, r.y1), (r.x0, r.y1)]); last = None; continue
        if k == "qu":
            q = it[1]; rs.append([(q.ul.x, q.ul.y), (q.ur.x, q.ur.y), (q.lr.x, q.lr.y), (q.ll.x, q.ll.y)]); last = None; continue
        p0 = it[1]
        if last is None or abs(last.x - p0.x) > 1e-3 or abs(last.y - p0.y) > 1e-3:
            if len(cur) > 2: rs.append(cur)
            cur = [(p0.x, p0.y)]
        if k == "l": cur.append((it[2].x, it[2].y)); last = it[2]
        elif k == "c": cur += bez(it[1], it[2], it[3], it[4]); last = it[4]
    if len(cur) > 2: rs.append(cur)
    return rs

doc = pymupdf.open(sys.argv[1])
out = []
for d in doc[0].get_drawings():
    out.append({"layer": d.get("layer"), "type": d.get("type"),
                "fill": [round(x, 3) for x in d["fill"]] if d.get("fill") else None,
                "stroke": [round(x, 3) for x in d["color"]] if d.get("color") else None,
                "width": d.get("width"), "dashes": d.get("dashes"), "closePath": d.get("closePath"),
                "rings": rings(d)})
json.dump(out, open(sys.argv[2], "w"))
print(len(out))

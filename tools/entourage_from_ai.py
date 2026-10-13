#!/usr/bin/env python3
"""Build styles/entourage_library.json from the office entourage .ai files (PDF-compatible).

Usage: python3 -I tools/entourage_from_ai.py PEOPLE.ai TREES.ai styles/entourage_library.json

The .ai files have merged layers: people are one compound path (one sub-path or a few per person), trees are
several fills per tree. Sub-paths are flattened (Bezier -> polyline), grouped into figures by overlapping
bounding boxes, de-duplicated, normalised to height 1 with the origin at the bottom centre (y up), and
simplified. Requires pypdf.
"""
import json
import math
import sys

from pypdf import PdfReader
from pypdf.generic import ContentStream

TOL = 0.004       # flattening / simplification tolerance, as a fraction of the figure height
DIGITS = 4


def mul(a, b):
    """PDF matrices [a b c d e f]: a then b."""
    return [a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3],
            a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3],
            a[4] * b[0] + a[5] * b[2] + b[4], a[4] * b[1] + a[5] * b[3] + b[5]]


def apply(m, x, y):
    return (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5])


def bezier(p0, p1, p2, p3, n=12):
    out = []
    for i in range(1, n + 1):
        t = i / n
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def walk(stream_obj, reader, resources, ctm, color, fills):
    """Interpret path/fill operators; append (colour, [rings]) per fill."""
    stack = []
    rings, cur = [], None
    for operands, op in ContentStream(stream_obj, reader).operations:
        op = op.decode() if isinstance(op, bytes) else op
        if op == 'q':
            stack.append((ctm, color))
        elif op == 'Q':
            ctm, color = stack.pop()
        elif op == 'cm':
            ctm = mul([float(v) for v in operands], ctm)
        elif op in ('scn', 'sc', 'g', 'k', 'rg'):
            color = [float(v) for v in operands if not hasattr(v, 'startswith')]
            if op == 'g':
                color = [0, 0, 0, 1 - color[0]]
        elif op == 'm':
            cur = [apply(ctm, float(operands[0]), float(operands[1]))]
            rings.append(cur)
        elif op == 'l':
            cur.append(apply(ctm, float(operands[0]), float(operands[1])))
        elif op in ('c', 'v', 'y'):
            v = [float(x) for x in operands]
            p0 = cur[-1]
            if op == 'c':
                p1, p2, p3 = apply(ctm, v[0], v[1]), apply(ctm, v[2], v[3]), apply(ctm, v[4], v[5])
            elif op == 'v':
                p1, p2, p3 = p0, apply(ctm, v[0], v[1]), apply(ctm, v[2], v[3])
            else:
                p1, p2, p3 = apply(ctm, v[0], v[1]), apply(ctm, v[2], v[3]), apply(ctm, v[2], v[3])
            cur.extend(bezier(p0, p1, p2, p3))
        elif op == 'h':
            pass
        elif op == 're':
            x, y, w, h = (float(v) for v in operands)
            cur = [apply(ctm, x, y), apply(ctm, x + w, y), apply(ctm, x + w, y + h), apply(ctm, x, y + h)]
            rings.append(cur)
        elif op in ('f', 'F', 'f*', 'B', 'B*', 'b', 'b*'):
            fills.append((tuple(color), [r for r in rings if len(r) > 2]))
            rings, cur = [], None
        elif op in ('n', 'S', 's', 'W', 'W*'):
            if op != 'W' and op != 'W*':
                rings, cur = [], None
        elif op == 'Do':
            xo = resources['/XObject'][operands[0]].get_object()
            m = [float(v) for v in xo.get('/Matrix', [1, 0, 0, 1, 0, 0])]
            walk(xo, reader, xo.get('/Resources', resources), mul(m, ctm), color, fills)
    return fills


def bbox(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return [min(xs), min(ys), max(xs), max(ys)]


def overlap(a, b, pad):
    return a[0] - pad <= b[2] and b[0] - pad <= a[2] and a[1] - pad <= b[3] and b[1] - pad <= a[3]


def group(rings, pad):
    """Union-find of rings whose boxes overlap -> figures."""
    boxes = [bbox(r) for r in rings]
    parent = list(range(len(rings)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for i in range(len(rings)):
        for j in range(i + 1, len(rings)):
            if overlap(boxes[i], boxes[j], pad):
                parent[find(i)] = find(j)
    out = {}
    for i in range(len(rings)):
        out.setdefault(find(i), []).append(i)
    return list(out.values())


def rdp(pts, eps):
    if len(pts) < 3:
        return pts
    (x1, y1), (x2, y2) = pts[0], pts[-1]
    dx, dy = x2 - x1, y2 - y1
    n = math.hypot(dx, dy) or 1e-12
    best, idx = 0, 0
    for i in range(1, len(pts) - 1):
        d = abs(dy * pts[i][0] - dx * pts[i][1] + x2 * y1 - y2 * x1) / n
        if d > best:
            best, idx = d, i
    if best <= eps:
        return [pts[0], pts[-1]]
    return rdp(pts[:idx + 1], eps)[:-1] + rdp(pts[idx:], eps)


def simplify_ring(r, eps):
    # Split the closed ring at its farthest point so RDP keeps the shape.
    far = max(range(len(r)), key=lambda i: (r[i][0] - r[0][0]) ** 2 + (r[i][1] - r[0][1]) ** 2)
    a = rdp(r[:far + 1], eps)
    b = rdp(r[far:] + [r[0]], eps)
    return a[:-1] + b[:-1]


def area(r):
    return 0.5 * sum(r[i - 1][0] * r[i][1] - r[i][0] * r[i - 1][1] for i in range(len(r)))


def normalise(parts, foot=None):
    """Height 1, origin at the bottom centre (or the given foot x), y up. Returns parts of rings."""
    b = bbox([p for rs in parts for r in rs for p in r])
    h = b[3] - b[1]
    cx = (b[0] + b[2]) / 2 if foot is None else foot
    out = []
    for rs in parts:
        part = []
        for r in rs:
            q = [((x - cx) / h, (y - b[1]) / h) for x, y in r]
            q = simplify_ring(q, TOL)
            if len(q) >= 3 and abs(area(q)) > 1e-5:
                part.append([[round(x, DIGITS), round(y, DIGITS)] for x, y in q])
        if part:
            out.append(part)
    return out, (b[2] - b[0]) / h


def signature(parts):
    rings = [r for rs in parts for r in rs]
    b = bbox([p for r in rings for p in r])
    return (len(rings), round((b[2] - b[0]) / (b[3] - b[1]), 2), round(sum(abs(area(r)) for r in rings) * 100, 1))


def figures(path, min_h):
    r = PdfReader(path)
    page = r.pages[0]
    fills = walk(page.get_contents(), r, page['/Resources'], [1, 0, 0, 1, 0, 0], (0, 0, 0, 1), [])
    rings = [ring for _, rs in fills for ring in rs]
    owner = [k for k, (_, rs) in enumerate(fills) for _ in rs]
    groups = group(rings, pad=0)
    out = []
    for g in groups:
        rs = [rings[i] for i in g]
        b = bbox([p for x in rs for p in x])
        if b[3] - b[1] < min_h:
            continue
        # Parts = the original fills (overlapping fills of one tree must not cancel under even-odd).
        parts = {}
        for i in g:
            parts.setdefault(owner[i], []).append(rings[i])
        out.append((b, list(parts.values())))
    out.sort(key=lambda f: (-round(f[0][3] / 40), f[0][0]))   # rows top→bottom, then left→right
    return out


def trunk_x(parts, b):
    """Bottom centre of a tree = mid-x of the lowest 3 % of points (the trunk foot)."""
    pts = [p for rs in parts for r in rs for p in r]
    low = [p[0] for p in pts if p[1] <= b[1] + 0.03 * (b[3] - b[1])]
    return (min(low) + max(low)) / 2 if low else None


def main(people_ai, trees_ai, out_path):
    people, seen = [], {}
    for b, rs in figures(people_ai, min_h=2.0):
        norm, aspect = normalise(rs)
        sig = signature(norm)
        if sig in seen:
            seen[sig]["count"] += 1
            continue
        fig = {"id": f"P{len(people) + 1:02d}", "aspect": round(aspect, 3), "parts": norm, "count": 1}
        seen[sig] = fig
        people.append(fig)
    trees, seen = [], set()
    for b, rs in figures(trees_ai, min_h=20.0):
        norm, aspect = normalise(rs, trunk_x(rs, b))
        sig = signature(norm)
        if sig in seen:
            continue
        seen.add(sig)
        trees.append({"id": f"T{len(trees) + 1:02d}", "aspect": round(aspect, 3), "parts": norm})
    lib = {
        "version": 1,
        "comment": "Generated by tools/entourage_from_ai.py from the office ENTOURAGES.ai / TREES.ai. Rings are "
                   "normalised: height 1, origin at the bottom centre (feet / trunk foot), y up. Drawn as AI symbols.",
        "source": {"people": people_ai.split('/')[-1], "trees": trees_ai.split('/')[-1]},
        "people": people,
        "trees": trees,
    }
    with open(out_path, "w") as f:
        json.dump(lib, f, separators=(",", ":"))
    pts = sum(len(r) for x in people + trees for rs in x["parts"] for r in rs)
    print(f"people {len(people)} unique (of {sum(p['count'] for p in people)}), trees {len(trees)}, points {pts}")


if __name__ == "__main__":
    main(*sys.argv[1:4])

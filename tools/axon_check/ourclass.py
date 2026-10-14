import sys, re, os, json
IDS = os.environ.get('IDS') == '1'
sys.path.insert(0, sys.argv[5])
from classes import *
ours, out, k, ox, oy, W, H = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), 0, 0, 0
oy = float(sys.argv[6]); W = int(sys.argv[7]); H = int(sys.argv[8])
s = open(ours).read()
body = s[s.find('>', s.find('<svg')) + 1: s.rfind('</svg>')]
body = re.sub(r'<g id="(AXON_SITE|AXON_TREE|AXON_CONTEXT|PD_ANNOTATION|PD_LABELS|FACADE|AXON_LINE|AXON_LEVEL_LINE|PD_LEGEND)">.*?\n</g>', '', body, flags=re.S)
body = re.sub(r'<rect width="100%"[^>]*/>', '', body)
body = re.sub(r'<text.*?</text>', '', body, flags=re.S)
ids = []
def fix(m):
    tag = m.group(0); idm = re.search(r'id="([A-Z0-9_]+)"', tag)
    c = of_name(idm.group(1)) if idm else None
    if c is None or 'HATCH' in (idm.group(1) if idm else ''): return ''
    tag = re.sub(r'fill="[^"]*"', 'fill="%s"' % (hexc(c) if not IDS else '#%06X' % (len(ids) + 1)), tag); ids.append(idm.group(1))
    tag = re.sub(r'(stroke|fill-opacity|opacity|stroke-width)="[^"]*"', '', tag)
    return tag
body = re.sub(r'<path[^>]*/>', fix, body)
with open(out, 'w') as f:
    f.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" shape-rendering="crispEdges"><rect width="100%" height="100%" fill="#FFFFFF"/>')
    f.write(f'<g transform="translate({10 - ox * k},{10 - oy * k}) scale({k})">{body}</g></svg>')

if IDS: json.dump(ids, open(out + '.json', 'w'))

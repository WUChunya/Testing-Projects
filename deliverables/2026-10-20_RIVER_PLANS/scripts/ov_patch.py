"""Patch the user-edited PLANS OVERVIEW with a fresh plugin export, only where the plugin changed.

Usage: python3 -I ov_patch.py <edited.svg> <fresh overview.svg> <out.svg> <panel>[,<panel>...]
For each listed panel (L1M, L2 ...), the site background (PLO_<panel>_BG_*) and what is seen below
(PLO_<panel>_BELOW_*, PLO_<panel>_EXPOSED_*) are replaced by the fresh ones, in the same AI layer groups and at the
same place in the stacking order. Everything else in the edited file (labels, icons, edits) stays as it is.
"""
import sys
import xml.etree.ElementTree as ET

NS = "http://www.w3.org/2000/svg"
ET.register_namespace("", NS)
ET.register_namespace("xlink", "http://www.w3.org/1999/xlink")
edited, fresh, out, panels = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4].split(",")
U, O = ET.parse(edited), ET.parse(fresh)
prefixes = [f"PLO_{p}_{c}" for p in panels for c in ("BG_", "BELOW_", "EXPOSED_")]
hit = lambda e: any((e.get("id") or "").startswith(x) for x in prefixes)

def layer_of(root, parents, e):
    """The top-level AI layer group (child of the root svg) an element sits in."""
    while parents.get(e) is not None and parents[e] is not root:
        e = parents[e]
    return e.get("id")

def index(root):
    parents = {c: p for p in root.iter() for c in p}
    return parents

ur, orr = U.getroot(), O.getroot()
up, op = index(ur), index(orr)
# Where the old pieces sat, per (layer, panel): parent element and index of the first one.
slots, removed = {}, 0
for e in [e for e in ur.iter() if hit(e)]:
    par = up[e]
    key = (layer_of(ur, up, e), e.get("id").split("_")[1])
    if key not in slots: slots[key] = (par, list(par).index(e))
    par.remove(e); removed += 1
groups = {g.get("id"): g for g in ur if g.tag == f"{{{NS}}}g"}
added = 0
batches = {}
for e in [e for e in orr.iter() if hit(e)]:
    batches.setdefault((layer_of(orr, op, e), e.get("id").split("_")[1]), []).append(e)
for key, items in batches.items():
    lay, panel = key
    if key in slots:
        par, at = slots[key]
    elif lay in groups:
        par = groups[lay]
        at = 0 if lay == "PD_PROGRAM" else len(par)      # seen-below roofs lie under the program fills
    else:
        print("no layer", lay, "for", panel); continue
    for k, it in enumerate(items):
        par.insert(at + k, it); added += 1
U.write(out, encoding="utf-8", xml_declaration=True)
print(f"replaced {removed} → {added} elements in panels {', '.join(panels)}")

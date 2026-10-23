"""CAMPUS 2026-10-22: room names from the text on the layout pages, stray / duplicate geometry cleaned up.

Usage: python3 -I c22_clean.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
- Names: every page (Ground … 6th) is a 1" = 100' detail of the plan; its text is mapped back to model feet
  (page → model fitted on the floor outline drawn on the pages) and given to the room under it on that floor
  (object name + the plugin's names table). The ground-floor lab under both "Core Labs" and "Quantum High MIP" is
  split at the corridor between the two labels.
- Cleanup (nothing deleted: removed pieces go to OUTSIDE BUILDING (LEFTOVERS), hidden, Ignore):
  exact duplicates (auditorium chairs, parking lines, facade joints, a stair piece), a property-line surface 213 ft
  up, a 1 sq ft face 3,400 ft away on the L2 floor plate, a terrace strip lying inside the L5 lab, a second outline
  under the Micron Gallery, page-space arrows on PROGRAM. Section / elevation lines leave PROGRAM::INTERACTIVE for
  DETAIL::CUTLINE and the plugin's section lines are re-read from them.
"""
import sys, json, collections
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *

M = Model(sys.argv[1], sys.argv[2])
st = M.st
names = st.setdefault('names', {})
spans = st.setdefault('spanModes', {})
LEFTP = "OUTSIDE BUILDING (LEFTOVERS)"
M.m.Layers[M.LEFT].Visible = False
M.role(LEFTP, 'Ignore', mode='')

# ------------------------------------------------------------------ names from the layout pages
# (short id, name): text → room found by mapping each page's text into the model (see README).
NAMES = [
    # Ground
    ('b2a92464', 'COMBINABLE CLASSROOMS'), ('31dc01b0', 'COFFEE KIOSK'), ('b46938b4', 'SECURITY'), ('b40cc17f', 'FITNESS'),
    ('40935811', 'CORE LABS'), ('640634c4', 'STORAGE'), ('d958d791', 'MEP SUPPORT'), ('5fee001a', 'LOADING DOCK'),
    ('84089f72', 'PROTOTYPING / FABRICATION'), ('a702526d', 'AUDITORIUM'), ('674abca0', 'MICRON GALLERY'),
    # 2nd
    ('7f88d4e6', 'RESEARCH'), ('ac19fa3d', 'PARTNER / INCUBATOR'), ('e368cce9', 'CONFERENCE'),
    ('40bb665e', 'DINING PREP'), ('6bb0929e', 'DINING TERRACE'), ('455cb12c', 'OUTDOOR TERRACE'),
    # 3rd
    ('b8084b12', 'RESEARCH'), ('a5252cbb', 'RESEARCH'), ('d5fc4641', 'CONFERENCE'), ('ffcb5b89', 'SEMINAR'),
    # 4th
    ('c15b2922', 'RESEARCH'), ('bb5b69f1', 'RESEARCH'), ('e368652e', 'EXECUTIVE BRIEFING'), ('b72ea690', 'EXECUTIVE BRIEFING'),
    # 5th
    ('823cec9b', 'RESEARCH'), ('5f9a295e', 'ROOFTOP TERRACE'),
]
for short, nm in NAMES:
    oid = M.find(short)
    M.obj[oid].Attributes.Name = nm
    names[oid] = nm
M.log.append(f"names: {len(NAMES)} rooms named from the layout text")

# 6th: "Trelis" over both trellises (the roof trellis and the slatted one at L5).
tre = [o['id'] for o in M.fx if o['layer'] == 'TRELIS']
for oid in set(tre): M.obj[oid].Attributes.Name = 'TRELLIS'
M.log.append(f"trellis: {len(set(tre))} objects named TRELLIS")

# Ground: one lab object under "Core Labs" (north of the corridor) and "Quantum High MIP" (south of it).
lab = M.find('b213836e'); corr = M.fo('eb10006b')
fp = footprint(M.byid[lab])
cy0, cy1 = corr['bbox'][1] / FT, corr['bbox'][4] / FT
north = fp.intersection(box(-1e5, cy1, 1e5, 1e5)); south = fp.intersection(box(-1e5, -1e5, 1e5, cy0))
b = [v / FT for v in M.byid[lab]['bbox']]
for g, nm in ((north, 'CORE LABS'), (south, 'QUANTUM HIGH MIP')):
    for nid in M.add_solid(g, b[2], b[5], 'PROGRAM::Lab', nm):
        names[nid] = nm
names.pop(lab, None)
M.retire(lab)
M.log.append(f"split {lab[:8]} at the corridor: CORE LABS {north.area:,.0f} sf / QUANTUM HIGH MIP {south.area:,.0f} sf")

# Double-height rooms ("Auditorium (Double height)", Prototyping open to L2 on the 2nd-floor page).
for short in ('a702526d', '84089f72'):
    spans[M.find(short)] = 'KeepTall'

# Terraces named on the pages keep their own program (Green Roof / Sky Garden, alias TERRACE) so the name shows
# instead of merging into the floor's circulation; the Micron Gallery (PROGRAM::INTERACTIVE) is Interaction / Collab.
progs = st.setdefault('objectPrograms', {})
for short in ('6bb0929e', '455cb12c', '5f9a295e'): progs[M.find(short)] = 'GREEN_ROOF'
progs[M.find('674abca0')] = 'INTERACTION'

# Open-plan rooms modelled as single surfaces on the slab (Circulation, Break Room): extruded slab top → ceiling of
# their storey, openings kept as holes, as in the 10-19 CAMPUS model; the surfaces go to LEFTOVERS.
STORIES = [(1.5, 20.5), (21.5, 40.5), (41.5, 56.5), (57.5, 72.5), (73.5, 92.5)]
# The named terraces become 1 ft blocks on their slab (a surface alone is drawn over by the storey's circulation),
# and the circulation of that storey stops at them.
TERR = collections.defaultdict(list)
for short in ('6bb0929e', '455cb12c', '5f9a295e'):
    oid = M.find(short); o = M.byid[oid]; z = o['bbox'][2] / FT
    g = footprint(o); TERR[min(STORIES, key=lambda s_: abs(s_[0] - z))].append(g)
    for nid in M.add_solid(g, z, z + 1.0, o['layer'], names[oid]): M.remap(oid, nid)
    M.retire(oid)
nsol = 0
for o in M.fx:
    b = [v / FT for v in o['bbox']]
    if o['layer'] not in ('PROGRAM::Circulation', 'PROGRAM::Break Room') or '#' in o['id'] or b[5] - b[2] > 1.0 or not o.get('mesh'): continue
    z0, z1 = min(STORIES, key=lambda s_: abs(s_[0] - b[2]))
    g = footprint(o)
    if g is None or g.area < 20: continue
    if TERR.get((z0, z1)): g = g.difference(unary_union(TERR[(z0, z1)]).buffer(0.05))
    ids = M.add_solid(g.buffer(0), z0, z1, o['layer'], o['name'])
    for nid in ids: M.remap(o['id'], nid)
    M.retire(o['id']); nsol += 1
M.log.append(f"open-plan rooms (single surfaces) extruded to their storey: {nsol}")

# ------------------------------------------------------------------ cleanup
def retire_all(ids, why):
    for i in ids: M.retire(i)
    M.log.append(f"leftovers: {len(ids)} {why}")

# exact duplicates (same layer-independent geometry / same block at the same transform)
seen, dups = {}, collections.defaultdict(list)
for x in M.m.Objects:
    g = x.Geometry; ot = str(g.ObjectType).split('.')[-1]
    if ot in ('Annotation', 'Detail', 'Light') or str(x.Attributes.ViewportId)[:8] != '00000000': continue
    L = M.m.Layers[x.Attributes.LayerIndex].FullPath
    if L.startswith('White Program For renders only'): continue        # render copies of the rooms, by design
    if ot == 'InstanceReference':
        t = g.Xform
        key = (ot, str(g.ParentIdefId), tuple(round(getattr(t, f"M{i}{j}"), 3) for i in range(4) for j in range(4)))
    else:
        bb = g.GetBoundingBox()
        key = (ot, tuple(round(v, 3) for v in (bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z)))
        if ot == 'Brep': key += (len(g.Faces),)
    if key in seen: dups[L].append(str(x.Attributes.Id))
    else: seen[key] = str(x.Attributes.Id)
for L, ids in dups.items(): retire_all(ids, f"exact duplicates on {L}")

retire_all([M.find('c54fc568')], "PROPERTY LINE surface at +213 ft over the building")
retire_all([M.find('32d6e5a6')], "Outdoor Circulation strip lying inside the L5 lab (not on the 5th-floor page)")
retire_all([M.find('d672cdb7')], "second Micron Gallery outline (curve under the solid)")

# L2 floor plate: one 1 sq ft face at (6531, 4724), 3,400 ft from the building.
fpid = M.find('5a04be12')
M.clip_mesh(fpid, box(2500, 1500, 4500, 3200))
M.log.append("L2 floor plate 5a04be12: stray face at (6531, 4724) dropped (plate rebuilt as a mesh, original to leftovers)")

# page-space arrows that sit on the PROGRAM layer (Ground page) → TEXT, where the rest of the page text is
text_i = M.layers['TEXT']; n = 0
for x in M.m.Objects:
    if M.m.Layers[x.Attributes.LayerIndex].FullPath == 'PROGRAM' and str(x.Attributes.ViewportId)[:8] != '00000000':
        x.Attributes.LayerIndex = text_i; n += 1
M.log.append(f"page arrows: {n} curves PROGRAM → TEXT")
st['layers']['PROGRAM'] = {"use": True, "role": "Ignore", "programKey": "", "mode": ""}

# section / elevation lines → DETAIL::CUTLINE; the plugin's lines re-read from the curves
M.role('DETAIL::CUTLINE', 'Ignore', mode='')
M.role('DETAIL', 'Ignore', mode='')
cut_i = M.layer('DETAIL::CUTLINE', (255, 0, 0)); moved = []
for o in M.fx:
    if o['layer'] == 'PROGRAM::INTERACTIVE' and o['type'] == 'PolylineCurve' and not o['isClosed']:
        M.obj[o['id']].Attributes.LayerIndex = cut_i; moved.append(o['id'])
for s in st.get('sections', []):
    o = M.byid.get(s.get('objectId'))
    if o and o.get('points'):
        p = o['points']; a, b_ = (p[0][0] / FT, p[0][1] / FT), (p[-1][0] / FT, p[-1][1] / FT)
        old = s['line']
        if (b_[0] - a[0]) * (old[2] - old[0]) + (b_[1] - a[1]) * (old[3] - old[1]) < 0: a, b_ = b_, a   # keep the picked direction
        s['line'] = [a[0], a[1], b_[0], b_[1]]
M.log.append(f"cut lines: {len(moved)} curves PROGRAM::INTERACTIVE → DETAIL::CUTLINE; sections "
             + ", ".join(f"{s['name']} {[round(v) for v in s['line']]}" for s in st.get('sections', [])))

# Levels at the slab tops (the plugin opens a storey's slab holes only within 1 ft of its level): L1 1'-6",
# L2 21'-6", L3 41'-6", L5 73'-6" were 1'-6" low, so the L3 courtyard read as indoor circulation.
SLAB = {'L1': 1.5, 'L2': 21.5, 'L3': 41.5, 'L4': 57.5, 'L5': 73.5}
for lv in st.get('levels', []):
    if lv['name'] in SLAB: lv['elevation'] = SLAB[lv['name']]
M.log.append("levels: " + ", ".join(f"{lv['name']} {lv['elevation']:.1f}" for lv in st['levels']))

# Axons from the south-west corner (user 10-23: NE looked from the opposite corner).
st['output']['axonView'] = 'SW'; st['output']['axonFromViewport'] = False; st['output']['axonViewDirection'] = None

# Parking-lot stall lines (SURROUNDING::PARK) were set as landscape and drew green: hardscape lines instead.
st['layers']['SURROUNDING::PARK'] = {"use": True, "role": "BG.Hardscape", "programKey": "", "mode": "line"}

M.write(sys.argv[3])
json.dump(st, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(M.log))

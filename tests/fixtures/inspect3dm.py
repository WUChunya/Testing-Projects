import sys, collections, rhino3dm as r
m = r.File3dm.Read(sys.argv[1])
print("units", m.Settings.ModelUnitSystem, "objs", len(m.Objects), "layers", len(m.Layers))
cnt=collections.Counter(); types=collections.defaultdict(collections.Counter); zs=collections.defaultdict(list); names=collections.defaultdict(set); colsrc=collections.defaultdict(collections.Counter)
for o in m.Objects:
    a=o.Attributes; g=o.Geometry
    cnt[a.LayerIndex]+=1; types[a.LayerIndex][type(g).__name__]+=1
    try: bb=g.GetBoundingBox()
    except TypeError: continue
    zs[a.LayerIndex].append((round(bb.Min.Z,2),round(bb.Max.Z,2)))
    if a.Name: names[a.LayerIndex].add(a.Name)
    c=a.ObjectColor; colsrc[a.LayerIndex][(str(a.ColorSource).split('.')[-1], '#%02X%02X%02X'%(c[0],c[1],c[2]))]+=1
for i,l in enumerate(m.Layers):
    c=l.Color
    print(f"[{i}] {l.FullPath!r} vis={l.Visible} color=#{c[0]:02X}{c[1]:02X}{c[2]:02X} n={cnt[i]} {dict(types[i])}")
    if cnt[i]:
        z=zs[i]; print("     zmin set:", sorted(set(a for a,b in z))[:15], " zmax max:", max(b for a,b in z))
        print("     colors:", dict(colsrc[i].most_common(5)))
        if names[i]: print("     names:", list(names[i])[:10])

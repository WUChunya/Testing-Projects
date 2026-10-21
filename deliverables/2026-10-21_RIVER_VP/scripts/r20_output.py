"""Output settings for the RIVER 1020 plans: 11x17 Landscape, 1" = 100'-0" (sections 1" = 50'-0"), centred on MX3.
Usage: python3 -I r20_output.py <in.3dm> <out.3dm> <export settings.json>  (the export JSON draws SitePlan + Plan only)"""
import json, sys
import rhino3dm as r
m = r.File3dm.Read(sys.argv[1]); K = 'ProgramDiagram\\settings'; s = json.loads(m.Strings[K])
s['output'].update({'artboard': '11x17 Landscape', 'scale': '1" = 100\'-0"', 'sectionScale': '1" = 50\'-0"', 'lastFolder': ''})
m.Strings[K] = json.dumps(s); m.Write(sys.argv[2], 8)
s['output'] = dict(s['output'], drawings=['SitePlan', 'Plan'])
json.dump(s, open(sys.argv[3], 'w'), indent=1)

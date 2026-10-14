CLS = ['LAB_SUPPORT', 'LAB', 'OFFICE', 'CONFERENCE', 'LOBBY', 'CIRCULATION', 'ASSEMBLY', 'SCREEN', 'BUILDING_SUPPORT', 'CORE', 'SLAB', 'GREEN']
LAYER = {'PROGRAM::Lab': 'LAB', 'PROGRAM::Lab Support': 'LAB_SUPPORT', 'PROGRAM::Enclosed Office': 'OFFICE', 'PROGRAM::Conference': 'CONFERENCE',
         'PROGRAM::Break Room': 'LOBBY', 'PROGRAM::Circulation': 'CIRCULATION', 'PROGRAM::Seminar Room': 'ASSEMBLY', 'PROGRAM::Shell Space': 'SCREEN',
         'PROGRAM::Support': 'BUILDING_SUPPORT', 'PROGRAM::Core': 'CORE', 'FLOOR PLATE': 'SLAB', 'PROGRAM::Green Roof': 'GREEN'}
def color(c):
    i = CLS.index(c) + 1
    return (i * 20, 255 - i * 20, (i * 67) % 256)
def hexc(c): return '#%02X%02X%02X' % color(c)
def of_name(n):
    if 'EXPOSED_ROOF' in n or 'PLATE' in n or 'SLAB' in n: return 'SLAB'
    if 'SCREEN' in n: return 'SCREEN'
    for c in CLS:
        if '_' + c + '_' in n + '_': return c
    return None

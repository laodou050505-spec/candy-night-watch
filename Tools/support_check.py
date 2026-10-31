"""支撑检查（Blender 里运行）：Blender -b -P Tools/support_check.py -- <装配.fbx> [前缀 Candy_]
对每个指定前缀的实例，求它的顶点到其它所有物体表面的最近距离；> 2mm 说明悬空（没有东西托着）。"""
import bpy, bmesh, sys
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
fp = argv[0]; pre = argv[1] if len(argv) > 1 else 'Candy_'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fp)
ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
def bvh(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world); t = BVHTree.FromBMesh(bm); bm.free(); return t
T = {o.name: bvh(o) for o in ms}
for o in ms:
    if not o.name.startswith(pre): continue
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    best, who = 9, ''
    for n, t in T.items():
        if n == o.name: continue
        for v in vs[::3]:
            r = t.find_nearest(v)
            if r[0] is not None and r[3] < best: best, who = r[3], n
    print('SUPPORT', o.name, round(best * 1000, 1), 'mm', who, 'FLOAT' if best > 0.002 else '')

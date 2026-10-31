"""把模型顶面调平（在 Blender 里运行）：Blender -b -P Tools/level_top.py -- <in.glb> <out.glb> [顶部比例=0.12]
取高度最高的那一层顶点（z > max - 比例×高度），最小二乘拟合平面，把平面法线旋到 +Z。贴图/UV 不变。"""
import bpy, sys, numpy as np
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, dst = argv[0], argv[1]; frac = float(argv[2]) if len(argv) > 2 else 0.12
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=src)
ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
pts = np.array([list(o.matrix_world @ v.co) for o in ms for v in o.data.vertices])
zmax, zmin = pts[:, 2].max(), pts[:, 2].min()
top = pts[pts[:, 2] > zmax - frac * (zmax - zmin)]
# 只取水平半径的内 85%（排除岛沿草丛、蘑菇）
c = top[:, :2].mean(0); rr = np.linalg.norm(top[:, :2] - c, axis=1); top = top[rr < np.percentile(rr, 85)]
A = np.c_[top[:, 0], top[:, 1], np.ones(len(top))]
(a, b, d), *_ = np.linalg.lstsq(A, top[:, 2], rcond=None)
n = Vector((-a, -b, 1)).normalized()
q = n.rotation_difference(Vector((0, 0, 1)))
import math
print('LEVEL tilt deg', round(math.degrees(n.angle(Vector((0, 0, 1)))), 2), 'samples', len(top))
for o in ms:
    o.matrix_world = q.to_matrix().to_4x4() @ o.matrix_world
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB')

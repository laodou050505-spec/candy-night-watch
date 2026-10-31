"""糖果桶内的糖果垫层（Blender 里运行）：
Blender -b -P Tools/blender_candybed.py -- <Desk_Bucket.fbx> <顶面高度 m> <输出目录>
桶里装满几百颗独立糖果太重，所以底下用一个"糖堆垫层"把内腔填到指定高度，上面再放可拿走的独立糖果。
做法：在桶壳内腔沿高度逐层水平射线测内壁半径 → 车削成贴合内壁（留 3mm 间隙）的封闭体 → 顶面加起伏
→ 平面投影 UV + 程序化"糖果堆"贴图（随机糖块、条纹、高光）。"""
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, top, OUT = argv[0], float(argv[1]), argv[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
sh = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('Shell')][0]
bm = bmesh.new(); bm.from_mesh(sh.data); bm.transform(sh.matrix_world); T = BVHTree.FromBMesh(bm)
# 内腔底：从顶面高度向下打射线
hit = T.ray_cast(Vector((0, 0, top)), Vector((0, 0, -1)))
zb = hit[0].z + 0.002
print('BED bottom', round(zb, 4))
prof = []
NZ = 14
for k in range(NZ + 1):
    z = zb + (top - zb) * k / NZ
    ds = []
    for a in range(24):
        ang = 2 * math.pi * a / 24
        h = T.ray_cast(Vector((0, 0, z)), Vector((math.cos(ang), math.sin(ang), 0)), 0.3)
        if h[0] is not None: ds.append(h[3])
    r = (np.percentile(ds, 10) if ds else 0.05) - 0.006     # 避开雕刻孔方向，取偏小值
    prof.append((max(r, 0.004), z))
print('BED profile', [(round(r, 3), round(z, 3)) for r, z in prof])
# 车削：底面中心 → 侧壁 → 顶面（顶面带起伏）
SEG = 48
b = bmesh.new()
rings = []
for r, z in prof:
    rings.append([b.verts.new((r * math.cos(2 * math.pi * i / SEG), r * math.sin(2 * math.pi * i / SEG), z)) for i in range(SEG)])
# 顶面同心环
r_top = prof[-1][0]
for k in range(1, 6):
    rr = r_top * (1 - k / 6)
    ring = []
    for i in range(SEG):
        ang = 2 * math.pi * i / SEG
        x, y = rr * math.cos(ang), rr * math.sin(ang)
        bump = 0.006 * (1 - (rr / r_top) ** 2) + 0.004 * noise.noise(Vector((x * 60, y * 60, 3.1)))
        ring.append(b.verts.new((x, y, top + bump)))
    rings.append(ring)
c_top = b.verts.new((0, 0, top + 0.007)); c_bot = b.verts.new((0, 0, zb))
for a_, b_ in zip(rings[:-1], rings[1:]):
    for i in range(SEG):
        b.faces.new((a_[i], a_[(i + 1) % SEG], b_[(i + 1) % SEG], b_[i]))
for i in range(SEG):
    b.faces.new((rings[-1][i], rings[-1][(i + 1) % SEG], c_top))
    b.faces.new((rings[0][(i + 1) % SEG], rings[0][i], c_bot))
bmesh.ops.recalc_face_normals(b, faces=b.faces)
bmesh.ops.triangulate(b, faces=b.faces)
bad = sum(1 for e in b.edges if len(e.link_faces) != 2)
assert bad == 0, f'Candy_Bed 拓扑不合格 {bad}'
me = bpy.data.meshes.new('Candy_Bed'); b.to_mesh(me); b.free()
bed = bpy.data.objects.new('Candy_Bed', me); bpy.context.scene.collection.objects.link(bed)
# 平面投影 UV（俯视）
R = max(r for r, _ in prof)
uvl = me.uv_layers.new(name='UVMap').data
for poly in me.polygons:
    for li in poly.loop_indices:
        co = me.vertices[me.loops[li].vertex_index].co
        if poly.normal.z > 0.5:
            uvl[li].uv = (co.x / R * 0.5 + 0.5, co.y / R * 0.5 + 0.5)
        else:   # 侧面用柱面展开，避免俯视投影在侧壁上拉成竖条
            uvl[li].uv = ((math.atan2(co.y, co.x) / (2 * math.pi)) % 1.0, (co.z - zb) / (top - zb) * 0.35)
# 程序化糖果堆贴图
N = 1024
rng = random.Random(7)
img = np.zeros((N, N, 3), np.float32); img[:] = (0.35, 0.12, 0.25)
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
pal = [(1.0, 0.5, 0.08), (0.55, 0.18, 0.75), (0.35, 0.85, 0.25), (1.0, 0.85, 0.2), (0.95, 0.35, 0.55), (0.95, 0.93, 0.9)]
for _ in range(420):
    cx, cy = rng.uniform(0, N), rng.uniform(0, N)
    rx_, ry_ = rng.uniform(16, 38), rng.uniform(12, 30)
    th = rng.uniform(0, math.pi)
    dx, dy = xx - cx, yy - cy
    u = (dx * math.cos(th) + dy * math.sin(th)) / rx_; v = (-dx * math.sin(th) + dy * math.cos(th)) / ry_
    d = u * u + v * v
    m = d < 1
    if not m.any(): continue
    col = np.array(pal[rng.randrange(len(pal))], np.float32)
    shade = (1.0 - 0.45 * d[m])[:, None]           # 球面明暗
    c = col[None] * shade
    if rng.random() < 0.35:                         # 条纹糖
        stripe = (np.sin((u[m] + v[m]) * 9) > 0)[:, None]
        c = np.where(stripe, c, np.array([0.97, 0.95, 0.92])[None] * shade)
    hl = np.clip(1 - ((u[m] + 0.35) ** 2 + (v[m] + 0.35) ** 2) * 6, 0, 1)[:, None] * 0.5   # 高光
    img[m] = np.clip(c + hl, 0, 1)
rgba = np.concatenate([img, np.ones((N, N, 1), np.float32)], -1)
im = bpy.data.images.new('Candy_Bed_albedo', N, N, alpha=False); im.pixels = rgba.ravel()
os.makedirs(OUT, exist_ok=True)
im.filepath_raw = os.path.join(OUT, 'Candy_Bed_albedo.png'); im.file_format = 'PNG'; im.save()
bpy.ops.object.select_all(action='DESELECT'); bed.select_set(True); bpy.context.view_layer.objects.active = bed
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Candy_Bed.fbx'), use_selection=True, axis_forward='-Z', axis_up='Y',
                         object_types={'MESH'}, mesh_smooth_type='FACE', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL')
print('BED done faces', len(me.polygons))

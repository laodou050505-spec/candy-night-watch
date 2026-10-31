"""雕刻南瓜灯的发光遮罩（Blender 里运行）：
  Blender -b -P Tools/carve_glow_mask.py -- <LOD0.fbx> <albedo.png> <输出 emis.png> [凹陷阈值比例=0.035] [预览.png]
思路：雕出来的眼睛/鼻子/嘴是“凹进去”的。对每个顶点算它到南瓜中心的距离 r，
再算同一方向角附近（球面分箱）的外包络 r_env（附近顶点 r 的 92 分位）；凹陷深度 = r_env - r。
凹陷 > 阈值（× 南瓜尺寸）且位于南瓜主体（排除顶部茎叶）的顶点记为发光，写进顶点色，
再用模型自身 UV 把顶点色烘焙成贴图（Cycles EMIT 自烘焙），做两次模糊让边缘柔和。"""
import bpy, bmesh, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, albedo, out = argv[0], argv[1], argv[2]
thr = float(argv[3]) if len(argv) > 3 else 0.035
preview = argv[4] if len(argv) > 4 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
me = ob.data
V = np.array([ob.matrix_world @ v.co for v in me.vertices])
lo, hi = V.min(0), V.max(0)
size = float((hi - lo).max())
# 南瓜主体中心：排除顶部 25%（茎叶）后取包围盒中心
body = V[V[:, 2] < lo[2] + (hi[2] - lo[2]) * 0.75]
c = (body.min(0) + body.max(0)) / 2
d = V - c
r = np.linalg.norm(d, axis=1)
th = np.arccos(np.clip(d[:, 2] / np.maximum(r, 1e-9), -1, 1))   # 0..pi
ph = np.arctan2(d[:, 1], d[:, 0])                                # -pi..pi
NT, NP = 36, 72
it = np.clip((th / math.pi * NT).astype(int), 0, NT - 1)
ip = np.clip(((ph + math.pi) / (2 * math.pi) * NP).astype(int), 0, NP - 1)
env = np.zeros((NT, NP))
bins = {}
for k in range(len(V)):
    bins.setdefault((it[k], ip[k]), []).append(r[k])
for (a, b), rs in bins.items():
    env[a, b] = np.percentile(rs, 92)
# 外包络在邻域内取最大（3×5 邻域），避免分箱太小
env2 = np.zeros_like(env)
for a in range(NT):
    for b in range(NP):
        aa = slice(max(0, a - 1), min(NT, a + 2))
        win = np.concatenate([env[aa, (b + k) % NP] for k in (-2, -1, 0, 1, 2)])
        env2[a, b] = win.max()
recess = env2[it, ip] - r
# 雕脸只在正面（模型 +X 半球，导入 Blender 后的方向；实测 Unity 里雕脸朝模型 -X）
front = (d[:, 0] / np.maximum(r, 1e-9)) > 0.25
glow = (recess > thr * size) & (V[:, 2] < lo[2] + (hi[2] - lo[2]) * 0.8) & front
# 只保留成团的雕刻区：发光顶点的邻居里至少一半也发光（去掉南瓜棱沟里的零星点）
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
g2 = glow.copy()
for _ in range(2):
    nxt = g2.copy()
    for v in bm.verts:
        if not g2[v.index]: continue
        nb = [e.other_vert(v).index for e in v.link_edges]
        if nb and sum(g2[i] for i in nb) < len(nb) * 0.5: nxt[v.index] = False
    g2 = nxt
# 再向外扩一圈（雕刻侧壁也发光）
g3 = g2.copy()
for v in bm.verts:
    if g2[v.index]:
        for e in v.link_edges: g3[e.other_vert(v).index] = True
bm.free()
inten = np.clip((recess - thr * size * 0.5) / (thr * size * 1.5), 0, 1) * g3
print('GLOW verts', int(g3.sum()), 'of', len(V), 'size', round(size, 3))

ca = me.color_attributes.new('glow', 'FLOAT_COLOR', 'POINT')
for i, d_ in enumerate(ca.data):
    k = float(inten[i])
    d_.color = (k, k * 0.62, k * 0.18, 1)
mat = bpy.data.materials.new('bake'); mat.use_nodes = True
nt = mat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'glow'
em = nt.nodes.new('ShaderNodeEmission'); outn = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(attr.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], outn.inputs[0])
img = bpy.data.images.new('emis', 2048, 2048, alpha=False)
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img; nt.nodes.active = tn
me.materials.clear(); me.materials.append(mat)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
sc.render.bake.margin = 6
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
bpy.ops.object.bake(type='EMIT')
img.filepath_raw = out; img.file_format = 'PNG'; img.save()
print('MASK', out)
if preview:
    # 预览：原贴图 + 发光叠加，从正面渲染
    img2 = bpy.data.images.load(albedo)
    for n in list(nt.nodes): nt.nodes.remove(n)
    t1 = nt.nodes.new('ShaderNodeTexImage'); t1.image = img2
    t2 = nt.nodes.new('ShaderNodeTexImage'); t2.image = img
    add = nt.nodes.new('ShaderNodeMixRGB'); add.blend_type = 'SCREEN'; add.inputs[0].default_value = 1
    em = nt.nodes.new('ShaderNodeEmission'); outn = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(t1.outputs[0], add.inputs[1]); nt.links.new(t2.outputs[0], add.inputs[2])
    nt.links.new(add.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], outn.inputs[0])
    sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = sc.render.resolution_y = 600
    from mathutils import Vector
    cam = bpy.data.objects.new('c', bpy.data.cameras.new('c')); sc.collection.objects.link(cam); sc.camera = cam
    cc = Vector(c.tolist())
    shots = []
    for i, dv in enumerate([(-1, 0, 0.1), (0, -1, 0.1), (1, 0, 0.1), (0, 1, 0.1)]):
        cam.location = cc + Vector(dv).normalized() * size * 2.2
        cam.rotation_euler = (cc - cam.location).to_track_quat('-Z', 'Y').to_euler()
        fp = preview.replace('.png', f'_{i}.png'); sc.render.filepath = fp; bpy.ops.render.render(write_still=True)
    print('PREVIEW done')

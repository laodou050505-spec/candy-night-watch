"""桌面一套的程序化部件（在 Blender 里运行）：Blender -b -P Tools/blender_deskparts.py -- <输出目录>
全部是封闭流形实体（导出前自检，不合格直接报错退出），带 UV；需要贴图的烘焙出 PNG。
  Top_Wood        桌板：半径 1.15、厚 6cm 的圆木板，边缘倒圆；顶面年轮+木纹贴图（程序纹理烘焙）
  Bucket_Handle   糖桶提手：黑铁弧形提手，两端带小环
  Cauldron_Ring   坩埚侧面拉环（圆环）
  Gem_Hex         坩埚宝石（六边形刻面宝石，颜色在 Unity 里给）
  Potion_Surface  坩埚汤药液面（中间略鼓的圆盘，Unity 里用动态着色器冒泡）
  Candle_Dish     烛簇底盘（浅碟，带翻边）
单位米，Blender Z 向上；导出 FBX 转 Unity Y 向上。"""
import bpy, bmesh, math, os, sys, json
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]
os.makedirs(OUT, exist_ok=True)
report = {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def check(bm, name):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bd = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    nm = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    report[name] = dict(faces=len(bm.faces), boundary=bd, nonmanifold=nm)
    if bd or nm:
        raise SystemExit(f'{name} 拓扑不合格: boundary={bd} nonmanifold={nm}')


def to_obj(bm, name, smooth=True):
    check(bm, name)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons: p.use_smooth = smooth
    return ob


def lathe(profile, seg, name, smooth=True):
    """绕 Z 轴车削封闭轮廓（profile: [(r, z)]，首尾都在轴上 r=0 → 封闭实体）"""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((math.cos(i / seg * math.tau) * r, math.sin(i / seg * math.tau) * r, z)) for i in range(seg)])
    for a, b in zip(rings[:-1], rings[1:]):
        if len(a) == 1 and len(b) == 1: continue
        if len(a) == 1:
            for i in range(seg): bm.faces.new((a[0], b[(i + 1) % seg], b[i]))
        elif len(b) == 1:
            for i in range(seg): bm.faces.new((a[i], a[(i + 1) % seg], b[0]))
        else:
            for i in range(seg): bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    return bm


def uv_planar(ob, size):
    me = ob.data
    if not me.uv_layers: me.uv_layers.new(name='UVMap')
    uv = me.uv_layers.active.data
    for p in me.polygons:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = (co.x / size * 0.5 + 0.5, co.y / size * 0.5 + 0.5)


def uv_smart(ob):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')


def export(ob, fname):
    bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(ob.data); bm.free()
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, fname), use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_space_transform=True)


# ---------------- 桌板 ----------------
reset()
R, T, BEV = 1.13, 0.06, 0.018
prof = [(0, -T)]
for i in range(7):   # 底边倒圆
    a = math.pi / 2 * i / 6
    prof.append((R - BEV + math.sin(a) * BEV, -T + BEV - math.cos(a) * BEV))
for i in range(7):   # 顶边倒圆
    a = math.pi / 2 * i / 6
    prof.append((R - BEV + math.cos(a) * BEV, -BEV + math.sin(a) * BEV))
# 顶面同心细分（让木纹烘焙/卡通光照更细）
for k in range(1, 12):
    prof.append(((R - BEV) * (1 - k / 12), 0.0))
prof.append((0, 0.0))
bm = lathe(prof, 128, 'Top_Wood')
top = to_obj(bm, 'Top_Wood')
uv_planar(top, R)
# 程序木纹：年轮（以略偏心的圆心）+ 纤维噪声 + 板缝（5 块拼板）
mat = bpy.data.materials.new('Top_Wood'); mat.use_nodes = True; nt = mat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
tc = nt.nodes.new('ShaderNodeTexCoord')
sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['UV'], sep.inputs[0])
# 板缝：沿 U 方向 5 条
wave = nt.nodes.new('ShaderNodeTexWave'); wave.wave_type = 'BANDS'; wave.bands_direction = 'X'; wave.inputs['Scale'].default_value = 2.5
wave.inputs['Distortion'].default_value = 6; wave.inputs['Detail'].default_value = 4; wave.inputs['Detail Scale'].default_value = 1.5
nt.links.new(tc.outputs['UV'], wave.inputs['Vector'])
noi = nt.nodes.new('ShaderNodeTexNoise'); noi.inputs['Scale'].default_value = 60; noi.inputs['Detail'].default_value = 6
mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1, 18, 1)
nt.links.new(tc.outputs['UV'], mp.inputs[0]); nt.links.new(mp.outputs[0], noi.inputs['Vector'])
mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'FLOAT'; mix.inputs['Factor'].default_value = 0.45
nt.links.new(wave.outputs['Fac'], mix.inputs['A']); nt.links.new(noi.outputs['Fac'], mix.inputs['B'])
ramp = nt.nodes.new('ShaderNodeValToRGB')
ramp.color_ramp.elements[0].position = 0.25; ramp.color_ramp.elements[0].color = (0.34, 0.18, 0.09, 1)
ramp.color_ramp.elements[1].position = 0.85; ramp.color_ramp.elements[1].color = (0.66, 0.42, 0.22, 1)
nt.links.new(mix.outputs['Result'], ramp.inputs[0])
# 板缝深线
seam = nt.nodes.new('ShaderNodeMath'); seam.operation = 'FRACT'
mul = nt.nodes.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = 5
nt.links.new(sep.outputs['X'], mul.inputs[0]); nt.links.new(mul.outputs[0], seam.inputs[0])
cmpv = nt.nodes.new('ShaderNodeMath'); cmpv.operation = 'COMPARE'; cmpv.inputs[1].default_value = 0.0; cmpv.inputs[2].default_value = 0.02
nt.links.new(seam.outputs[0], cmpv.inputs[0])
mixc = nt.nodes.new('ShaderNodeMix'); mixc.data_type = 'RGBA'
nt.links.new(cmpv.outputs[0], mixc.inputs['Factor']); nt.links.new(ramp.outputs[0], mixc.inputs['A'])
mixc.inputs['B'].default_value = (0.22, 0.11, 0.05, 1)
em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(mixc.outputs['Result'], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])
img = bpy.data.images.new('Top_Wood_albedo', 2048, 2048, alpha=False)
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img; nt.nodes.active = tn
top.data.materials.append(mat)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
bpy.ops.object.select_all(action='DESELECT'); top.select_set(True); bpy.context.view_layer.objects.active = top
bpy.ops.object.bake(type='EMIT')
img.filepath_raw = os.path.join(OUT, 'Top_Wood_albedo.png'); img.file_format = 'PNG'; img.save()
export(top, 'Top_Wood.fbx')

# ---------------- 糖桶提手 ----------------
reset()
cu = bpy.data.curves.new('h', 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 0.0045; cu.bevel_resolution = 3; cu.use_fill_caps = True
sp = cu.splines.new('POLY'); n = 40; sp.points.add(n - 1)
W, H = 0.105, 0.11      # 半宽、拱高（相对桶口）
for i in range(n):
    t = i / (n - 1); a = math.pi * t
    sp.points[i].co = (-math.cos(a) * W, 0, math.sin(a) * H, 1)
o = bpy.data.objects.new('Bucket_Handle', cu); bpy.context.scene.collection.objects.link(o)
bpy.context.view_layer.objects.active = o; o.select_set(True); bpy.ops.object.convert(target='MESH')
# 两端小环（扣在桶沿上）
parts = [o]
for sx in (-1, 1):
    b2 = bmesh.new(); bmesh.ops.create_cone(b2, cap_ends=True, segments=16, radius1=0.009, radius2=0.009, depth=0.006)
    bmesh.ops.rotate(b2, verts=b2.verts, matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
    bmesh.ops.translate(b2, verts=b2.verts, vec=(sx * (W + 0.001), 0, 0))
    me = bpy.data.meshes.new('r'); b2.to_mesh(me); b2.free()
    r = bpy.data.objects.new('r', me); bpy.context.scene.collection.objects.link(r); parts.append(r)
bpy.ops.object.select_all(action='DESELECT')
for p in parts: p.select_set(True)
bpy.context.view_layer.objects.active = o; bpy.ops.object.join()
bm = bmesh.new(); bm.from_mesh(o.data); check(bm, 'Bucket_Handle'); bm.to_mesh(o.data); bm.free()
uv_smart(o); export(o, 'Bucket_Handle.fbx')

# ---------------- 坩埚拉环 ----------------
reset()
bm = bmesh.new()
seg, rs = 32, 12
Rr, rr = 0.022, 0.0045
vs = [[bm.verts.new(((Rr + rr * math.cos(b / rs * math.tau)) * math.cos(a / seg * math.tau), rr * math.sin(b / rs * math.tau),
                     (Rr + rr * math.cos(b / rs * math.tau)) * math.sin(a / seg * math.tau))) for b in range(rs)] for a in range(seg)]
for a in range(seg):
    for b in range(rs):
        bm.faces.new((vs[a][b], vs[(a + 1) % seg][b], vs[(a + 1) % seg][(b + 1) % rs], vs[a][(b + 1) % rs]))
o = to_obj(bm, 'Cauldron_Ring'); uv_smart(o); export(o, 'Cauldron_Ring.fbx')

# ---------------- 宝石 ----------------
reset()
bm = bmesh.new()
n = 6; rw, h1, h2, tb = 0.022, 0.006, 0.014, 0.012   # 腰半径、上冠高、下亭深、台面半径
top_c = bm.verts.new((0, 0, h1 + 0.002))
table = [bm.verts.new((math.cos(i / n * math.tau) * tb, math.sin(i / n * math.tau) * tb, h1)) for i in range(n)]
girdle = [bm.verts.new((math.cos((i + 0.5) / n * math.tau) * rw, math.sin((i + 0.5) / n * math.tau) * rw, 0)) for i in range(n)]
cul = bm.verts.new((0, 0, -h2))
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((top_c, table[i], table[j]))
    bm.faces.new((table[i], girdle[i], table[j]))
    bm.faces.new((girdle[i], girdle[j], table[j]))
    bm.faces.new((girdle[j], girdle[i], cul))
o = to_obj(bm, 'Gem_Hex', smooth=False); uv_smart(o); export(o, 'Gem_Hex.fbx')

# ---------------- 汤药液面 ----------------
reset()
prof = [(0, -0.004)] + [(0.085 * k / 10, -0.004) for k in range(1, 11)] + [(0.085, 0.0)] + [(0.085 * (1 - k / 10), 0.006 * math.sin(math.pi / 2 * k / 10)) for k in range(1, 10)] + [(0, 0.006)]
bm = lathe(prof, 64, 'Potion_Surface')
o = to_obj(bm, 'Potion_Surface'); uv_planar(o, 0.085); export(o, 'Potion_Surface.fbx')

# ---------------- 烛簇底盘 ----------------
reset()
prof = [(0, 0)] + [(0.13, 0), (0.15, 0.004), (0.158, 0.014), (0.155, 0.024), (0.147, 0.026), (0.142, 0.018), (0.13, 0.012)] + [(0.13 * (1 - k / 8), 0.012) for k in range(1, 8)] + [(0, 0.012)]
bm = lathe(prof, 64, 'Candle_Dish')
for v in bm.verts:   # 手工感：边沿轻微起伏
    v.co.z += noise.noise(v.co * 25) * 0.0015 * (v.co.length / 0.15)
o = to_obj(bm, 'Candle_Dish'); uv_smart(o); export(o, 'Candle_Dish.fbx')

# ---------------- 坩埚锅身（车削：外壁 → 翻卷锅沿 → 内壁 → 内底，一圈封闭实体，真正中空）----------------
reset()
RB, HB, WALL = 0.11, 0.13, 0.007     # 最大半径、锅身高（不含脚）、壁厚


def catmull(pts, n):
    out = []
    P = [pts[0]] + pts + [pts[-1]]
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = [Vector(x) for x in P[i - 1:i + 3]]
        for k in range(n):
            t = k / n
            out.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)))
    out.append(tuple(pts[-1]))
    return out


# 外轮廓（归一化 r/RB, z/HB）：圆鼓腹 → 收颈 → 锅口
OUT_CTRL = [(0.0, 0.0), (0.42, 0.015), (0.76, 0.1), (0.95, 0.28), (1.0, 0.47), (0.95, 0.66), (0.84, 0.81), (0.79, 0.9), (0.8, 0.95)]
outer = [(r * RB, z * HB) for r, z in catmull(OUT_CTRL, 6)]
r_m, z_m = outer[-1]
# 厚卷边：圆心 (r_m, z_m + L)、半径 L，从外侧下方绕过顶部到内侧（270°→-90° 走上半圈外翻）
L = 0.011
roll = []
for k in range(1, 19):
    ang = -math.pi / 2 + math.pi * 1.5 * k / 18 * -1 + math.pi * 1.0 * 0   # 占位，下面重新算
for k in range(1, 19):
    ang = math.radians(-90 + 270 * k / 18)    # -90°(下) → 0°(外) → 90°(顶) → 180°(内)
    roll.append((r_m + math.cos(ang) * L, z_m + L + math.sin(ang) * L))
# 内壁：外轮廓内缩 WALL（锅口段从卷边内侧下来），锅底保留厚度
inner = [(max(r - WALL * 1.3, 0.003), max(z + WALL * 0.4, WALL * 1.6)) for r, z in reversed(outer[1:-1])]
prof = [(0.0, 0.0)] + outer[1:] + roll + [(r_m - L - WALL * 0.2, z_m)] + inner + [(0.0, WALL * 1.6)]
bm = lathe(prof, 72, 'Cauldron_Body')
for v in bm.verts:   # 锤打感：外表面轻微凹凸
    if math.hypot(v.co.x, v.co.y) > 0.02:
        v.co += Vector((v.co.x, v.co.y, 0)).normalized() * noise.noise(v.co * 60) * 0.0012
ob = to_obj(bm, 'Cauldron_Body'); uv_smart(ob)
mat = bpy.data.materials.new('Cauldron_Body'); mat.use_nodes = True; nt = mat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
geo = nt.nodes.new('ShaderNodeNewGeometry')
vor = nt.nodes.new('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 90
noi = nt.nodes.new('ShaderNodeTexNoise'); noi.inputs['Scale'].default_value = 40
nt.links.new(geo.outputs['Position'], vor.inputs['Vector']); nt.links.new(geo.outputs['Position'], noi.inputs['Vector'])
mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'FLOAT'; mx.inputs['Factor'].default_value = 0.5
nt.links.new(vor.outputs['Distance'], mx.inputs['A']); nt.links.new(noi.outputs['Fac'], mx.inputs['B'])
ramp = nt.nodes.new('ShaderNodeValToRGB')
ramp.color_ramp.elements[0].color = (0.07, 0.06, 0.09, 1); ramp.color_ramp.elements[1].color = (0.22, 0.2, 0.26, 1)
nt.links.new(mx.outputs['Result'], ramp.inputs[0])
em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(ramp.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])
img = bpy.data.images.new('Cauldron_Body_albedo', 1024, 1024, alpha=False)
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img; nt.nodes.active = tn
ob.data.materials.append(mat)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
bpy.ops.object.bake(type='EMIT')
img.filepath_raw = os.path.join(OUT, 'Cauldron_Body_albedo.png'); img.file_format = 'PNG'; img.save()
export(ob, 'Cauldron_Body.fbx')

# ---------------- 桌沿南瓜瓣圈（瓣状圆环：截面椭圆，沿环向按瓣数起伏，瓣与瓣之间收成沟）----------------
reset()
RC, NL = 1.19, 30          # 管中心半径、瓣数
TR, TH = 0.075, 0.065      # 截面径向半径、竖向半径
SEG_U, SEG_V = NL * 12, 28
bm = bmesh.new()
grid = []
for i in range(SEG_U):
    u = i / SEG_U * math.tau
    lobe = 0.5 + 0.5 * math.cos(u * NL)              # 1 = 瓣中央，0 = 沟
    k = 0.78 + 0.22 * lobe ** 0.6                     # 沟处截面收窄
    row = []
    for j in range(SEG_V):
        v = j / SEG_V * math.tau
        r = RC + math.cos(v) * TR * k
        z = math.sin(v) * TH * (0.9 + 0.1 * lobe)
        row.append(bm.verts.new((math.cos(u) * r, math.sin(u) * r, z)))
    grid.append(row)
uvl_ = bm.loops.layers.uv.new('UVMap')
for i in range(SEG_U):
    for j in range(SEG_V):
        a, b = grid[i][j], grid[(i + 1) % SEG_U][j]
        c, d = grid[(i + 1) % SEG_U][(j + 1) % SEG_V], grid[i][(j + 1) % SEG_V]
        f = bm.faces.new((a, b, c, d))
        # UV 按网格索引给（接缝处用 i+1 / j+1，不回绕），U 方向分 4 段铺满贴图（每段 NL/4 个瓣）
        for l, (ii, jj) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
            l[uvl_].uv = (ii / SEG_U, jj / SEG_V)
rim = to_obj(bm, 'Table_Rim')
# 贴图：按顶点算颜色（瓣中央亮橙、沟深橙褐、顶部受光亮、底部暗，加细纵纹噪声），写顶点色再烘焙到 UV
ca = rim.data.color_attributes.new('col', 'FLOAT_COLOR', 'POINT')
dark, mid, light = Vector((0.42, 0.11, 0.02)), Vector((0.85, 0.32, 0.05)), Vector((1.0, 0.6, 0.16))
for vi, vtx in enumerate(rim.data.vertices):
    co = vtx.co
    u = math.atan2(co.y, co.x)
    lobe = 0.5 + 0.5 * math.cos(u * NL)
    vv = math.atan2(co.z, math.hypot(co.x, co.y) - RC)          # -pi..pi，pi/2 = 顶
    up_ = 0.5 + 0.5 * math.sin(vv)
    stripe = noise.noise(Vector((u * 40, co.z * 30, 0))) * 0.12
    t = max(0.0, min(1.0, lobe ** 0.7 * 0.65 + up_ * 0.45 - 0.15 + stripe))
    c = dark.lerp(mid, min(1, t * 1.6)) if t < 0.62 else mid.lerp(light, (t - 0.62) / 0.38)
    ca.data[vi].color = (c.x, c.y, c.z, 1)
mat = bpy.data.materials.new('Table_Rim'); mat.use_nodes = True; nt = mat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'col'
em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(attr.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])
img = bpy.data.images.new('Table_Rim_albedo', 2048, 512, alpha=False)
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img; nt.nodes.active = tn
rim.data.materials.append(mat)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
bpy.ops.object.select_all(action='DESELECT'); rim.select_set(True); bpy.context.view_layer.objects.active = rim
bpy.ops.object.bake(type='EMIT')
img.filepath_raw = os.path.join(OUT, 'Table_Rim_albedo.png'); img.file_format = 'PNG'; img.save()
export(rim, 'Table_Rim.fbx')

# ---------------- 哥特铁烛台框架（车削底座/立柱/烛杯 + 曲线圆管扫掠的 S 形侧臂与卷草）----------------
# 每个部件各自封闭，彼此搭接（不合并），导出为一个物体的多个连通块；尺寸：总高 0.30
reset()
pieces = []
def lathe_obj(profile, seg, name):
    bm_ = lathe(profile, seg, name); check(bm_, name)
    me_ = bpy.data.meshes.new(name); bm_.to_mesh(me_); bm_.free()
    o_ = bpy.data.objects.new(name, me_); bpy.context.scene.collection.objects.link(o_); return o_
def tube_obj(pts, r, name, taper=None):
    cu_ = bpy.data.curves.new(name, 'CURVE'); cu_.dimensions = '3D'; cu_.bevel_depth = r; cu_.bevel_resolution = 4; cu_.use_fill_caps = True
    sp_ = cu_.splines.new('BEZIER'); sp_.bezier_points.add(len(pts) - 1)
    for bp, q in zip(sp_.bezier_points, pts):
        bp.co = q; bp.handle_left_type = bp.handle_right_type = 'AUTO'
        if taper: bp.radius = taper(q)
    cu_.resolution_u = 12
    o_ = bpy.data.objects.new(name, cu_); bpy.context.scene.collection.objects.link(o_)
    bpy.ops.object.select_all(action='DESELECT'); o_.select_set(True); bpy.context.view_layer.objects.active = o_
    bpy.ops.object.convert(target='MESH')
    b_ = bmesh.new(); b_.from_mesh(o_.data); check(b_, name); b_.to_mesh(o_.data); b_.free()
    return o_
# 底座：厚圆盘 + 两级线脚
pieces.append(lathe_obj([(0, 0), (0.07, 0), (0.072, 0.004), (0.07, 0.012), (0.055, 0.016), (0.05, 0.022), (0.03, 0.026), (0.022, 0.03), (0, 0.03)], 48, 'cb_base'))
# 立柱：带两个鼓形节
col_prof = [(0, 0.028), (0.011, 0.028)]
for z, r in [(0.05, 0.009), (0.06, 0.016), (0.07, 0.009), (0.17, 0.008), (0.18, 0.014), (0.19, 0.008), (0.25, 0.007)]:
    col_prof.append((r, z))
col_prof += [(0.0075, 0.252), (0, 0.252)]
pieces.append(lathe_obj(col_prof, 32, 'cb_column'))
# 烛杯（三只）：外鼓内凹的实心杯，顶面下凹 6mm 放蜡烛
def cup(name, cz):
    return lathe_obj([(0, cz), (0.006, cz), (0.016, cz + 0.008), (0.021, cz + 0.02), (0.022, cz + 0.026), (0.017, cz + 0.026), (0.014, cz + 0.021), (0, cz + 0.021)], 32, name)
c0 = cup('cb_cup_top', 0.25); pieces.append(c0)
for sx, nm in ((-1, 'cb_cup_l'), (1, 'cb_cup_r')):
    c = cup(nm, 0.17); c.location.x = sx * 0.105; pieces.append(c)
    # S 形侧臂：从立柱中段出发，下弯再上扬托住杯子
    arm = [Vector((sx * 0.006, 0, 0.12)), Vector((sx * 0.04, 0, 0.1)), Vector((sx * 0.08, 0, 0.115)), Vector((sx * 0.105, 0, 0.15)), Vector((sx * 0.105, 0, 0.172))]
    pieces.append(tube_obj(arm, 0.0055, nm + '_arm'))
    # 卷草：立柱上部两侧的小涡卷
    sc_pts = []
    for k in range(14):
        t = k / 13; ang = t * math.tau * 1.15; rr = 0.022 * (1 - t * 0.8)
        sc_pts.append(Vector((sx * (0.008 + 0.024 - math.cos(ang) * rr), 0, 0.2 + math.sin(ang) * rr)))
    pieces.append(tube_obj(sc_pts, 0.0032, nm + '_scroll'))
    # 底部卷脚
    ft = []
    for k in range(12):
        t = k / 11; ang = t * math.tau * 0.9; rr = 0.018 * (1 - t * 0.75)
        ft.append(Vector((sx * (0.01 + 0.03 - math.cos(ang) * rr), 0, 0.042 + math.sin(ang) * rr)))
    pieces.append(tube_obj(ft, 0.0036, nm + '_foot'))
# 前后方向也各一个卷脚（俯视呈十字）
for sy in (1,):          # 只留后侧卷脚；前侧位置放骷髅
    ft = []
    for k in range(12):
        t = k / 11; ang = t * math.tau * 0.9; rr = 0.018 * (1 - t * 0.75)
        ft.append(Vector((0, sy * (0.01 + 0.03 - math.cos(ang) * rr), 0.042 + math.sin(ang) * rr)))
    pieces.append(tube_obj(ft, 0.0036, f'cb_foot_y{sy}'))
for o in pieces:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.select_all(action='DESELECT')
for o in pieces: o.select_set(True)
bpy.context.view_layer.objects.active = pieces[0]; bpy.ops.object.join()
frame = bpy.context.view_layer.objects.active; frame.name = 'Candelabra_Frame'
b_ = bmesh.new(); b_.from_mesh(frame.data); check(b_, 'Candelabra_Frame'); b_.to_mesh(frame.data); b_.free()
for p_ in frame.data.polygons: p_.use_smooth = True
uv_smart(frame); export(frame, 'Candelabra_Frame.fbx')

# ---------------- 棒棒糖（圆角扁圆盘 + 糖棍；盘面螺旋双色纹由顶点色烘焙）----------------
reset()
RL, TL = 0.032, 0.012          # 半径、厚度
prof = [(0, -TL / 2)]
for k in range(1, 9): prof.append((RL * 0.86 * k / 8, -TL / 2))
for k in range(0, 13):
    a = -math.pi / 2 + math.pi * k / 12
    prof.append((RL * 0.86 + math.cos(a) * RL * 0.14, math.sin(a) * TL / 2))
for k in range(7, 0, -1): prof.append((RL * 0.86 * k / 8, TL / 2))
prof.append((0, TL / 2))
bm = lathe(prof, 96, 'Lollipop_Head')
for v in bm.verts:     # 盘面竖起来（Blender 里 Y 为厚度方向），中心离地 = 棍长
    x, y, z = v.co; v.co = Vector((x, z, y + 0.075))
lol = to_obj(bm, 'Lollipop_Head')
bm2 = lathe([(0, 0), (0.0035, 0), (0.0035, 0.07), (0, 0.07)], 16, 'stick')
me2 = bpy.data.meshes.new('stick'); bm2.to_mesh(me2); bm2.free()
stick = bpy.data.objects.new('Lollipop_Stick', me2); bpy.context.scene.collection.objects.link(stick)
# 盘面平面投影 UV（正反两面共用同一张螺旋图），糖棍 UV 指向图左下角白块
import numpy as np
N_ = 1024
yy, xx = np.mgrid[0:N_, 0:N_].astype(np.float32)
u = (xx + 0.5) / N_ * 2 - 1; v = (yy + 0.5) / N_ * 2 - 1
r = np.hypot(u, v); a = np.arctan2(v, u)
t = 0.5 + 0.5 * np.sin(a * 2 + r * 15)
edge = np.clip((t - 0.5) * 18 + 0.5, 0, 1)          # 抗锯齿硬边
c1 = np.array([1.0, 0.45, 0.08]); c2 = np.array([0.45, 0.13, 0.66])
col = c1[None, None] * (1 - edge[..., None]) + c2[None, None] * edge[..., None]
col *= (0.82 + 0.18 * np.clip(1 - r, 0, 1))[..., None]
col[:24, :24] = [0.95, 0.93, 0.9]
rgba = np.concatenate([col, np.ones((N_, N_, 1))], -1).astype(np.float32)
img = bpy.data.images.new('Lollipop_albedo', N_, N_, alpha=False)
img.pixels = rgba.ravel()
img.filepath_raw = os.path.join(OUT, 'Lollipop_albedo.png'); img.file_format = 'PNG'; img.save()
for ob, head in ((lol, True), (stick, False)):
    if not ob.data.uv_layers: ob.data.uv_layers.new(name='UVMap')
    uvl = ob.data.uv_layers.active.data
    for poly in ob.data.polygons:
        for li in poly.loop_indices:
            co = ob.data.vertices[ob.data.loops[li].vertex_index].co
            uvl[li].uv = (((co.x / RL) * 0.97 + 1) / 2, ((co.z - 0.075) / RL * 0.97 + 1) / 2) if head else (10 / N_, 10 / N_)
mat = bpy.data.materials.new('Lollipop'); lol.data.materials.append(mat); stick.data.materials.append(mat)
bpy.ops.object.select_all(action='DESELECT'); lol.select_set(True); stick.select_set(True); bpy.context.view_layer.objects.active = lol
bpy.ops.object.join()
lol.name = 'Lollipop'
b_ = bmesh.new(); b_.from_mesh(lol.data); check(b_, 'Lollipop'); b_.to_mesh(lol.data); b_.free()
export(lol, 'Lollipop.fbx')

json.dump(report, open(os.path.join(OUT, 'deskparts_report.json'), 'w'), indent=1)
print('DESKPARTS', json.dumps(report))

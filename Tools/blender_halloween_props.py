"""程序化万圣节装饰（在 Blender 里运行）：Blender -b -P Tools/blender_halloween_props.py -- <输出目录>
全部是封闭实体（丝线用带封口的圆管、旗面有厚度），导出前三角化，导出后交给 proc_audit / fbx_roundtrip_fix。
  Deco_Web        蛛网：8 根放射主丝 + 9 圈下垂的螺旋丝（每段丝在两条主丝间向中心下垂），直径 1 米，挂在拱柱之间
  Deco_Bunting    旗串：悬链绳 + 交替的蝙蝠剪影旗/南瓜色三角旗（旗面 6mm 厚、边缘倒角），长 4 米
  Deco_Spider     小蜘蛛：圆身 + 圆头 + 8 条两节腿 + 两只大眼，挂在蛛丝下端（丝单独一根管，Unity 里上下爬）
  Deco_PumpkinVineGarland  南瓜藤花环：螺旋藤 + 叶片 + 卷须，绕在护边上半圈
顶点色 R 通道 = 区域 id（0 丝/绳，1 旗主色，2 旗副色/眼睛），卡通着色器用调色板查色。
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True); random.seed(1031)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def tube(points, r, name, res=6, region=0.0, cyclic=False):
    cd = bpy.data.curves.new(name, 'CURVE'); cd.dimensions = '3D'
    cd.bevel_depth = r; cd.bevel_resolution = max(1, res // 4); cd.use_fill_caps = not cyclic; cd.resolution_u = 2
    sp = cd.splines.new('POLY'); sp.points.add(len(points) - 1); sp.use_cyclic_u = cyclic
    for p, q in zip(sp.points, points): p.co = (q[0], q[1], q[2], 1)
    ob = bpy.data.objects.new(name, cd); bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.convert(target='MESH'); ob.select_set(False)
    paint(ob, region); return ob

def paint(ob, region):
    me = ob.data
    if not me.color_attributes: me.color_attributes.new('Col', 'FLOAT_COLOR', 'POINT')
    ca = me.color_attributes[0]
    for d in ca.data: d.color = (region, 0, 0, 1)

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); ob = bpy.context.view_layer.objects.active; ob.name = name
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=bm.faces); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
    # 顶点色区域 → 材质槽（Unity 侧每槽一个 ToonLit 材质）：0 丝/绳/身体、0.5 副色、1 主色
    names = ['Deco_Base', 'Deco_Accent', 'Deco_Main']
    for nm in names:
        mat = bpy.data.materials.get(nm) or bpy.data.materials.new(nm)
        ob.data.materials.append(mat)
    ca = ob.data.color_attributes[0]
    for p in ob.data.polygons:
        r = sum(ca.data[v].color[0] for v in p.vertices) / len(p.vertices)
        p.material_index = 0 if r < 0.25 else (1 if r < 0.75 else 2)
        p.use_smooth = True
    return ob

def export(ob, fname):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, fname), use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_space_transform=True, colors_type='LINEAR')
    print('EXPORT', fname, len(ob.data.polygons))

def extrude_shape(poly2d, thick, name, region, bevel=0.003):
    """平面多边形（XZ 平面，Y 为厚度）→ 有厚度的实体，带一圈倒角。"""
    bm = bmesh.new()
    vs = [bm.verts.new((x, -thick / 2, z)) for x, z in poly2d]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r['geom'] if isinstance(g, bmesh.types.BMVert)]
    for v in nv: v.co.y += thick
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    m = ob.modifiers.new('bv', 'BEVEL'); m.width = bevel; m.segments = 1; m.limit_method = 'ANGLE'
    bpy.context.view_layer.objects.active = ob; ob.select_set(True); bpy.ops.object.modifier_apply(modifier=m.name); ob.select_set(False)
    paint(ob, region); return ob

# ---------------- 蛛网 ----------------
reset()
R = 0.5; spokes = 8; parts = []
ang = [i / spokes * math.tau + random.uniform(-0.12, 0.12) for i in range(spokes)]
rad = [R * random.uniform(0.85, 1.05) for _ in range(spokes)]
# 主丝从中心结表面内部起（各自起点错开，端面不重合），中心结是一个小扁球把起点全部包住
for i, (a, rr) in enumerate(zip(ang, rad)):
    st = 0.006 + i * 0.0004
    parts.append(tube([(math.cos(a) * st, 0, math.sin(a) * st), (math.cos(a) * rr, 0, math.sin(a) * rr)], 0.0035, 'spoke'))
hub = bmesh.new(); bmesh.ops.create_uvsphere(hub, u_segments=12, v_segments=8, radius=0.016)
for v in hub.verts: v.co.y *= 0.5
me = bpy.data.meshes.new('hub'); hub.to_mesh(me); hub.free()
ho = bpy.data.objects.new('hub', me); bpy.context.scene.collection.objects.link(ho); paint(ho, 0.0); parts.append(ho)
# 螺旋丝：每圈一条闭合环（在相邻主丝之间向中心下垂），不留端点
rings = 9
for k in range(1, rings + 1):
    f = k / (rings + 0.6); pts = []
    for i in range(spokes):
        a0 = ang[i]; a1 = ang[(i + 1) % spokes] + (math.tau if i == spokes - 1 else 0)
        p0 = Vector((math.cos(a0) * rad[i] * f, 0, math.sin(a0) * rad[i] * f))
        p1 = Vector((math.cos(a1) * rad[(i + 1) % spokes] * f, 0, math.sin(a1) * rad[(i + 1) % spokes] * f))
        sag = (p0 + p1) / 2 * 0.87
        for st in range(6):
            t = st / 6
            pts.append(tuple(p0.lerp(sag, t).lerp(sag.lerp(p1, t), t)))
    parts.append(tube(pts, 0.0022, 'ring', cyclic=True))
web = join(parts, 'Deco_Web'); export(web, 'Deco_Web.fbx')

# ---------------- 旗串 ----------------
reset()
L = 4.0; sag = 0.45; n = 13; parts = []
def cat(x):  # 悬链近似（抛物线）
    u = x / L * 2 - 1; return -sag * (1 - u * u)
rope = [(x, 0, cat(x)) for x in [i / 40 * L for i in range(41)]]
parts.append(tube(rope, 0.009, 'rope', region=0.0))
bat = [(0, 0), (0.05, 0.03), (0.09, 0.01), (0.13, 0.05), (0.16, -0.01), (0.12, -0.03), (0.1, -0.08), (0.07, -0.05),
       (0.03, -0.09), (0, -0.14), (-0.03, -0.09), (-0.07, -0.05), (-0.1, -0.08), (-0.12, -0.03), (-0.16, -0.01), (-0.13, 0.05), (-0.09, 0.01), (-0.05, 0.03)]
tri = [(-0.11, 0.0), (0.11, 0.0), (0.0, -0.26)]
for i in range(n):
    x = (i + 0.5) / n * L; z = cat(x)
    # 旗挂点在绳下方 8mm，旗面顶边与绳相交 → 视觉上挂在绳上，实体相交不破面
    if i % 2 == 0:
        shape = [(px * 1.15, pz * 1.15 - 0.004) for px, pz in bat]; region = 0.5
    else:
        shape = [(px, pz - 0.004) for px, pz in tri]; region = 1.0
    o = extrude_shape(shape, 0.006, 'flag', region)
    slope = (cat(x + 0.01) - cat(x - 0.01)) / 0.02
    o.matrix_world = Matrix.Translation((x, 0, z)) @ Matrix.Rotation(math.atan(slope), 4, 'Y').inverted() @ Matrix.Rotation(random.uniform(-0.12, 0.12), 4, 'Z')
    bpy.context.view_layer.objects.active = o; o.select_set(True); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True); o.select_set(False)
    parts.append(o)
b = join(parts, 'Deco_Bunting'); bpy.context.view_layer.objects.active = b
for v in b.data.vertices: v.co.x -= L / 2
export(b, 'Deco_Bunting.fbx')

# ---------------- 小蜘蛛 ----------------
reset(); parts = []
def ball(c, r, sx=1, sy=1, sz=1, region=0.0):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=r)
    for v in bm.verts: v.co = Vector((v.co.x * sx + c[0], v.co.y * sy + c[1], v.co.z * sz + c[2]))
    me = bpy.data.meshes.new('b'); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new('b', me); bpy.context.scene.collection.objects.link(ob); paint(ob, region); return ob
parts.append(ball((0, 0, 0), 0.05, 1, 1.15, 0.95))
parts.append(ball((0, -0.06, 0.005), 0.032))
for s in (-1, 1):
    parts.append(ball((s * 0.014, -0.085, 0.018), 0.012, region=1.0))
    parts.append(ball((s * 0.016, -0.094, 0.02), 0.0055, region=0.5))
    for k in range(4):
        y = -0.03 + k * 0.022
        knee = (s * 0.075, y + (k - 1.5) * 0.02, 0.04)
        foot = (s * 0.11, y + (k - 1.5) * 0.04, -0.04)
        parts.append(tube([(s * 0.02, y, 0), knee, foot], 0.0055, 'leg'))
sp = join(parts, 'Deco_Spider'); export(sp, 'Deco_Spider.fbx')

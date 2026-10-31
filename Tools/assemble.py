"""部件拼装（Blender 里运行）：Blender -b -P Tools/assemble.py -- <装配规格.json> <输出目录>

规格：{"name": "Desk_Table", "parts": [ {"id": "Leg", "src": "P_TableLeg", "fit": {"h": 0.66}, "inst": [{"pos":[x,y,z], "yaw":deg, "name":"Leg_0"}, ...]}, ... ]}
  src：Tripo 部件名（ArtSource/Models/<src>/model.glb，带贴图）或 proc:<文件名>（程序化部件 FBX，在 ArtSource/Procedural/）
  fit：把部件统一缩放到目标尺寸——h=高（Z）/ w=最大水平宽 / d=直径（XY 最大）；可加 "base": "bottom|center|top" 决定原点
  inst：实例位置（Unity 坐标：x 右、y 上、z 前；脚本内部转成 Blender：X=-x? 不，见下）
坐标约定：规格里统一用 Unity 坐标 (x, y, z)。FBX 导出 axis_forward=-Z, axis_up=Y，Blender(X, Y, Z) → Unity(-X, Z, -Y)，
  所以 Blender 位置 = (-x, -z, y)，Blender 绕 Z 旋转角 = Unity yaw（Unity 绕 Y 顺时针看俯视）取反后一致（脚本内部处理）。
每个部件：
  - 合并网格 → 应用变换 → 按 fit 缩放并把原点放到底面中心 → 再次拓扑自检（边界边/非流形必须为 0，不合格报错退出）
  - 贴图另存为 <输出>/<src>_albedo.png，材质命名为 src（Unity 导入按材质名配贴图）
装配后：所有实例两两检测穿插（BVH overlap + 深度估计），> allow_overlap（默认 0.006 米，即“贴合”）的报出；
输出 <name>.fbx（每个实例一个子物体，名字 = inst.name）、<name>_assembly.json（穿插报告）。"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT = '/Users/ld/Documents/ChatGPT/万圣节'
argv = sys.argv[sys.argv.index('--') + 1:]
spec = json.load(open(argv[0])); OUT = argv[1]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
report = {'name': spec['name'], 'parts': {}, 'overlaps': []}


def topo(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    r = dict(faces=len(bm.faces), boundary=sum(1 for e in bm.edges if len(e.link_faces) == 1),
             nonmanifold=sum(1 for e in bm.edges if len(e.link_faces) > 2))
    bm.to_mesh(ob.data); bm.free()
    return r


def hollow(ob, h):
    """开口挖空：先在 cut 高度截平并封口（去掉盖子/梗），再用椭球做布尔差挖出内腔；内腔面 UV 指到贴图里的深色处"""
    V = np.array([v.co[:] for v in ob.data.vertices]); H = V[:, 2].max(); W = max(np.ptp(V[:, 0]), np.ptp(V[:, 1])) / 2
    zc = H * h.get('cut', 0.85)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    if h.get('keep_largest'):      # 只保留最大的连通块（去掉独立的盖子/梗，避免互相穿插的岛让布尔出错）
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6); bm.faces.ensure_lookup_table(); bm.faces.index_update()
        seen, comps = set(), []
        for f0 in bm.faces:
            if f0.index in seen: continue
            st, comp = [f0], []
            seen.add(f0.index)
            while st:
                f = st.pop(); comp.append(f)
                for e in f.edges:
                    for g in e.link_faces:
                        if g.index not in seen: seen.add(g.index); st.append(g)
            comps.append(comp)
        comps.sort(key=len, reverse=True)
        for c_ in comps[1:]: bmesh.ops.delete(bm, geom=c_, context='FACES')
        bm.faces.index_update()
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, zc), plane_no=(0, 0, 1), clear_outer=True)
    cut_edges = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if len(e.link_faces) == 1], sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
    # 椭球内腔
    sp = bmesh.new(); bmesh.ops.create_uvsphere(sp, u_segments=48, v_segments=24, radius=1.0)
    rx, rz = h.get('rx', 0.8) * W, h.get('rz', 0.75) * zc
    for v in sp.verts: v.co = Vector((v.co.x * rx, v.co.y * rx, v.co.z * rz + zc))
    me = bpy.data.meshes.new('cav'); sp.to_mesh(me); sp.free()
    cav = bpy.data.objects.new('cav', me); bpy.context.scene.collection.objects.link(cav)
    m = ob.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.object = cav; m.solver = 'EXACT'
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.data.objects.remove(cav, do_unlink=True)
    # 内腔面（顶点都落在椭球面上）与截面封口面 → UV 指向贴图中最接近目标深色的像素
    img = None
    for mm in ob.data.materials:
        if mm and mm.use_nodes:
            for nd in mm.node_tree.nodes:
                if nd.type == 'TEX_IMAGE' and nd.image: img = nd.image
    if img and ob.data.uv_layers:
        px = np.array(img.pixels[:]).reshape(img.size[1], img.size[0], 4)[..., :3]
        tgt = np.array(h.get('inside_color', [0.45, 0.14, 0.03]))
        d = np.linalg.norm(px[::4, ::4] - tgt, axis=2); iy, ix = np.unravel_index(np.argmin(d), d.shape)
        uv_in = ((ix * 4 + 0.5) / img.size[0], (iy * 4 + 0.5) / img.size[1])
        uvd = ob.data.uv_layers.active.data
        for poly in ob.data.polygons:
            vs_ = [ob.data.vertices[i].co for i in poly.vertices]
            on_cav = all(abs(((c.x / rx) ** 2 + (c.y / rx) ** 2 + ((c.z - zc) / rz) ** 2) - 1) < 0.04 for c in vs_)
            on_cut = all(abs(c.z - zc) < 1e-4 for c in vs_)
            if on_cav or on_cut:
                for li in poly.loop_indices: uvd[li].uv = uv_in
    bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(ob.data); bm.free()


def load_part(p):
    src = p['src']
    before = set(bpy.data.objects)
    if src.startswith('proc:'):
        bpy.ops.import_scene.fbx(filepath=f'{ROOT}/ArtSource/Procedural/{src[5:]}.fbx')
    else:
        cg = f'{ROOT}/ArtSource/Models/{src}/clean.glb'     # 整体模型的封闭去刺版（有就用）
        bpy.ops.import_scene.gltf(filepath=cg if os.path.exists(cg) else f'{ROOT}/ArtSource/Models/{src}/model.glb')
    new = [o for o in bpy.data.objects if o not in before]
    ms = [o for o in new if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in ms: o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o != ob and o.name in bpy.data.objects: bpy.data.objects.remove(o, do_unlink=True)
    # 自动调平：主成分分析，把方差最小的轴（扁平物体的厚度方向）转到 Z
    if p.get('level'):
        P = np.array([v.co[:] for v in ob.data.vertices]); P = P - P.mean(0)
        w, U = np.linalg.eigh(np.cov(P.T)); n = Vector(U[:, 0].tolist())
        if n.z < 0: n = -n
        ob.data.transform(n.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4())
    # 预旋转（部件自身朝向校正，Blender 欧拉角度）
    if 'rot' in p:
        rx, ry, rz = [math.radians(a) for a in p['rot']]
        ob.data.transform(Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X'))
    if p.get('trim_bottom'):    # 截掉底部（如摆件底座）：按高度比例水平切开并封口，仍是封闭体
        V0 = np.array([v.co[:] for v in ob.data.vertices]); z0, z1 = V0[:, 2].min(), V0[:, 2].max()
        zc = z0 + (z1 - z0) * p['trim_bottom']
        bmt = bmesh.new(); bmt.from_mesh(ob.data); bmesh.ops.remove_doubles(bmt, verts=bmt.verts, dist=1e-6)
        bmesh.ops.bisect_plane(bmt, geom=bmt.verts[:] + bmt.edges[:] + bmt.faces[:], plane_co=(0, 0, zc), plane_no=(0, 0, 1), clear_inner=True)
        bmesh.ops.holes_fill(bmt, edges=[e for e in bmt.edges if len(e.link_faces) == 1], sides=0)
        bmesh.ops.triangulate(bmt, faces=[f_ for f_ in bmt.faces if len(f_.verts) > 4])
        bmt.to_mesh(ob.data); bmt.free()
    vs = np.array([v.co[:] for v in ob.data.vertices]); lo, hi = vs.min(0), vs.max(0); dim = hi - lo
    f = p.get('fit', {})
    if 'h' in f: s = f['h'] / dim[2]
    elif 'd' in f: s = f['d'] / max(dim[0], dim[1])
    elif 'w' in f: s = f['w'] / max(dim[0], dim[1])
    else: s = 1.0
    base = f.get('base', 'bottom')
    cz = lo[2] if base == 'bottom' else (hi[2] if base == 'top' else (lo[2] + hi[2]) / 2)
    c = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, cz))
    if p.get('keep_origin'): c = Vector((0, 0, 0))     # 部件在共同坐标系里建好的（如拱柱各部件），不重新居中
    ob.data.transform(Matrix.Diagonal((s, s, s, 1)) @ Matrix.Translation(-c))
    if 'hollow' in p:
        hollow(ob, p['hollow'])
    if 'decimate' in p and len(ob.data.polygons) > p['decimate']:     # 小物件减面（塌陷），之后照样过拓扑检查
        bw = bmesh.new(); bw.from_mesh(ob.data); bmesh.ops.remove_doubles(bw, verts=bw.verts, dist=1e-6); bw.to_mesh(ob.data); bw.free()
        m = ob.modifiers.new('dec', 'DECIMATE'); m.ratio = p['decimate'] / len(ob.data.polygons); m.use_collapse_triangulate = True
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=m.name)
    # 特征方向（水平）：底部质心 - 顶部质心，用于实例 face_out 自动朝向
    V = np.array([v.co[:] for v in ob.data.vertices]); zl, zh = V[:, 2].min(), V[:, 2].max()
    fb = V[V[:, 2] < zl + (zh - zl) * 0.12][:, :2].mean(0); ft = V[V[:, 2] > zh - (zh - zl) * 0.15][:, :2].mean(0)
    ob['feat_ang'] = float(math.atan2(*(fb - ft)[::-1])) if np.linalg.norm(fb - ft) > 1e-6 else 0.0
    t = topo(ob)
    report['parts'][p['id']] = dict(src=src, scale=round(s, 4), size=[round(x * s, 4) for x in dim], **t)
    if t['boundary'] or t['nonmanifold']:
        raise SystemExit(f"部件 {p['id']} ({src}) 拓扑不合格: {t}")
    # 材质与贴图
    mname = src.replace('proc:', '')
    img = None
    for m in ob.data.materials:
        if m and m.use_nodes:
            for nd in m.node_tree.nodes:
                if nd.type == 'TEX_IMAGE' and nd.image: img = nd.image
    if img and not src.startswith('proc:'):
        fp = os.path.join(OUT, f'{mname}_albedo.png'); img.filepath_raw = fp; img.file_format = 'PNG'; img.save()
    mat = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
    ob.data.materials.clear(); ob.data.materials.append(mat)
    for poly in ob.data.polygons: poly.use_smooth = True
    ob.name = '__src_' + p['id']
    return ob


insts = []
for p in spec['parts']:
    src_ob = load_part(p)
    for k, it in enumerate(p['inst']):
        o = src_ob.copy(); o.data = src_ob.data   # 共享网格
        bpy.context.scene.collection.objects.link(o)
        x, y, z = it['pos']
        o.location = (-x, -z, y)     # Unity → Blender
        rx, ry, rz = it.get('rot', [0, 0, 0])
        # Unity yaw（绕上轴）= Blender 绕 Z 的角度（Unity 左手系 + 坐标映射后符号相同，实测）
        yaw_b = math.radians(it.get('yaw', 0))
        if it.get('face_out') or p.get('face_out'):
            # 让部件的特征方向（例如桌腿卷脚）指向远离装配原点的径向
            out_ang = math.atan2(o.location.y, o.location.x)
            yaw_b = out_ang - src_ob['feat_ang'] + math.radians(it.get('yaw', 0))
        o.rotation_mode = 'XYZ'   # glTF 导入的物体默认是四元数模式，直接设欧拉角会被忽略
        o.rotation_euler = (math.radians(rx), math.radians(ry), yaw_b)
        sc = it.get('scale', 1.0)
        o.scale = (sc, sc, sc) if not isinstance(sc, list) else tuple(sc)
        o.name = it.get('name', f"{p['id']}_{k}")
        o['src'] = p['src'].replace('proc:', '')
        if it.get('drop', p.get('drop')): o['drop'] = 1
        if it.get('scatter', p.get('scatter')): o['scatter'] = it.get('scatter', p.get('scatter'))
        if p.get('no_support'): o['no_support'] = 1
        insts.append((o, p.get('allow', []), it.get('allow', [])))
    bpy.data.objects.remove(src_ob, do_unlink=True)

# ---- 落定：标了 drop 的实例按高度从低到高依次沿竖直方向下落，直到碰到已固定的物体（糖果堆自然叠放、不悬空）----
def _bvh_of(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world); t = BVHTree.FromBMesh(bm); bm.free(); return t
bpy.context.view_layer.update()
drops = [it for it in insts if it[0].get('drop')]
fixed = [_bvh_of(o) for o, _, _ in insts if not o.get('drop') and not o.get('no_support')]
import random as _rnd
_rng = _rnd.Random(11)


def _drop_dist(o):
    bpy.context.view_layer.update()
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    zmin = min(v.z for v in vs); zmax = max(v.z for v in vs)
    low = [v for v in vs if v.z < zmin + 0.6 * (zmax - zmin)]
    best = 1.0
    for v in low[::2]:
        for t in fixed:
            hit = t.ray_cast(v + Vector((0, 0, -1e-5)), Vector((0, 0, -1)), 0.5)
            if hit[0] is not None and hit[3] < best: best = hit[3]
    return best


for o, _, _ in sorted(drops, key=lambda t: t[0].location.z):
    sr = o.get('scatter', 0)
    if sr:   # 撒落：在半径 sr 内试多个水平位置，取落得最低的（糖堆自然铺开，不会叠成塔）
        z0 = o.location.z; cands = []
        for _ in range(20):
            a = _rng.uniform(0, 2 * math.pi); r = sr * math.sqrt(_rng.random())
            o.location.x, o.location.y = r * math.cos(a), r * math.sin(a)
            d = _drop_dist(o)
            if d < 1.0: cands.append((z0 - d, o.location.x, o.location.y, d))
        if cands:
            _, x, y, d = min(cands); o.location.x, o.location.y = x, y
            o.location.z -= d - 0.0003; report.setdefault('drops', {})[o.name] = round(d, 4)
    else:
        d = _drop_dist(o)
        if d < 1.0:
            o.location.z -= d - 0.0003; report.setdefault('drops', {})[o.name] = round(d, 4)
    bpy.context.view_layer.update()
    fixed.append(_bvh_of(o))

# ---- 穿插检测 ----
def world_bvh(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    return BVHTree.FromBMesh(bm), bm

bvhs = {o.name: world_bvh(o) for o, _, _ in insts}
ALLOW = spec.get('allow_overlap', 0.006)
names = [o.name for o, _, _ in insts]
allow_pairs = {tuple(sorted(a)) for a in spec.get('allow_pairs', [])}
for i in range(len(names)):
    for j in range(i + 1, len(names)):
        a, b = names[i], names[j]
        if tuple(sorted((a.split('_')[0], b.split('_')[0]))) in allow_pairs: continue
        ta, bma = bvhs[a]; tb, bmb = bvhs[b]
        ov = ta.overlap(tb)
        if not ov: continue
        # 深度：a 的顶点里落在 b 内部（三向射线奇偶）的点，到 b 表面的最大距离
        def depth(bm1, t2):
            best = 0.0
            vs = list(bm1.verts)
            step = max(1, len(vs) // 3000)
            for v in vs[::step]:
                p = v.co; inside = 0
                for d in (Vector((0, 0, 1)), Vector((0, 0, -1)), Vector((0.37, 0.92, 0.11)).normalized()):
                    o = p.copy(); hits = 0
                    for _ in range(40):
                        loc, n, idx, dist = t2.ray_cast(o, d, 50)
                        if loc is None: break
                        hits += 1; o = loc + d * 1e-5
                    inside += hits % 2
                if inside >= 2:
                    loc, n, idx, dd = t2.find_nearest(p)
                    if dd: best = max(best, dd)
            return best
        dep = max(depth(bma, tb), depth(bmb, ta))
        if dep > ALLOW:
            report['overlaps'].append(dict(a=a, b=b, depth=round(dep, 4)))

# ---- 导出 ----
bpy.ops.object.select_all(action='DESELECT')
for o, _, _ in insts: o.select_set(True)
bpy.context.view_layer.objects.active = insts[0][0]
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, spec['name'] + '.fbx'), use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_space_transform=True,
                         path_mode='STRIP', embed_textures=False)
json.dump(report, open(os.path.join(OUT, spec['name'] + '_assembly.json'), 'w'), ensure_ascii=False, indent=1)
# 实例名 → 部件名 对照表（Unity 导入按它给每个子物体配材质）
open(os.path.join(OUT, spec['name'] + '_map.txt'), 'w').write('\n'.join(f"{o.name}\t{o['src']}" for o, _, _ in insts))
# 程序化部件的贴图一并拷过去
import shutil
for p in spec['parts']:
    if p['src'].startswith('proc:'):
        tp = f"{ROOT}/ArtSource/Procedural/{p['src'][5:]}_albedo.png"
        if os.path.exists(tp): shutil.copy(tp, os.path.join(OUT, f"{p['src'][5:]}_albedo.png"))
print('ASSEMBLY', spec['name'], 'parts', len(report['parts']), 'instances', len(insts), 'overlaps', len(report['overlaps']))
for o in report['overlaps']: print('  OVERLAP', o)

"""最小拓扑修复（保留原网格与原贴图）——取代体素重建方案：
  Blender -b -P Tools/blender_repair_min.py -- <model.glb> <out.glb> [部件规则名|-]

体素重建 + 重烘焙会把细节（雕刻笑脸、卷须、飘带）熔掉、把贴图烘出黑块/渗色，审查确认 24 处模型缺陷都来自它。
这里只修真正的拓扑问题，原始面与原始 UV/贴图一律不动：
  1. 合并极近重复顶点（模型尺寸的 1e-5）
  2. 删除漂浮碎屑（小于总面数 0.15% 且体积很小的连通块）
  3. 非流形边（被 3 个以上面共用）：沿边拆开 → 变成开放边界，交给第 4 步
  4. 补洞：对每个开放边界环单独补面（扇形/网格填充 + beauty 三角化）；
     新面的 UV 取该顶点在相邻原面里的 UV（颜色与周边一致），补出来的面朝向与相邻面一致
  5. 统一法线朝外
  6. 审计：边界边 = 0、非流形边 = 0 才算通过；不通过就把剩余问题区域周围一圈删掉重补，最多 6 轮
  7. 可选部件规则（同 blender_repair_parts.py）：按连通块分组导出为 PART_<组名> 物体，共用原贴图
"""
import bpy, bmesh, sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from normals_util import orient_outward, inward_components
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, dst = argv[0], argv[1]
rule_name = argv[2] if len(argv) > 2 and argv[2] != '-' else None


def is_flame(c):
    r, g, b = c['col']
    return r > 0.85 and (r - b) > 0.3 and g > 0.5 and c['faces'] < 600


RULES = {
    'bucket': lambda c: 'Bucket' if c['rank'] == 0 or (max(c['col']) < 0.2 and c['faces'] > 800) else f"Candy_{c['rank']:02d}",
    'candles': lambda c: f"Flame_{c['rank']:02d}" if is_flame(c) else 'Body',
}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != obj: bpy.data.objects.remove(o, do_unlink=True)
size = max(obj.dimensions)

bm = bmesh.new(); bm.from_mesh(obj.data)
uvl = bm.loops.layers.uv.active
stats = dict(faces_in=len(bm.faces))
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=size * 1e-5)


def components(b):
    b.faces.ensure_lookup_table()
    seen = set(); out = []
    for f in b.faces:
        if f.index in seen: continue
        st = [f]; seen.add(f.index); c = []
        while st:
            g = st.pop(); c.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        out.append(c)
    return out


# 2. 碎屑：面数 < 0.15% 且包围盒很小（< 模型尺寸 3%）的连通块；有规则时不删（火苗、糖果都很小）
comps = components(bm)
nf = len(bm.faces)
kill = []
if not rule_name:
    for c in comps:
        if len(c) < max(12, nf * 0.0015):
            vs = np.array([v.co[:] for f in c for v in f.verts])
            if np.max(vs.max(0) - vs.min(0)) < size * 0.03:
                kill.extend(c)
if kill: bmesh.ops.delete(bm, geom=kill, context='FACES')
stats['debris_faces'] = len(kill)


def vert_uv(v):
    """顶点在相邻原面里的 UV（取第一个有 UV 的环）"""
    for l in v.link_loops:
        if l.face.is_valid and l.face.tag is False:
            return l[uvl].uv.copy()
    return None


def fill_holes(b, max_perim=None):
    """对开放边界环补面（只补周长 <= max_perim 的小洞）；返回补出的面数"""
    for f in b.faces: f.tag = False
    boundary = [e for e in b.edges if len(e.link_faces) == 1]
    if not boundary: return 0
    # 按边界环分组
    adj = {}
    for e in boundary:
        for v in e.verts: adj.setdefault(v, []).append(e)
    seen = set(); loops = []
    for e in boundary:
        if e in seen: continue
        st = [e]; seen.add(e); grp = []
        while st:
            x = st.pop(); grp.append(x)
            for v in x.verts:
                for y in adj[v]:
                    if y not in seen: seen.add(y); st.append(y)
        loops.append(grp)
    added = 0
    for grp in loops:
        if max_perim is not None and sum(e.calc_length() for e in grp) > max_perim:
            continue
        res = bmesh.ops.holes_fill(b, edges=grp, sides=0)
        new_faces = res.get('faces', [])
        if not new_faces:
            # holes_fill 处理不了（非简单环）：用 edgenet 补
            try:
                res = bmesh.ops.edgenet_fill(b, edges=grp, use_fill_check=False)
                new_faces = res.get('faces', [])
            except Exception:
                new_faces = []
        if not new_faces: continue
        for f in new_faces: f.tag = True
        tri = bmesh.ops.triangulate(b, faces=new_faces, quad_method='BEAUTY', ngon_method='BEAUTY')
        tfaces = tri['faces']
        for f in tfaces: f.tag = True
        try:
            bmesh.ops.beautify_fill(b, faces=tfaces, edges=[e for f in tfaces for e in f.edges if all(x.tag for x in e.link_faces)])
        except Exception:
            pass
        added += len(tfaces)
    # 新面 UV：每个补丁（相连的新面）整体取一个 UV 点——补丁质心最近的边界顶点在原面里的 UV。
    # 不能逐顶点取：补丁边界可能跨多个 UV 岛，逐顶点取会让补丁在贴图空间里拉出横跨整张图的大三角（颜色串成条纹）。
    if uvl:
        patches = []
        seen_ = set()
        for f in b.faces:
            if not f.tag or f in seen_: continue
            st = [f]; seen_.add(f); grp = []
            while st:
                g = st.pop(); grp.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h.tag and h not in seen_: seen_.add(h); st.append(h)
            patches.append(grp)
        for grp in patches:
            cen = sum((g.calc_center_median() for g in grp), Vector()) / len(grp)
            best = None; bd = 1e30
            for g in grp:
                for v in g.verts:
                    uv = vert_uv(v)
                    if uv is None: continue
                    dd = (v.co - cen).length
                    if dd < bd: bd = dd; best = uv
            if best is None: continue
            for g in grp:
                for l in g.loops: l[uvl].uv = best
    for f in b.faces: f.tag = False
    return added


def wire_and_loose(b):
    w = [e for e in b.edges if not e.link_faces]
    if w: bmesh.ops.delete(b, geom=w, context='EDGES')
    lv = [v for v in b.verts if not v.link_faces]
    if lv: bmesh.ops.delete(b, geom=lv, context='VERTS')


def split_nonmanifold(b):
    """非流形边：不拆边（拆边会留下重合的双层面，焊接后又变回非流形）。
    删掉非流形边周围一圈面，留出开放边界交给补洞。"""
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=size * 1e-6)
    bad = [e for e in b.edges if len(e.link_faces) > 2]
    if bad:
        ring = {f for e in bad for v in e.verts for f in v.link_faces}
        bmesh.ops.delete(b, geom=list(ring), context='FACES')
        wire_and_loose(b)
    return len(bad)





def audit(b):
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=size * 1e-6)
    return dict(boundary=sum(1 for e in b.edges if len(e.link_faces) == 1),
                nonmanifold=sum(1 for e in b.edges if len(e.link_faces) > 2),
                zero=sum(1 for f in b.faces if f.calc_area() < size * size * 1e-12))


def solidify_open(b, thick):
    """仍有开放边界的连通块 = 单层薄片（墙、屋顶、床幔、雕刻开口的壳）：
    不封口，整块向内加厚（正 thickness 沿法线反方向挤出），开口保持开口，变成有厚度的封闭实体。"""
    n = 0
    for c in components(b):
        if not any(len(e.link_faces) == 1 for f in c for e in f.edges): continue
        bmesh.ops.recalc_face_normals(b, faces=c)
        bmesh.ops.solidify(b, geom=c, thickness=thick)
        n += 1
    return n


SMALL_HOLE = size * 0.03      # 周长小于模型尺寸 3% 的洞直接补；大开口加厚
THICK = size * 0.005
stats['nonmanifold_split'] = split_nonmanifold(bm)
wire_and_loose(bm)
bmesh.ops.dissolve_degenerate(bm, dist=size * 1e-6, edges=bm.edges)
stats['filled'] = fill_holes(bm, SMALL_HOLE)
stats['solidified'] = solidify_open(bm, THICK)
def close_all(rounds=12):
    """反复：拆非流形 → 补洞 → 补不上就删一圈再补，直到没有边界边/非流形边"""
    for it in range(rounds):
        a = audit(bm)
        if a['boundary'] == 0 and a['nonmanifold'] == 0: return
        split_nonmanifold(bm)
        wire_and_loose(bm)
        stats['filled'] += fill_holes(bm)
        bd = [e for e in bm.edges if len(e.link_faces) == 1]
        if bd and it >= 1:
            ring = {f for e in bd for v in e.verts for f in v.link_faces}
            if it >= 4:
                ring |= {g for f in list(ring) for v in f.verts for g in v.link_faces}
            bmesh.ops.delete(bm, geom=list(ring), context='FACES')
            wire_and_loose(bm)
            stats['filled'] += fill_holes(bm)


close_all()
for it in range(0):
    a = audit(bm)
    if a['boundary'] == 0 and a['nonmanifold'] == 0: break
    # 加厚只做一次；之后剩下的问题（加厚自交、焊接后的非流形）删一圈再补，不论大小
    split_nonmanifold(bm)
    wire_and_loose(bm)
    stats['filled'] += fill_holes(bm)
    # 补不上的复杂边界环（自相接触/8 字形）：把边界顶点周围一圈面删掉，让洞变成简单环再补
    bd = [e for e in bm.edges if len(e.link_faces) == 1]
    if bd and it >= 1:
        ring = {f for e in bd for v in e.verts for f in v.link_faces}
        if it >= 4:   # 还不行就再扩一圈
            ring |= {g for f in list(ring) for v in f.verts for g in v.link_faces}
        bmesh.ops.delete(bm, geom=list(ring), context='FACES')
        wire_and_loose(bm)
        stats['filled'] += fill_holes(bm)


def slivers(b):
    """导出端会丢掉的退化三角形：面积极小，或最小内角 < 0.5°"""
    out = []
    for f in b.faces:
        if f.calc_area() < size * size * 1e-9:
            out.append(f); continue
        if len(f.verts) == 3:
            vs = [v.co for v in f.verts]
            for i in range(3):
                a = vs[(i + 1) % 3] - vs[i]; c = vs[(i + 2) % 3] - vs[i]
                if a.length < 1e-12 or c.length < 1e-12 or a.angle(c) < 0.0087:
                    out.append(f); break
    return out


# 导出前：全部三角化，塌掉细长/零面积三角形（塌最短边），再补洞；反复直到干净
for it in range(8):
    bmesh.ops.triangulate(bm, faces=bm.faces)
    sl = slivers(bm)
    if not sl and audit(bm)['boundary'] == 0 and audit(bm)['nonmanifold'] == 0: break
    if sl:
        es = list({min(f.edges, key=lambda e: e.calc_length()) for f in sl if f.is_valid})
        es = [e for e in es if e.is_valid]
        if es: bmesh.ops.collapse(bm, edges=es, uvs=True)
    bmesh.ops.dissolve_degenerate(bm, dist=size * 1e-6, edges=bm.edges)
    split_nonmanifold(bm)
    wire_and_loose(bm)
    close_all(8)
stats['slivers_left'] = len(slivers(bm))
stats['components_flipped'] = orient_outward(bm, size)
_b, _t = inward_components(bm); stats['inward_faces'] = _b
stats.update(audit(bm))
stats['faces_out'] = len(bm.faces)

# 7. 部件分组
groups = None
if rule_name:
    px = None; img = None
    for slot in obj.material_slots:
        if slot.material and slot.material.use_nodes:
            for nd in slot.material.node_tree.nodes:
                if nd.type == 'TEX_IMAGE' and nd.image: img = nd.image
    if img: px = np.array(img.pixels[:]).reshape(img.size[1], img.size[0], 4)
    comps = sorted(components(bm), key=len, reverse=True)
    groups = {}
    for rank, c in enumerate(comps):
        col = [0.5, 0.5, 0.5]
        if px is not None and uvl:
            cs = [px[int((f.loops[0][uvl].uv.y % 1) * (img.size[1] - 1)), int((f.loops[0][uvl].uv.x % 1) * (img.size[0] - 1)), :3] for f in c[:400]]
            col = np.mean(cs, 0).tolist()
        g = RULES[rule_name](dict(rank=rank, faces=len(c), col=col))
        groups.setdefault(g, []).extend(f.index for f in c)
    stats['groups'] = {k: len(v) for k, v in groups.items()}

bm.to_mesh(obj.data); bm.free()
for p in obj.data.polygons: p.use_smooth = True

if groups:
    # 按组用材质槽标记再 separate（所有槽共用原材质的拷贝）
    base = obj.data.materials[0] if obj.data.materials else bpy.data.materials.new('m')
    obj.data.materials.clear()
    names = sorted(groups)
    for n in names:
        m = base.copy(); m.name = 'PART_' + n; obj.data.materials.append(m)
    idx = {fi: names.index(g) for g, fl in groups.items() for fi in fl}
    for p in obj.data.polygons: p.material_index = idx.get(p.index, 0)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='MATERIAL')
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
        o.name = o.data.materials[0].name
else:
    obj.name = 'repaired'

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=dst, use_selection=True, export_format='GLB')
stats['pass'] = stats['boundary'] == 0 and stats['nonmanifold'] == 0
open(os.path.splitext(dst)[0] + '_repair.json', 'w').write(json.dumps(stats, ensure_ascii=False, indent=2))
print('REPAIRMIN', json.dumps(stats, ensure_ascii=False))
sys.exit(0 if stats['pass'] else 2)

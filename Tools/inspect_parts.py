"""查看模型的连通块结构（Blender 里运行）：Blender -b -P Tools/inspect_parts.py -- <a.glb> [b.glb ...]
对每个连通块报告：面数、包围盒、平均贴图颜色（采样 UV 处贴图）。用来判断糖和桶、火苗和蜡烛是不是独立部件。
另外从模型中心向四周打射线，判断模型是否中空（射线先穿出外壳再进入内壁 → 中空）。"""
import bpy, bmesh, sys
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

for fp in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=fp)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    o = ms[0]
    img = None
    for slot in o.material_slots:
        if slot.material and slot.material.use_nodes:
            for nd in slot.material.node_tree.nodes:
                if nd.type == 'TEX_IMAGE' and nd.image:
                    img = nd.image
    px = np.array(img.pixels[:]).reshape(img.size[1], img.size[0], 4) if img else None
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    uv = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()
    seen = set(); comps = []
    for f in bm.faces:
        if f.index in seen: continue
        st = [f]; seen.add(f.index); c = []
        while st:
            g = st.pop(); c.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        comps.append(c)
    comps.sort(key=len, reverse=True)
    allv = np.array([v.co[:] for v in bm.verts]); lo, hi = allv.min(0), allv.max(0)
    print('MODEL', fp.split('/')[-2], 'faces', len(bm.faces), 'comps', len(comps), 'bbox', np.round(lo, 3).tolist(), np.round(hi, 3).tolist())
    for c in comps[:40]:
        vs = np.array([v.co[:] for f in c for v in f.verts])
        col = None
        if px is not None and uv:
            cs = []
            for f in c[:400]:
                u = f.loops[0][uv].uv
                x = int((u.x % 1) * (img.size[0] - 1)); y = int((u.y % 1) * (img.size[1] - 1))
                cs.append(px[y, x, :3])
            col = np.round(np.mean(cs, 0), 2).tolist()
        print('  PART faces=%5d  z=%.3f..%.3f  xy=(%.3f,%.3f)  col=%s' % (len(c), vs[:, 2].min(), vs[:, 2].max(), vs[:, 0].mean(), vs[:, 1].mean(), col))
    # 中空检测：从包围盒中心沿 8 个水平方向打射线，统计命中次数
    bvh = BVHTree.FromBMesh(bm)
    ctr = Vector(((lo + hi) / 2).tolist())
    counts = []
    for k in range(8):
        a = k / 8 * 6.2832; d = Vector((np.cos(a), np.sin(a), 0.0))
        o_ = ctr.copy(); n = 0
        for _ in range(20):
            loc, nrm, idx, dist = bvh.ray_cast(o_, d, 10)
            if loc is None: break
            n += 1; o_ = loc + d * 1e-4
        counts.append(n)
    print('  CENTER_RAY_HITS', counts, '（1 次 = 中心在实体内部或空腔外壳单层；2 次以上 = 有内壁）')

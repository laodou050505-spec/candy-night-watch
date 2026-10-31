"""程序化精细建模：中心浮空圣坛（玩家所在的地面）+ 哥特环廊拱券 + 吊桥。在 Blender 里运行：
  Blender -b -P Tools/blender_sanctum.py -- <输出目录>

设计（单位米，Blender Z 向上；导出 FBX 时转 Unity Y 向上）：
  - 圣坛岛面：半径 4.6。中心铺星月纹石板（同心环 + 放射分缝，每块石板单独倒角、随机高低/倾斜/缺角），
    外圈一道凸起的石砌护边（带随机磕缺），岛底是倒锥形碎岩（多层不规则岩块 + 悬垂根须）。
  - 环廊：8 个拱位（正前方 ±40° 留空，保证看玫瑰窗无遮挡），拱券用尖拱轮廓扫掠出带线脚的拱肋，
    柱身是带收分的八角柱 + 柱础 + 柱头；部分拱半毁（断口用布尔噪声切），营造“残廊”。
    南瓜藤由 P2 模型 L1_ArchColumn 提供，另行缠绕在柱身上（Unity 里摆放），这里只出石构。
  - 吊桥：曲线悬链，木板逐块随机宽度/扭转/缺损，两侧麻绳用三股螺旋扫掠。
每类物件导出独立 FBX（便于 Unity 里分别加材质/LOD），并输出顶点色作为 AO/磨损遮罩（卡通着色器读取）。
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]
os.makedirs(OUT, exist_ok=True)
random.seed(20261025)

# 岛尺寸（v4：放大到能容纳环廊）。Unity 坐标 (x, z) = Blender (-x, -y)；玩家面朝 Unity -Z = Blender +Y。
ISLAND_R = 7.4          # 地面石板外径
RIM_R0, RIM_R1 = 7.42, 7.82   # 护边内外径
# 护边缺口（Unity 方位角，0 = 正前方 -Z，正值 = 向 +X）：正前方降低成矮沿；四座吊桥桥头全开
FRONT_LOW_DEG = 34
BRIDGE_GAPS_DEG = [-62, 62, -128, 128]
BRIDGE_GAP_HALF_DEG = 6.5


def unity_bearing(blender_angle):
    """Blender 极角（atan2(y, x)）→ Unity 方位角（0 = 正前 -Z，正 = +X）。"""
    x, y = math.cos(blender_angle), math.sin(blender_angle)
    ux, uz = -x, -y
    return math.degrees(math.atan2(ux, -uz))


def ang_dist(a, b):
    d = (a - b + 180) % 360 - 180
    return abs(d)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_obj(name, bm):
    # 统一法线朝外（手工拼的面绕序不保证一致）
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def export(objs, fname):
    # 先三角化：非平面四边形在 FBX 导入端会被不同方式切分甚至剔除，造成破洞
    for o in objs:
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bm.to_mesh(o.data); bm.free()
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, fname), use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             object_types={'MESH'}, mesh_smooth_type='FACE', bake_space_transform=True,
                             use_mesh_modifiers=True, colors_type='LINEAR')
    print('EXPORT', fname, sum(len(o.data.polygons) for o in objs), 'faces')


def bevel_box(bm, center, size, rot_z=0.0, tilt=(0, 0), bevel=0.02, segs=2, chip=0.0):
    """一块倒角石头：先建盒子，再倒角，再用噪声把顶面轻微起伏，模拟手凿感。返回新面列表。"""
    r = bmesh.ops.create_cube(bm, size=1.0)
    verts = r['verts']
    S = Matrix.Diagonal((size[0], size[1], size[2], 1))
    R = Matrix.Rotation(rot_z, 4, 'Z') @ Matrix.Rotation(tilt[0], 4, 'X') @ Matrix.Rotation(tilt[1], 4, 'Y')
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ R @ S, verts=verts)
    edges = list({e for v in verts for e in v.link_edges})
    faces0 = list({f for v in verts for f in v.link_faces})
    res = bmesh.ops.bevel(bm, geom=verts + edges + faces0, offset=bevel, segments=max(segs, 3), affect='EDGES', profile=0.6)
    # 倒角会删除原顶点：改用倒角后仍存活的面的顶点（原盒子面被缩小保留 + 新倒角面）
    alive = [f for f in faces0 if f.is_valid] + list(res.get('faces', []))
    # 大面再细分两次，让噪声能“雕”出手凿起伏，避免方块感
    big = [f for f in alive if f.is_valid and f.calc_area() > (bevel * 4) ** 2]
    if big:
        bmesh.ops.subdivide_edges(bm, edges=list({e for f in big for e in f.edges}), cuts=2, use_grid_fill=True)
    vs = {v for f in bm.faces if f.is_valid for v in f.verts if (v.co - center).length < max(size) * 1.2}
    top = center.z + size[2] / 2
    amp = min(size) * 0.06
    for v in vs:
        d = v.co - center
        nrm = Vector((d.x / max(size[0], 1e-6), d.y / max(size[1], 1e-6), d.z / max(size[2], 1e-6))).normalized()
        v.co += nrm * noise.noise(v.co * (5.0 / max(min(size), 0.05))) * amp
        if v.co.z > top - bevel * 1.5:
            v.co.z += noise.noise(v.co * 6.0) * 0.006
            if chip > 0 and noise.noise(v.co * 2.3 + Vector((7, 1, 3))) > 0.45:
                v.co.z -= chip   # 缺角
    return vs


def slab(bm, r0, r1, a0, a1, h, nu=9, nv=5):
    """一块扇形石板：顶面 nu×nv 细分网格；四周向下收成圆角；顶面中心鼓、边缘下塌；
    噪声做手凿起伏 + 偶发磕缺；整块随机微倾。底面封口。"""
    tilt = (random.uniform(-1, 1) * 0.01, random.uniform(-1, 1) * 0.01)
    seed = Vector((random.uniform(0, 100), random.uniform(0, 100), 0))
    round_w = 0.035
    grid = []
    for j in range(nv + 1):
        v = j / nv
        row = []
        for i in range(nu + 1):
            u = i / nu
            a = a0 + (a1 - a0) * u
            r = r0 + (r1 - r0) * v
            x, y = math.cos(a) * r, math.sin(a) * r
            # 到边缘的距离（米）→ 圆角下塌
            du = min(u, 1 - u) * (a1 - a0) * r
            dv = min(v, 1 - v) * (r1 - r0)
            de = min(du, dv)
            edge = 1 - min(1.0, de / round_w)
            z = h - edge * edge * 0.03
            # 中心微鼓
            z += (1 - (2 * u - 1) ** 2) * (1 - (2 * v - 1) ** 2) * 0.006
            p = Vector((x, y, 0))
            z += noise.noise(p * 7.0 + seed) * 0.005 + noise.noise(p * 23.0 + seed) * 0.0018
            if noise.noise(p * 3.1 + seed * 2) > 0.5 and edge > 0.3:
                z -= 0.012          # 边角磕缺
            z += tilt[0] * x + tilt[1] * y
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    # 侧壁 + 底面：沿边界一圈下拉到 z=0
    border = [grid[0][i] for i in range(nu + 1)] + [grid[j][nu] for j in range(1, nv + 1)] + \
             [grid[nv][i] for i in range(nu - 1, -1, -1)] + [grid[j][0] for j in range(nv - 1, 0, -1)]
    bottom = [bm.verts.new((v.co.x, v.co.y, 0.0)) for v in border]
    m = len(border)
    for i in range(m):
        bm.faces.new((border[(i + 1) % m], border[i], bottom[i], bottom[(i + 1) % m]))
    bm.faces.new(bottom)


# ======================================================================
# 1) 圣坛岛面：星月纹石板
# ======================================================================
def build_floor():
    bm = bmesh.new()
    rings = [0.55]
    while rings[-1] < ISLAND_R - 0.05:
        rings.append(min(ISLAND_R, rings[-1] + 0.62))
    gap = 0.018
    # 中心：一块圆形月盘石（车削）
    circ = bmesh.ops.create_circle(bm, cap_ends=True, segments=48, radius=rings[0] - gap)
    f = [f for f in bm.faces if len(f.verts) > 4][-1]
    ex = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in ex['geom'] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, 0.09), verts=nv)
    for v in nv:
        v.co.z += noise.noise(v.co * 3) * 0.004
    # 同心环的石板：每环按周长分块，相邻环错缝；每块是细分网格，雕出鼓面、圆边、磨损
    for ri in range(1, len(rings)):
        r0, r1 = rings[ri - 1] + gap, rings[ri] - gap
        rm = (r0 + r1) / 2
        n = max(8, int(2 * math.pi * rm / 0.62))
        off = random.random() * math.tau
        for k in range(n):
            a0 = off + k / n * math.tau + gap / rm
            a1 = off + (k + 1) / n * math.tau - gap / rm
            slab(bm, r0, r1, a0, a1, 0.085 + random.uniform(-0.012, 0.012))
    # 星月纹：第 2、4 环在 12 个方向嵌入浅色“星”石（抬高 4mm，Unity 里换浅色材质 → 顶点色标记）
    ob = new_obj('Sanctum_Floor', bm)
    return ob


def stone_block(bm, r0, r1, a0, a1, z0, z1, nu=8, nv=4, nw=4, chip=True):
    """一块弧形砌石（跟着圆周弯）：六个面都是细分网格，所有棱做圆角下塌，表面噪声雕琢，偶发缺角。
    参数：径向 r0→r1、角度 a0→a1、高度 z0→z1。"""
    seed = Vector((random.uniform(0, 100), random.uniform(0, 100), random.uniform(0, 100)))
    rnd = 0.04
    W = (a1 - a0) * (r0 + r1) / 2
    D, Hh = r1 - r0, z1 - z0

    def pos(u, v, w):
        # u:角向 v:径向 w:高度，均 0..1；越靠近棱越往里收（圆角）
        a = a0 + (a1 - a0) * u
        r = r0 + (r1 - r0) * v
        z = z0 + (z1 - z0) * w
        du, dv, dw = min(u, 1 - u) * W, min(v, 1 - v) * D, min(w, 1 - w) * Hh
        e1 = max(0.0, 1 - du / rnd); e2 = max(0.0, 1 - dv / rnd); e3 = max(0.0, 1 - dw / rnd)
        # 棱：两个方向同时靠边时收得更多（圆角），单边靠近时只收一点
        k = (e1 * e2 + e2 * e3 + e1 * e3) * 0.5 + (e1 + e2 + e3) * 0.04
        cu, cv, cw = (u - 0.5), (v - 0.5), (w - 0.5)
        # 向块中心收缩
        a -= cu * 2 * k * rnd / max((r0 + r1) / 2, 1e-3) * (e1 > 0)
        r -= cv * 2 * k * rnd * (e2 > 0)
        z -= cw * 2 * k * rnd * (e3 > 0)
        p = Vector((math.cos(a) * r, math.sin(a) * r, z))
        n = noise.noise(p * 6.0 + seed) * 0.012 + noise.noise(p * 19.0 + seed) * 0.004
        # 法线方向粗略取：离哪个面最近就沿哪个面推
        out = Vector((math.cos(a), math.sin(a), 0)) * (1 if v > 0.5 else -1) if dv <= min(du, dw) else \
            (Vector((0, 0, 1 if w > 0.5 else -1)) if dw <= du else Vector((-math.sin(a), math.cos(a), 0)) * (1 if u > 0.5 else -1))
        p += out * n
        if chip and w > 0.8 and noise.noise(p * 2.7 + seed) > 0.5:
            p.z -= 0.025
        return p

    # 用一个细分立方体的“壳”：遍历 6 个面
    faces_spec = [
        ('w', 1, lambda i, j: (i / nu, j / nv, 1.0), nu, nv, False),
        ('w', 0, lambda i, j: (i / nu, j / nv, 0.0), nu, nv, True),
        ('v', 1, lambda i, j: (i / nu, 1.0, j / nw), nu, nw, True),
        ('v', 0, lambda i, j: (i / nu, 0.0, j / nw), nu, nw, False),
        ('u', 1, lambda i, j: (1.0, i / nv, j / nw), nv, nw, False),
        ('u', 0, lambda i, j: (0.0, i / nv, j / nw), nv, nw, True),
    ]
    cache = {}

    def vert(uvw):
        key = tuple(round(c, 5) for c in uvw)
        if key not in cache:
            cache[key] = bm.verts.new(pos(*uvw))
        return cache[key]

    for _, _, f, ni, nj, flip in faces_spec:
        for i in range(ni):
            for j in range(nj):
                q = [vert(f(i, j)), vert(f(i + 1, j)), vert(f(i + 1, j + 1)), vert(f(i, j + 1))]
                if flip:
                    q.reverse()
                try:
                    bm.faces.new(q)
                except ValueError:
                    pass


def build_rim():
    """外圈石砌护边：一圈弧形砌石，两层错缝（下层长、上层短），随机高低与缺角；
    正前方 ±30° 降低成一道矮沿，视线不被挡；偶尔加盖石和小南瓜位（Unity 里放 P2 南瓜）。"""
    bm = bmesh.new()
    r0, r1 = RIM_R0, RIM_R1
    # 下层
    n = 64
    off = random.random()
    for k in range(n):
        a0 = (k + off) / n * math.tau + 0.004
        a1 = (k + 1 + off) / n * math.tau - 0.004
        am = (a0 + a1) / 2
        ub = unity_bearing(am)
        if any(ang_dist(ub, g) < BRIDGE_GAP_HALF_DEG for g in BRIDGE_GAPS_DEG):
            continue   # 桥头缺口
        front = ang_dist(ub, 0) < FRONT_LOW_DEG
        h = 0.18 + random.uniform(-0.015, 0.02)
        stone_block(bm, r0, r1, a0, a1, 0.0, h, chip=not front)
    # 上层（错半块）
    for k in range(n):
        a0 = (k + off + 0.5) / n * math.tau + 0.004
        a1 = (k + 1 + off + 0.5) / n * math.tau - 0.004
        am = (a0 + a1) / 2
        ub = unity_bearing(am)
        if ang_dist(ub, 0) < FRONT_LOW_DEG or any(ang_dist(ub, g) < BRIDGE_GAP_HALF_DEG + 3 for g in BRIDGE_GAPS_DEG):
            continue
        if random.random() < 0.12:
            continue   # 偶尔缺一块，残破感
        h0 = 0.19
        h1 = h0 + 0.15 + random.uniform(-0.02, 0.04)
        stone_block(bm, r0 + 0.02, r1 - 0.02, a0, a1, h0, h1)
        if random.random() < 0.3:
            stone_block(bm, r0 + 0.05, r1 - 0.05, a0 + 0.01, a1 - 0.01, h1 + 0.01, h1 + 0.09, nu=6, nv=3, nw=2)
    return new_obj('Sanctum_Rim', bm)


def build_underside():
    """岛底：倒锥形，由 5 层不规则岩块堆叠（越往下越小），加悬垂根须。"""
    bm = bmesh.new()
    k_ = ISLAND_R / 4.3
    layers = [(4.3 * k_, -0.75), (3.9 * k_, -1.5), (3.1 * k_, -2.3), (2.2 * k_, -3.4), (1.2 * k_, -4.6), (0.5 * k_, -5.6)]
    for li, (rad, z) in enumerate(layers):
        n = max(6, int(rad * 5))
        for k in range(n):
            a = k / n * math.tau + random.uniform(-0.1, 0.1) + li * 0.3
            rr = rad * random.uniform(0.75, 0.98)
            sz = (rad * 0.55 * random.uniform(0.8, 1.2), math.tau * rr / n * random.uniform(1.0, 1.4), 0.9 + random.uniform(-0.2, 0.3))
            c = Vector((math.cos(a) * rr * 0.75, math.sin(a) * rr * 0.75, z))
            vs = bevel_box(bm, c, sz, rot_z=a, tilt=(random.uniform(-0.25, 0.25), random.uniform(-0.25, 0.25)), bevel=0.09, segs=2)
            for v in vs:   # 岩块表面噪声起伏，去掉“方块感”
                v.co += Vector((noise.noise(v.co * 1.3), noise.noise(v.co * 1.3 + Vector((3, 0, 0))), noise.noise(v.co * 1.3 + Vector((0, 5, 0))))) * 0.12
    # 岩块不得高过岛面底（z=-0.12），超出的顶点压回去（压平后顶部被石板/护边盖住）
    for v in bm.verts:
        if v.co.z > -0.12:
            v.co.z = -0.12
        rr = math.hypot(v.co.x, v.co.y)
        if rr > RIM_R1 - 0.05:
            v.co.x *= (RIM_R1 - 0.05) / rr; v.co.y *= (RIM_R1 - 0.05) / rr
    ob = new_obj('Sanctum_Underside', bm)
    # 岩块互相穿插 → 体素重建成一整块水密岩体（无内部面、无穿插），再轻度平滑和减面
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    m = ob.modifiers.new('vox', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 0.055; m.adaptivity = 0.0
    bpy.ops.object.modifier_apply(modifier=m.name)
    m = ob.modifiers.new('sm', 'SMOOTH'); m.factor = 0.5; m.iterations = 4
    bpy.ops.object.modifier_apply(modifier=m.name)
    m = ob.modifiers.new('dec', 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = min(1.0, 30000 / max(1, len(ob.data.polygons)))
    bpy.ops.object.modifier_apply(modifier=m.name)
    for _ in range(3):   # 减面偶发的退化/小孔：删退化、删非流形、封孔、重算法线
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=bm.edges)
        # 细长三角形（面积极小但边不短）导出后会被 FBX 导入剔掉形成破洞 → 塌缩最短边
        thin = [f for f in bm.faces if f.calc_area() < 1e-6]
        es = list({min(f.edges, key=lambda e: e.calc_length()) for f in thin})
        if es:
            bmesh.ops.collapse(bm, edges=es, uvs=False)
        bad = [e for e in bm.edges if len(e.link_faces) > 2]
        if bad:
            bmesh.ops.delete(bm, geom=list({f for e in bad for f in e.link_faces}), context='FACES')
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(ob.data); bm.free()
    for _ in range(6):   # 兜底：删掉问题边周围两圈面再封孔
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-3)
        bmesh.ops.dissolve_degenerate(bm, dist=2e-3, edges=bm.edges)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bad = {v for e in bm.edges if len(e.link_faces) != 2 for v in e.verts}
        bad |= {v for f in bm.faces if f.calc_area() < 1e-6 for v in f.verts}
        if not bad:
            bm.to_mesh(ob.data); bm.free(); break
        for _r in range(2):
            bad |= {w for v in list(bad) for e in v.link_edges for w in e.verts}
        bmesh.ops.delete(bm, geom=list({f for v in bad for f in v.link_faces}), context='FACES')
        loose = [v for v in bm.verts if not v.link_faces]
        if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
        bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(ob.data); bm.free()
    for p_ in ob.data.polygons:
        p_.use_smooth = True
    # 根须：曲线 + 粗细渐变
    roots = []
    k_ = ISLAND_R / 4.3
    for k in range(30):
        a = random.uniform(0, math.tau)
        r0 = random.uniform(1.5, 4.0) * k_
        start = Vector((math.cos(a) * r0, math.sin(a) * r0, -0.4 - (4.4 * k_ - r0) * 0.75))
        cu = bpy.data.curves.new(f'root{k}', 'CURVE'); cu.dimensions = '3D'
        sp = cu.splines.new('BEZIER'); n = 5
        sp.bezier_points.add(n - 1)
        p = start.copy()
        for i, bp in enumerate(sp.bezier_points):
            bp.co = p
            bp.handle_left_type = bp.handle_right_type = 'AUTO'
            bp.radius = 1.0 - i / n
            p = p + Vector((random.uniform(-0.25, 0.25), random.uniform(-0.25, 0.25), -random.uniform(0.35, 0.7)))
        # 末端卷曲
        sp.bezier_points[-1].co += Vector((0.2, 0.0, 0.15))
        cu.bevel_depth = 0.045 * random.uniform(0.7, 1.3)
        cu.bevel_resolution = 3
        cu.use_fill_caps = True
        cu.resolution_u = 8
        o = bpy.data.objects.new(f'Root{k}', cu)
        bpy.context.scene.collection.objects.link(o)
        roots.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in roots:
        o.select_set(True)
    bpy.context.view_layer.objects.active = roots[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    roots[0].name = 'Sanctum_Roots'
    return ob, roots[0]


# ======================================================================
# 1b) 星月纹地毯：铺在玩家和南瓜桌之下，三个材质槽（毯面 / 金边 / 发光纹样）
# ======================================================================
def build_rug():
    R_IN, R_OUT, T = 0.0, 2.35, 0.012
    Z0 = 0.10                   # 石板顶面约 0.085~0.1，毯子盖在上面
    bm = bmesh.new()
    mats = []
    def ring(r0, r1, z0, z1, seg, scallop=0.0, mat=0):
        # 一圈有厚度的环带；外沿可做扇贝起伏
        top_i, top_o, bot_i, bot_o = [], [], [], []
        for i in range(seg):
            a = i / seg * math.tau
            ro = r1 + (scallop * (0.5 + 0.5 * math.cos(a * 48)) if scallop else 0)
            c, s_ = math.cos(a), math.sin(a)
            top_i.append(bm.verts.new((c * r0, s_ * r0, z1))); top_o.append(bm.verts.new((c * ro, s_ * ro, z1)))
            bot_i.append(bm.verts.new((c * r0, s_ * r0, z0))); bot_o.append(bm.verts.new((c * ro, s_ * ro, z0)))
        fs = []
        for i in range(seg):
            j = (i + 1) % seg
            fs.append(bm.faces.new((top_i[i], top_o[i], top_o[j], top_i[j])))
            fs.append(bm.faces.new((bot_i[j], bot_o[j], bot_o[i], bot_i[i])))
            fs.append(bm.faces.new((top_o[i], bot_o[i], bot_o[j], top_o[j])))
            if r0 > 0:
                fs.append(bm.faces.new((top_i[j], bot_i[j], bot_i[i], top_i[i])))
        for f in fs: f.material_index = mat
        return fs
    def disc(r, z0, z1, seg, mat):
        top = [bm.verts.new((math.cos(i / seg * math.tau) * r, math.sin(i / seg * math.tau) * r, z1)) for i in range(seg)]
        bot = [bm.verts.new((math.cos(i / seg * math.tau) * r, math.sin(i / seg * math.tau) * r, z0)) for i in range(seg)]
        fs = [bm.faces.new(top), bm.faces.new(list(reversed(bot)))]
        for i in range(seg):
            j = (i + 1) % seg
            fs.append(bm.faces.new((top[i], bot[i], bot[j], top[j])))
        for f in fs: f.material_index = mat
    def prism(pts2d, z0, z1, mat):
        top = [bm.verts.new((x, y, z1)) for x, y in pts2d]
        bot = [bm.verts.new((x, y, z0)) for x, y in pts2d]
        fs = [bm.faces.new(top), bm.faces.new(list(reversed(bot)))]
        n = len(pts2d)
        for i in range(n):
            j = (i + 1) % n
            fs.append(bm.faces.new((top[i], bot[i], bot[j], top[j])))
        for f in fs: f.material_index = mat
    # 毯面（中心圆 + 主环），两圈金边，外沿扇贝流苏
    # 各部件是独立封闭实体：相邻圈之间留 2mm 缝，金边略高并压在毯面上方（不共点、不共面）
    disc(1.62, Z0, Z0 + T, 96, 0)
    ring(1.622, 1.738, Z0 + 0.001, Z0 + T + 0.003, 96, mat=1)
    ring(1.740, 2.178, Z0, Z0 + T, 128, mat=0)
    ring(2.180, 2.26, Z0 + 0.001, Z0 + T + 0.003, 128, scallop=0.05, mat=1)
    # 发光纹：外环上 12 颗四角星，中心方向（玩家正后方）留出坐处，内圈一弯大月牙
    for k in range(12):
        a = k / 12 * math.tau
        cx, cy = math.cos(a) * 1.96, math.sin(a) * 1.96
        pts = []
        for i in range(8):
            ang = a + i / 8 * math.tau
            rr = 0.13 if i % 2 == 0 else 0.045
            pts.append((cx + math.cos(ang) * rr, cy + math.sin(ang) * rr))
        prism(pts, Z0 + T - 0.002, Z0 + T + 0.004, 2)
    # 月牙：外弧（大圆）+ 内弧（过两个尖端、圆心向开口侧偏移的圆），构成不自交的简单多边形
    C = Vector((0.0, 0.55)); R1 = 0.6; tip = math.radians(128)
    P1 = Vector((C.x + math.cos(tip) * R1, C.y + math.sin(tip) * R1))
    P2 = Vector((C.x + math.cos(-tip) * R1, C.y + math.sin(-tip) * R1))
    Ci = Vector((C.x - 0.28, C.y)); r2 = (P1 - Ci).length
    outer = [(C.x + math.cos(t) * R1, C.y + math.sin(t) * R1) for t in [-tip + i / 80 * 2 * tip for i in range(81)]]
    b1 = math.atan2(P1.y - Ci.y, P1.x - Ci.x); b2 = math.atan2(P2.y - Ci.y, P2.x - Ci.x)
    inner = [(Ci.x + math.cos(t) * r2, Ci.y + math.sin(t) * r2) for t in [b1 + (b2 - b1) * i / 60 for i in range(1, 60)]]
    poly = outer + inner
    prism(poly, Z0 + T - 0.002, Z0 + T + 0.004, 2)
    ob = new_obj('Sanctum_Rug', bm)
    for nm in ('Rug_Cloth', 'Rug_Trim', 'Rug_Glow'):
        ob.data.materials.append(bpy.data.materials.get(nm) or bpy.data.materials.new(nm))
    return ob


# ======================================================================
# 2) 哥特环廊：尖拱 + 八角柱
# ======================================================================
def pointed_arch_profile(span, rise, n=24):
    """尖拱中线：两段圆弧，圆心在对侧拱脚内侧（等边尖拱的变体，rise 控制尖度）"""
    half = span / 2
    # 半径 R 使得两段弧在顶点相交：R^2 = (R - half)^2 + rise^2 → R = (half^2 + rise^2) / (2 half)
    R = (half * half + rise * rise) / (2 * half)
    pts = []
    for side in (-1, 1):
        cx = side * (half - R) * -1  # 左弧圆心在右侧
        cx = (R - half) * (1 if side < 0 else -1)
        a0 = math.atan2(0, -half * side - cx) if False else None
        for i in range(n + 1):
            t = i / n
            # 从拱脚(±half,0) 到顶点(0,rise)；角度取最短方向插值，避免绕到反面
            foot_x = -half if side < 0 else half
            ang0 = math.atan2(0.0, foot_x - cx)
            ang1 = math.atan2(rise, 0.0 - cx)
            dang = math.atan2(math.sin(ang1 - ang0), math.cos(ang1 - ang0))
            ang = ang0 + dang * t
            pts.append((side, Vector((cx + math.cos(ang) * R, 0, math.sin(ang) * R))))
    left = [p for s, p in pts if s < 0]
    right = [p for s, p in pts if s > 0]
    path = left + list(reversed(right))[1:]
    out = [path[0]]
    for q in path[1:]:
        if (q - out[-1]).length > 1e-4:
            out.append(q)
    return out


def sweep_profile(bm, path, prof, close_path=False):
    """沿折线 path 扫掠二维截面 prof（局部 (u,v)，u=径向水平，v=竖直于路径的法线），返回面。"""
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        side = Vector((0, 1, 0))          # 拱在 XZ 平面，厚度方向 Y
        nrm = t.cross(side).normalized()  # 指向拱外（下方为内）
        ring = [bm.verts.new(p + nrm * u + side * v) for (u, v) in prof]
        rings.append(ring)
    m = len(prof)
    for i in range(len(rings) - 1):
        for j in range(m):
            a, b = rings[i][j], rings[i][(j + 1) % m]
            c, d = rings[i + 1][(j + 1) % m], rings[i + 1][j]
            bm.faces.new((a, b, c, d))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


def arch_rib_profile(w, d):
    """拱肋截面：带两道凹槽线脚的矩形（cavetto + roll），共 14 点"""
    hw, hd = w / 2, d / 2
    pts = [(-hw, -hd), (-hw * 0.6, -hd), (-hw * 0.45, -hd * 0.8), (-hw * 0.3, -hd * 0.85), (hw * 0.3, -hd * 0.85), (hw * 0.45, -hd * 0.8),
           (hw * 0.6, -hd), (hw, -hd), (hw, hd), (hw * 0.6, hd), (hw * 0.45, hd * 0.8), (-hw * 0.45, hd * 0.8), (-hw * 0.6, hd), (-hw, hd)]
    return pts


def octagon_column(bm, base, h, r_bot, r_top):
    """八角柱 + 柱础（三层线脚）+ 柱头（外挑托盘）"""
    def ring(z, r, n=8, rot=math.pi / 8):
        return [bm.verts.new((base.x + math.cos(rot + k / n * math.tau) * r, base.y + math.sin(rot + k / n * math.tau) * r, base.z + z)) for k in range(n)]
    profile = [(0.0, r_bot * 1.55), (0.12, r_bot * 1.55), (0.14, r_bot * 1.35), (0.24, r_bot * 1.35), (0.27, r_bot * 1.15), (0.36, r_bot * 1.1),
               (0.4, r_bot)]
    # 柱身收分 + 中段一道箍环
    body = [(0.4 + (h - 1.1) * t, r_bot + (r_top - r_bot) * t) for t in (0.0, 0.25, 0.48, 0.5, 0.52, 0.75, 1.0)]
    body[3] = (body[3][0], body[3][1] * 1.12)
    cap = [(h - 0.7, r_top), (h - 0.62, r_top * 1.2), (h - 0.45, r_top * 1.45), (h - 0.3, r_top * 1.75), (h - 0.18, r_top * 1.85), (h, r_top * 1.85)]
    prof = profile + body + cap
    rings = [ring(z, r) for z, r in prof]
    for i in range(len(rings) - 1):
        for k in range(8):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % 8], rings[i + 1][(k + 1) % 8], rings[i + 1][k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


def build_colonnade():
    bm = bmesh.new()
    R = 6.2           # 环廊半径
    H = 6.4           # 柱高
    n = 10
    # 前方（Blender +Y）±38° 留空给玫瑰窗
    angles = [k / n * math.tau for k in range(n)]
    keep = [a for a in angles if abs(math.atan2(math.sin(a - math.pi / 2), math.cos(a - math.pi / 2))) > math.radians(38)]
    cols = []
    for a in keep:
        p = Vector((math.cos(a) * R, math.sin(a) * R, 0.0))
        octagon_column(bm, p, H, 0.32, 0.27)
        cols.append((a, p))
    # 相邻柱间起尖拱（跳过前方空档）；其中两跨半毁
    broken = {1, 4}
    for i in range(len(cols) - 1):
        a0, p0 = cols[i]; a1, p1 = cols[i + 1]
        if abs(a1 - a0) > math.tau / n * 1.5:
            continue
        span = (p1 - p0).length
        mid = (p0 + p1) / 2
        ang = math.atan2(p1.y - p0.y, p1.x - p0.x)
        path_local = pointed_arch_profile(span, span * 0.95, n=18)
        Rm = Matrix.Translation(Vector((mid.x, mid.y, H))) @ Matrix.Rotation(ang, 4, 'Z')
        path = [Rm @ v for v in path_local]
        if i in broken:
            # 半毁：只保留一侧到 60% 高度，断口加噪声
            keepn = int(len(path) * 0.38)
            path = path[:keepn] if i % 2 else list(reversed(path))[:keepn]
        sweep_profile(bm, path, arch_rib_profile(0.42, 0.5))
    ob = new_obj('Colonnade_Stone', bm)
    # 拱顶中心的“楔石”：小南瓜形装饰由 P2 南瓜补
    return ob


# ======================================================================
# 3) 吊桥（单座，Unity 里复制旋转）
# ======================================================================
def build_bridge(length=5.6, sag=0.38, width=1.05):
    bm = bmesh.new()
    n = int(length / 0.24)
    for i in range(n):
        t = (i + 0.5) / n
        x = (t - 0.5) * length
        z = -sag * (1 - (2 * t - 1) ** 2)
        w = width * random.uniform(0.92, 1.05)
        missing = random.random() < 0.06 and 3 < i < n - 3
        if missing:
            continue
        c = Vector((x, random.uniform(-0.03, 0.03), z))
        dz = 2 * sag * (2 * t - 1) * 2 / length   # 板面跟悬链倾斜
        bevel_box(bm, c, (0.2, w, 0.05), rot_z=random.uniform(-0.05, 0.05), tilt=(random.uniform(-0.04, 0.04), -math.atan(dz)),
                  bevel=0.012, segs=1, chip=0.006)
    planks = new_obj('Bridge_Planks', bm)
    # 绳：两侧各一根扶手绳 + 一根承重绳，三股螺旋
    ropes = []
    for side in (-1, 1):
        for zoff, r in ((0.0, 0.035), (0.75, 0.03)):
            for strand in range(3):
                cu = bpy.data.curves.new('rope', 'CURVE'); cu.dimensions = '3D'
                sp = cu.splines.new('POLY'); m = 120
                sp.points.add(m - 1)
                for k in range(m):
                    t = k / (m - 1)
                    x = (t - 0.5) * length
                    z = -sag * (1 - (2 * t - 1) ** 2) * (0.85 if zoff > 0 else 1.0) + zoff
                    ph = t * length / 0.06 + strand * math.tau / 3
                    sp.points[k].co = (x, side * width / 2 + math.cos(ph) * r * 0.55, z + math.sin(ph) * r * 0.55, 1)
                cu.bevel_depth = r * 0.5
                cu.bevel_resolution = 2
                cu.use_fill_caps = True
                o = bpy.data.objects.new('rope', cu)
                bpy.context.scene.collection.objects.link(o)
                ropes.append(o)
    # 竖向吊绳：每 4 块板一根
    for i in range(0, n, 4):
        t = (i + 0.5) / n
        x = (t - 0.5) * length
        z = -sag * (1 - (2 * t - 1) ** 2)
        for side in (-1, 1):
            cu = bpy.data.curves.new('hang', 'CURVE'); cu.dimensions = '3D'
            sp = cu.splines.new('POLY'); sp.points.add(1)
            sp.points[0].co = (x, side * width / 2, z, 1)
            sp.points[1].co = (x, side * width / 2, z * 0.85 + 0.75, 1)
            cu.bevel_depth = 0.012; cu.bevel_resolution = 1; cu.use_fill_caps = True
            o = bpy.data.objects.new('hang', cu)
            bpy.context.scene.collection.objects.link(o)
            ropes.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in ropes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ropes[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    ropes[0].name = 'Bridge_Ropes'
    return planks, ropes[0]


# ======================================================================
# 4) 浮空蜡烛（单支，精细滴蜡；Unity 里 GPU 实例化几百支）
# ======================================================================
def build_candle():
    """一支融化的蜡烛：高密度圆柱（48×64），用位移“雕”出顶部融化唇边和流淌的蜡泪；
    蜡泪是柱面本身鼓起来的（沿流淌路径的高斯隆起，末端更胖形成泪珠），不是额外拼的球。"""
    bm = bmesh.new()
    n, rings = 48, 64
    H, Rr = 0.16, 0.022
    # 蜡泪路径：角度、长度、宽度；路径随高度轻微左右摆
    drips = []
    for d in range(6):
        drips.append((random.uniform(0, math.tau), random.uniform(0.025, 0.1), random.uniform(0.18, 0.32), random.uniform(-0.6, 0.6)))

    def radius_at(a, z):
        r = Rr * (1 + 0.02 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, z * 30))))
        depth = H - z
        for (da, L, w, wig) in drips:
            if depth < 0 or depth > L + 0.012:
                continue
            t = depth / L
            ca = da + wig * t * t * 0.3          # 路径向一侧弯
            dd = math.atan2(math.sin(a - ca), math.cos(a - ca))
            width = w * (0.85 + 0.6 * max(0.0, t - 0.75) / 0.25) if t <= 1 else w * 1.3   # 末端变宽
            g = math.exp(-(dd / width) ** 2 * 3.0)
            # 厚度：顶部薄、往下渐厚，末端泪珠最厚，之后迅速收住
            if t <= 1:
                thick = 0.0022 + 0.0028 * t + 0.0035 * max(0.0, t - 0.8) / 0.2
            else:
                thick = 0.0063 * max(0.0, 1 - (depth - L) / 0.012)
            r += thick * g
        return r

    verts = []
    for j in range(rings + 1):
        z = H * j / rings
        row = []
        for k in range(n):
            a = k / n * math.tau
            r = radius_at(a, z)
            zz = z
            # 顶缘：不规则地往上翻的融化唇边
            if j == rings:
                zz = z + 0.002 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, 5)))
                r *= 1.04
            row.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, zz)))
        verts.append(row)
    for j in range(rings):
        for k in range(n):
            bm.faces.new((verts[j][k], verts[j][(k + 1) % n], verts[j + 1][(k + 1) % n], verts[j + 1][k]))
    # 顶面：内凹的蜡池（两圈向内收、向下沉）
    inner = []
    for s_, (rf, dz) in enumerate(((0.82, -0.003), (0.45, -0.008))):
        ring = [bm.verts.new((v.co.x * rf, v.co.y * rf, H + dz)) for v in verts[-1]]
        prev = verts[-1] if s_ == 0 else inner[-1]
        for k in range(n):
            bm.faces.new((prev[k], prev[(k + 1) % n], ring[(k + 1) % n], ring[k]))
        inner.append(ring)
    top_c = bm.verts.new((0, 0, H - 0.01))
    for k in range(n):
        bm.faces.new((inner[-1][k], inner[-1][(k + 1) % n], top_c))
    bot_c = bm.verts.new((0, 0, 0))
    for k in range(n):
        bm.faces.new((verts[0][(k + 1) % n], verts[0][k], bot_c))
    # 灯芯
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.0016, radius2=0.0008, depth=0.014)
    bmesh.ops.translate(bm, vec=(0, 0, H - 0.004), verts=res['verts'])
    ob = new_obj('Candle_Float', bm)
    # 火焰单独一个物体（Unity 里用发光材质 + 摇曳）
    bm2 = bmesh.new()
    res = bmesh.ops.create_uvsphere(bm2, u_segments=12, v_segments=8, radius=0.008)
    for v in res['verts']:
        v.co.z *= 2.2
        if v.co.z > 0:
            v.co.x *= 1 - v.co.z / 0.02 * 0.6
            v.co.y *= 1 - v.co.z / 0.02 * 0.6
        v.co.z += H + 0.016
    fl = new_obj('Candle_Flame', bm2)
    return ob, fl


# ======================================================================
reset()
f = build_floor(); export([f], 'Sanctum_Floor.fbx')
reset()
r = build_rim(); export([r], 'Sanctum_Rim.fbx')
reset()
rg = build_rug(); export([rg], 'Sanctum_Rug.fbx')
reset()
u, roots = build_underside(); export([u, roots], 'Sanctum_Underside.fbx')
reset()
p, rp = build_bridge(); export([p], 'Bridge_Planks.fbx'); export([rp], 'Bridge_Ropes.fbx')
reset()
cd, fl = build_candle(); export([cd], 'Candle_Float.fbx'); export([fl], 'Candle_Flame.fbx')
print('DONE')

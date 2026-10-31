"""花园 v6 第一批（分部件程序化）：Blender -b -P Tools/blender_garden2.py -- <输出目录>
  Wl_Wall    石砌栏杆矮墙（底座 + 宝瓶栏杆 + 压顶）+ 墩柱，围住花园左右和身后（Unity 坐标）
  Wl_Pumpkin 墩柱顶上的南瓜灯（雕脸画在贴图上，朝花园内侧）
  Wl_Urn     墩柱顶上的石瓮 + Wl_Twig 瓮里插的枯枝
  Tr_DeadA/B/C 三种卡通枯树（本地原点，场景里大量摆放：墙外、远处地平线一圈）"""
import bpy, bmesh, math, sys, os, json, random
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix, noise

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)


def save_rgb(path, col):
    N = col.shape[0]
    rgba = np.concatenate([np.clip(col, 0, 1), np.ones((N, N, 1), np.float32)], -1).astype(np.float32)
    im = bpy.data.images.new(os.path.basename(path), N, N, alpha=False); im.pixels = rgba[::-1].ravel()
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()


def finish(bm, name, base=None, tex=None, uv=None, **kw):
    ob = to_obj(bm, name)
    if uv: uv(ob)
    else: uv_smart(ob)
    if tex: tex(os.path.join(OUT, f'{name}_albedo.png'))
    else: noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    export(ob, os.path.join(OUT, f'{name}.fbx'))


FX, BZ, GZ = 6.2, 4.8, -4.8
H_PLINTH, H_RAIL0, H_RAIL1 = 0.22, 0.74, 0.86

# ================= 石栏杆矮墙 =================
def wall_run(bm, a, b, piers):
    """a→b（Unity xz）一段矮墙；piers 收集墩柱位置"""
    a = Vector((a[0], 0, a[1])); b = Vector((b[0], 0, b[1]))
    L = (b - a).length; d = (b - a) / L; n = Vector((-d.z, 0, d.x))
    npier = max(1, round(L / 2.4)); seg = L / npier
    for i in range(npier + 1):
        piers.append(a + d * seg * i)
    def box_along(s0, s1, y0, y1, w, bev=0.01):
        p0 = a + d * s0; p1 = a + d * s1
        xs = sorted([p0.x - n.x * w / 2, p0.x + n.x * w / 2, p1.x - n.x * w / 2, p1.x + n.x * w / 2])
        zs = sorted([p0.z - n.z * w / 2, p0.z + n.z * w / 2, p1.z - n.z * w / 2, p1.z + n.z * w / 2])
        return ubox(xs[0], xs[-1], y0, y1, zs[0], zs[-1], bev)
    for i in range(npier):
        s0, s1 = i * seg + 0.27, (i + 1) * seg - 0.27
        merge(bm, box_along(s0, s1, 0, H_PLINTH, 0.5, 0.02))
        merge(bm, box_along(s0, s1, H_RAIL0, H_RAIL1, 0.46, 0.02))
        k = int((s1 - s0) / 0.27)
        for j in range(k):
            s = s0 + (j + 0.5) * (s1 - s0) / k; p = a + d * s
            bal = lathe([(0, H_PLINTH - 0.01), (0.07, H_PLINTH - 0.01), (0.07, H_PLINTH + 0.04), (0.045, H_PLINTH + 0.08),
                         (0.085, H_PLINTH + 0.24), (0.06, H_PLINTH + 0.4), (0.04, H_PLINTH + 0.46), (0.065, H_RAIL0 - 0.03), (0.065, H_RAIL0 + 0.01), (0, H_RAIL0 + 0.01)], 10)
            for v in bal.verts: v.co += U(p.x, 0, p.z)
            merge(bm, bal)


reset()
wb = bmesh.new(); piers = []
wall_run(wb, (-FX, GZ + 0.3), (-FX, BZ), piers)
wall_run(wb, (FX, GZ + 0.3), (FX, BZ), piers)
wall_run(wb, (-FX + 0.3, BZ), (FX - 0.3, BZ), piers)
uniq = []
for p in piers:
    if all((p - q).length > 0.5 for q in uniq): uniq.append(p)
piers = uniq
for p in piers:
    merge(wb, ubox(p.x - 0.3, p.x + 0.3, 0, 0.25, p.z - 0.3, p.z + 0.3, 0.02))
    merge(wb, ubox(p.x - 0.25, p.x + 0.25, 0.24, 1.18, p.z - 0.25, p.z + 0.25, 0.015))
    merge(wb, ubox(p.x - 0.32, p.x + 0.32, 1.17, 1.3, p.z - 0.32, p.z + 0.32, 0.02))


def stone_blocks(path):
    """石块贴图：错缝石块 + 苔藓斑"""
    N = 512; y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    row = (y // 64).astype(int); off = (row % 2) * 48
    mortar = ((y % 64) < 4) | (((x + off) % 96) < 4)
    r = np.random.default_rng(31)
    tone = r.random((N // 64 + 1, N // 96 + 2))[row, ((x + off) // 96).astype(int) % (N // 96 + 2)]
    col = np.stack([0.52 + 0.1 * tone, 0.5 + 0.09 * tone, 0.56 + 0.1 * tone], -1).astype(np.float32)
    col *= (0.88 + 0.12 * r.random((N, N, 1))).astype(np.float32)
    g = smooth_noise(N, 8, r) * 0.65 + smooth_noise(N, 24, r) * 0.35
    moss = np.clip((g - 0.6) * 5, 0, 1)[..., None]
    col = col * (1 - moss) + np.array([0.28, 0.38, 0.22], np.float32) * moss
    col[mortar] = [0.32, 0.3, 0.34]
    save_rgb(path, col)


finish(wb, 'Wl_Wall', tex=stone_blocks)

# ================= 墩柱顶：南瓜灯与石瓮交替 =================
reset()
pb = bmesh.new(); ub = bmesh.new(); tb = bmesh.new(); gb = bmesh.new()
rng = random.Random(5)
for i, p in enumerate(piers):
    top = U(p.x, 1.3, p.z)
    if i % 2 == 0:
        r = 0.26; h = r * 1.4
        pk = lathe([(0, 0)] + [(r * math.sin(math.pi * k / 16) ** 0.8, h * (1 - math.cos(math.pi * k / 16)) / 2) for k in range(1, 16)] + [(0, h * 0.94)], 36,
                   r_fn=lambda t, z: 1 - 0.08 * (0.5 - 0.5 * math.cos(8 * t)))
        # 让 UV 正面（雕脸）朝花园中心：先绕竖轴转到朝内
        ang = math.atan2(-p.x, -p.z)
        M = Matrix.Rotation(-ang, 4, 'Z')
        for v in pk.verts: v.co = M @ v.co + top - Vector((0, 0, 0.005))
        merge(pb, pk)
        # 雕刻的脸：发光薄片贴在朝花园中心的一面（三角眼、三角鼻、锯齿嘴）
        fd = Vector((p.x, p.z, 0)).normalized()              # Unity 指向原点的方向在 Blender 里
        side = fd.cross(Vector((0, 0, 1))).normalized(); up = Vector((0, 0, 1))
        cen = top + Vector((0, 0, h * 0.5))
        feats = [[(-0.05, 0.03), (0.05, 0.03), (0, 0.11)], [(-0.04, -0.035), (0.04, -0.035), (0, 0.03)]]
        mouth = [(-0.15, -0.06), (-0.1, -0.1), (-0.05, -0.08), (0, -0.12), (0.05, -0.08), (0.1, -0.1), (0.15, -0.06), (0.09, -0.15), (0, -0.17), (-0.09, -0.15)]
        for ox, poly in ((-0.09, feats[0]), (0.09, feats[0]), (0, feats[1]), (0, mouth)):
            sl = slab([(x_ + ox, y_ + 0.04) for x_, y_ in poly], 0.012)
            rot = Vector((0, 0, 1)).rotation_difference(fd).to_matrix().to_4x4()
            for v in sl.verts:
                lc = v.co.copy()
                v.co = cen + side * lc.x + up * lc.y + fd * (r * 0.93 + lc.z)
            merge(gb, sl)
        st = tube([top + Vector((0, 0, h * 0.9)), top + Vector((0.02, 0, h * 0.9 + 0.08)), top + Vector((0.05, 0, h * 0.9 + 0.12))], 0.025, 8, r_fn=lambda u: 1 - 0.4 * u)
        merge(pb, st)
    else:
        urn = lathe([(0, 0), (0.16, 0), (0.16, 0.05), (0.08, 0.1), (0.07, 0.16), (0.18, 0.26), (0.24, 0.42), (0.25, 0.5), (0.2, 0.5), (0, 0.46)], 24)
        for v in urn.verts: v.co += top
        merge(ub, urn)
        for k in range(5):
            a = rng.uniform(0, math.tau); lean = rng.uniform(0.25, 0.6); L = rng.uniform(0.5, 0.85)
            base = top + Vector((math.cos(a) * 0.06, math.sin(a) * 0.06, 0.4))
            pts = [base + Vector((math.cos(a) * lean * L * t + 0.05 * math.sin(t * 9 + k), math.sin(a) * lean * L * t, L * t)) for t in np.linspace(0, 1, 10)]
            merge(tb, tube(pts, 0.016, 6, r_fn=lambda u: 1 - 0.85 * u))


finish(pb, 'Wl_Pumpkin', (1.0, 0.5, 0.1), var=0.14, dark=(0.85, 0.36, 0.06), seed=401, cells=4)
finish(ub, 'Wl_Urn', tex=stone_blocks)
finish(gb, 'Wl_Glow', (1.0, 0.82, 0.3), var=0.05, seed=405, cells=3)
finish(tb, 'Wl_Twig', (0.2, 0.15, 0.13), var=0.15, seed=403, cells=3)

# ================= 卡通枯树生成器 =================
def dead_tree(seed, H, spread, curl=1.0, roots=6):
    """扭曲的卡通枯树：粗干倾斜扭转 → 分叉向四周横伸 → 枝梢螺旋打卷；盘根贴地蜿蜒"""
    R = random.Random(seed)
    bm = bmesh.new()
    lean = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0)).normalized() * 0.25
    def branch(p, d, L, r, depth):
        pts = [p.copy()]; dd = d.copy(); q = p.copy()
        n = 14
        wob = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0))
        sgn = 1 if R.random() < 0.4 else -1          # 多数枝梢往下卷，少数往上翘
        for i in range(n):
            t = (i + 1) / n
            dd = (dd + wob * 0.06 * math.sin(t * 7 + depth) + Vector((0, 0, 0.02 if depth else 0.0))).normalized()
            if depth >= 1 and t > 0.55:                 # 梢头打卷（越细卷得越狠）
                ax = dd.cross(Vector((0, 0, 1)))
                if ax.length > 1e-3: dd = (Matrix.Rotation(sgn * curl * (0.22 + 0.12 * depth), 3, ax.normalized()) @ dd).normalized()
            q = q + dd * (L / n); pts.append(q.copy())
        merge(bm, tube(pts, r, max(6, 12 - depth * 2), r_fn=lambda u: 1 - (0.55 if depth == 0 else 0.85) * u))
        if depth < 3:
            kids = (3, 3, 2)[depth]
            base_a = R.uniform(0, math.tau)
            for k in range(kids):
                at = int(len(pts) * (0.92 if depth == 0 else R.uniform(0.45, 0.85))) - 1
                a = base_a + k * math.tau / kids + R.uniform(-0.4, 0.4)
                hz = Vector((math.cos(a), math.sin(a), 0))
                nd = (dd * 0.45 + hz * spread + Vector((0, 0, R.uniform(-0.05, 0.3)))).normalized()
                branch(pts[at], nd, L * (0.85 if depth == 0 else R.uniform(0.55, 0.72)), r * (0.62 if depth == 0 else 0.55), depth + 1)
    trunk_dir = (Vector((0, 0, 1)) + lean).normalized()
    branch(Vector((0, 0, -0.1)), trunk_dir, H * 0.45, H * 0.075, 0)
    for k in range(roots):         # 盘根：从干基贴地蜿蜒伸出，末端钻进土里
        a = k / roots * math.tau + R.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0)); s_ = d.cross(Vector((0, 0, 1)))
        L = H * R.uniform(0.18, 0.28)
        pts = [Vector((0, 0, H * 0.12))]
        for i in range(1, 9):
            t = i / 8
            pts.append(d * L * t + s_ * L * 0.18 * math.sin(t * 5 + k) + Vector((0, 0, H * 0.12 * (1 - t) ** 2 - 0.08 * t)))
        merge(bm, tube(pts, H * 0.045, 8, r_fn=lambda u: 1 - 0.9 * u))
    return bm


for name, seed, H, sp, cu in (('Tr_DeadA', 11, 6.0, 1.6, 1.0), ('Tr_DeadB', 23, 7.5, 1.3, 0.7), ('Tr_DeadC', 37, 4.5, 1.9, 1.3)):
    reset()
    finish(dead_tree(seed, H, sp, cu), name, (0.22, 0.17, 0.16), var=0.25, dark=(0.14, 0.11, 0.11), seed=seed, streak=0.5)

json.dump(report, open(os.path.join(OUT, 'garden2_report.json'), 'w'), indent=1)
json.dump([[p.x, p.z] for p in piers], open(os.path.join(OUT, 'garden2_piers.json'), 'w'))
print('GARDEN2', {k: v['faces'] for k, v in report.items()}, 'piers', len(piers))

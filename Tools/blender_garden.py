"""庄园后花园（分部件程序化）：Blender -b -P Tools/blender_garden.py -- <输出目录>
全部用 Unity 世界坐标建（proc_lib.U 转换），装配时 keep_origin 放在原点。玩家坐在原点，面朝 -Z。
  藤架   Pg_Post 立柱 / Pg_Beam 梁与椽 / Pg_Vine 藤 / Pg_Leaf 叶 / Pg_Wire 彩灯线 / Pg_Bulb 灯泡（自发光）
         —— 椽子在 2.75~2.9m，正好当规则层的“天花板”（蜘蛛从 2.3m 垂降），两侧梁头是蝙蝠出生点
  拱门   Gt_Pillar 砖石门柱与围栏石柱 / Gt_Iron 铸铁拱、敞开的两扇门、围栏 / Gt_Finial 柱顶南瓜
         —— 门洞宽 1.85m，对应规则层的 WINDOW（被甩出的敌人从这里飞出去）
  树篱   Hd_Hedge
  路灯   Lp_Iron / Lp_Glass（本地原点，场景里多处摆放）
  地面   Gr_Lawn 草坪 / Gr_Patio 藤架下石板地 / Gr_Path 碎石小径"""
import bpy, bmesh, math, sys, os, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

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


def uv_world(scale):
    """世界平面投影 UV（地面用，贴图平铺）"""
    def f(ob):
        me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
        for p in me.polygons:
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uvl[li].uv = (co.x / scale, co.y / scale)
    return f


def heart(s):
    out = []
    for i in range(24):
        t = i / 24 * math.tau
        x = 16 * math.sin(t) ** 3; y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        out.append((x / 17 * s, -y / 17 * s))
    return out


def leaf_at(p, out, s, tilt):
    lf = slab(heart(s), 0.012, bend=0.8)
    rot = Vector((0, 0, 1)).rotation_difference(out).to_matrix().to_4x4()
    T = Matrix.Rotation(tilt, 4, 'Z')
    for v in lf.verts: v.co = rot @ (T @ v.co) + p + out * 0.04
    return lf


rng = np.random.default_rng(12)
# ================= 藤架 =================
PX, PZ = 3.1, (-4.1, -1.3, 1.5)
TOP0, TOP1 = 2.5, 2.75
reset()
bm = bmesh.new()
for sx in (-1, 1):
    for z in PZ:
        x = sx * PX
        merge(bm, ubox(x - 0.17, x + 0.17, 0, 0.3, z - 0.17, z + 0.17, 0.02))       # 柱础
        merge(bm, ubox(x - 0.11, x + 0.11, 0.28, TOP0 - 0.12, z - 0.11, z + 0.11, 0.015))
        merge(bm, ubox(x - 0.16, x + 0.16, TOP0 - 0.14, TOP0 + 0.01, z - 0.16, z + 0.16, 0.02))   # 柱头
# 弧形托架：每根柱顶沿纵梁方向（±Z）各一块，轮廓是带内凹弧线的直角三角，外加一个小涡卷
br = [(0.0, 0.0), (0.0, 0.5)] + [(0.42 - 0.42 * math.cos(a), 0.5 - 0.5 * math.sin(a)) for a in np.linspace(0.12, math.pi / 2 - 0.12, 9)] + [(0.42, 0.0)]   # 实心角撑，外侧是内凹弧
for sx in (-1, 1):
    for z in PZ:
        for dz in (-1, 1):
            if (z == PZ[0] and dz < 0) or (z == PZ[-1] and dz > 0): continue
            sl = slab([(u, -w) for u, w in br], 0.07)
            for v in sl.verts:
                u_, w_, t_ = v.co.x, v.co.y, v.co.z
                v.co = U(sx * PX + t_, TOP0 - 0.005 + w_, z + dz * (0.1 + u_))
            merge(bm, sl)
finish(bm, 'Pg_Post', (0.42, 0.34, 0.3), var=0.2, dark=(0.3, 0.24, 0.22), seed=201, streak=0.45)
reset()
bm = bmesh.new()
for sx in (-1, 1):
    merge(bm, ubox(sx * PX - 0.09, sx * PX + 0.09, TOP0, TOP1, -4.45, 1.85, 0.015))          # 两侧纵梁
for z in (PZ[0], PZ[2]):
    merge(bm, ubox(-PX - 0.35, PX + 0.35, TOP0 + 0.02, TOP1 - 0.02, z - 0.07, z + 0.07, 0.015))   # 前后横梁
z = -4.3
while z < 1.75:
    merge(bm, ubox(-PX - 0.45, PX + 0.45, TOP1 - 0.01, TOP1 + 0.14, z - 0.04, z + 0.04, 0.01))     # 椽子
    z += 0.95
finish(bm, 'Pg_Beam', (0.4, 0.32, 0.28), var=0.2, dark=(0.28, 0.22, 0.2), seed=203, streak=0.5)
# 藤：沿两侧纵梁垂挂 + 绕前排立柱往上爬；叶子单独一个部件
reset()
vb = bmesh.new(); lb = bmesh.new()
for sx in (-1, 1):
    for k in range(5):
        z0 = -4.3 + k * 1.2; z1 = z0 + 1.2
        pts = catenary(U(sx * (PX + 0.02), TOP0 - 0.02, z0), U(sx * (PX + 0.02), TOP0 - 0.02, z1), 0.28, 18)
        merge(vb, tube(pts, 0.022, 7))
        for i in range(2, 18, 3):
            out = Vector((-sx, 0, 0)) if i % 2 else Vector((0, 0, -1))
            merge(lb, leaf_at(pts[i], out if out.z == 0 else out, 0.11, rng.uniform(-0.6, 0.6)))
    for z in (PZ[0], PZ[1]):
        x = sx * PX
        hel = [U(x + 0.14 * math.cos(t * 0.5 + 1), 0.25 + t * 0.075, z + 0.14 * math.sin(t * 0.5 + 1)) for t in np.linspace(0, 29, 90)]
        merge(vb, tube(hel, 0.02, 7))
        for i in range(4, 90, 7):
            p = hel[i]; out = (p - U(x, (p.z), z)); out.z = 0
            out = Vector((p.x - U(x, 0, z).x, p.y - U(x, 0, z).y, 0)).normalized()
            merge(lb, leaf_at(p, out, 0.1, rng.uniform(-0.8, 0.8)))
finish(vb, 'Pg_Vine', (0.22, 0.4, 0.16), var=0.2, seed=205, cells=6)
finish(lb, 'Pg_Leaf', (0.3, 0.58, 0.22), var=0.22, dark=(0.2, 0.42, 0.16), seed=207, cells=5)
# 彩灯：三串横跨藤架、微微下垂
reset()
wb = bmesh.new(); bb = bmesh.new(); bp = bmesh.new()
for z in (-3.55, -1.9, -0.25, 1.1):
    pts = catenary(U(-PX, TOP0 - 0.02, z), U(PX, TOP0 - 0.02, z), 0.3, 40)
    merge(wb, tube(pts, 0.006, 6))
    for i in range(2, 40, 3):
        b = lathe([(0, -0.05), (0.022, -0.035), (0.026, -0.015), (0.012, 0.0), (0, 0.004)], 10)
        for v in b.verts: v.co += pts[i]
        merge(bb if (i // 3) % 2 == 0 else bp, b)
# 顶上每根椽子底下再挂一串扇形小彩灯：每 ~0.8m 钉一下、中间微垂，灯泡约 0.2m 一颗
z = -4.3; ri = 0
while z < 1.75:
    xs = np.linspace(-PX - 0.25, PX + 0.25, 9)
    pts = []
    for j in range(len(xs) - 1):
        seg = catenary(U(xs[j], TOP1 - 0.016, z), U(xs[j + 1], TOP1 - 0.016, z), 0.07, 8)
        pts += seg if j == 0 else seg[1:]
    merge(wb, tube(pts, 0.005, 6))
    for i in range(1, len(pts) - 1, 2):
        if i % 8 == 0: continue                     # 钉点处不挂灯泡
        b = lathe([(0, -0.042), (0.018, -0.03), (0.021, -0.013), (0.01, 0.0), (0, 0.004)], 10)
        for v in b.verts: v.co += pts[i]
        merge(bb if (i // 2 + ri) % 2 == 0 else bp, b)
    z += 0.95; ri += 1
finish(wb, 'Pg_Wire', (0.12, 0.1, 0.12), var=0.05, seed=209, cells=3)
finish(bb, 'Pg_Bulb', (1.0, 0.6, 0.2), var=0.05, seed=211, cells=3)
finish(bp, 'Pg_BulbP', (0.75, 0.45, 1.0), var=0.05, seed=212, cells=3)

# ================= 拱门 + 围栏 =================
GZ, GX, FX = -4.8, 1.15, 6.2
reset()
pb = bmesh.new()
for sx in (-1, 1):
    x = sx * GX
    merge(pb, ubox(x - 0.27, x + 0.27, 0, 0.25, GZ - 0.27, GZ + 0.27, 0.02))
    merge(pb, ubox(x - 0.225, x + 0.225, 0.24, 2.2, GZ - 0.225, GZ + 0.225, 0.015))
    merge(pb, ubox(x - 0.3, x + 0.3, 2.19, 2.33, GZ - 0.3, GZ + 0.3, 0.02))
    for fx in (3.4, 4.8, FX):
        merge(pb, ubox(sx * fx - 0.16, sx * fx + 0.16, 0, 1.45, GZ - 0.16, GZ + 0.16, 0.015))
        merge(pb, ubox(sx * fx - 0.21, sx * fx + 0.21, 1.44, 1.54, GZ - 0.21, GZ + 0.21, 0.015))


def bricks(path):
    N = 512; y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    row = (y // 32).astype(int); off = (row % 2) * 32
    mortar = ((y % 32) < 3) | (((x + off) % 64) < 3)
    base = np.array([0.5, 0.3, 0.26], np.float32)
    col = np.tile(base, (N, N, 1)) * (0.85 + 0.3 * np.random.default_rng(3).random((N // 32 + 1, N // 64 + 2))[row, ((x + off) // 64).astype(int)][..., None])
    col *= (0.92 + 0.08 * np.random.default_rng(4).random((N, N, 1)))
    col[mortar] = [0.62, 0.58, 0.55]
    save_rgb(path, col.astype(np.float32))


finish(pb, 'Gt_Pillar', tex=bricks)
reset()
ib = bmesh.new()
# 拱：两道同心弧 + 竖条 + 两端卷草
for r_, yb in ((0.925, 2.05), (0.8, 2.05)):
    arc = [U(math.cos(a) * r_, yb + math.sin(a) * 0.62 * r_ / 0.925, GZ) for a in np.linspace(0, math.pi, 30)]
    merge(ib, tube(arc, 0.022, 8))
for a in np.linspace(0.25, math.pi - 0.25, 9):
    merge(ib, tube([U(math.cos(a) * 0.8, 2.05 + math.sin(a) * 0.62 * 0.8 / 0.925, GZ), U(math.cos(a) * 0.925, 2.05 + math.sin(a) * 0.62, GZ)], 0.012, 6))
for sx in (-1, 1):
    cur = [U(sx * (0.9 - 0.18 * t / 6.28 * (1 + math.cos(t)) * 0.5 - 0.0), 2.0 - 0.1 * math.sin(t) * (1 - t / 7), GZ) for t in np.linspace(0, 6.0, 30)]
    merge(ib, tube(cur, 0.012, 6))


def panel(hinge_x, dirx, ang, w=0.9, h=1.6):
    """一扇铁门：边框 + 竖条带尖头，绕门轴向外（-Z）打开 ang 度"""
    d = Vector((math.cos(ang) * dirx, 0, -math.sin(ang)))      # Unity 平面方向
    def P(s, y): return U(hinge_x + d.x * s, y, GZ + d.z * s)
    b = bmesh.new()
    for y in (0.12, 0.85, h):
        merge(b, tube([P(0.03, y), P(w, y)], 0.016, 6))
    for s in (0.03, w):
        merge(b, tube([P(s, 0.1), P(s, h + 0.02)], 0.02, 6))
    s = 0.15
    while s < w - 0.05:
        merge(b, tube([P(s, 0.12), P(s, h + 0.12)], 0.011, 6))
        tip = lathe([(0, 0), (0.025, 0.0), (0, 0.07)], 6)
        for v in tip.verts: v.co = Vector((v.co.x, v.co.y, v.co.z)) + P(s, h + 0.12)
        merge(b, tip)
        s += 0.12
    return b


merge(ib, panel(-0.925, 1, math.radians(72)))
merge(ib, panel(0.925, -1, math.radians(72)))
# 围栏：门柱到两侧石柱之间
for sx in (-1, 1):
    segs = [(GX + 0.23, 3.4 - 0.16), (3.4 + 0.16, 4.8 - 0.16), (4.8 + 0.16, FX - 0.16)]
    for a, b in segs:
        x0, x1 = sx * a, sx * b
        for y in (0.22, 1.05):
            merge(ib, tube([U(x0, y, GZ), U(x1, y, GZ)], 0.014, 6))
        n = int(abs(x1 - x0) / 0.13)
        for i in range(1, n):
            x = x0 + (x1 - x0) * i / n
            merge(ib, tube([U(x, 0.05, GZ), U(x, 1.2, GZ)], 0.01, 6))
            tip = lathe([(0, 0), (0.022, 0.0), (0, 0.065)], 6)
            for v in tip.verts: v.co += U(x, 1.2, GZ)
            merge(ib, tip)
finish(ib, 'Gt_Iron', (0.13, 0.11, 0.15), var=0.12, seed=213, cells=4)
reset()
fb = bmesh.new()
for sx in (-1, 1):
    for x, y, r in ((sx * GX, 2.33, 0.2), (sx * 3.4, 1.54, 0.13), (sx * 4.8, 1.54, 0.13), (sx * FX, 1.54, 0.13)):
        pk = lathe([(0, 0)] + [(r * math.sin(math.pi * k / 14) ** 0.8, r * 1.5 * (1 - math.cos(math.pi * k / 14)) / 2) for k in range(1, 14)] + [(0, r * 1.5 * 0.94)], 28,
                   r_fn=lambda t, z: 1 - 0.08 * (0.5 - 0.5 * math.cos(8 * t)))
        for v in pk.verts: v.co += U(x, y - 0.005, GZ)
        merge(fb, pk)
finish(fb, 'Gt_Finial', (1.0, 0.5, 0.1), var=0.14, dark=(0.85, 0.36, 0.06), seed=215, cells=4)

# ================= 树篱（主体 + 顶上一簇簇叶团，轮廓不规则）=================
reset()
hb = bmesh.new()
k = 0
def hedge_seg(c, sx_, sz_):
    global k
    merge(hb, blob(*c, sx_, sz_, 1.25, amp=0.11, freq=3.2, cuts=10, seed=k)); k += 1
    for j in range(3):     # 顶部叶团
        off = Vector(((rng.random() - 0.5) * sx_ * 0.6, (rng.random() - 0.5) * sz_ * 0.6, 0.55 + rng.random() * 0.15))
        r = 0.32 + rng.random() * 0.12
        merge(hb, blob(c.x + off.x, c.y + off.y, c.z + off.z, r * 2, r * 2, r * 1.6, amp=0.06, freq=5.0, cuts=6, seed=k)); k += 1
for sx in (-1, 1):
    z = -4.45
    while z < 4.6:
        hedge_seg(U(sx * FX, 0.62, z + 0.55), 1.0, 1.15); z += 1.0
x = -FX
while x < FX + 0.01:
    hedge_seg(U(x, 0.62, 4.85), 1.15, 1.0); x += 1.05
finish(hb, 'Hd_Hedge', (0.2, 0.44, 0.17), var=0.35, dark=(0.11, 0.27, 0.1), seed=217, cells=18)

# ================= 维多利亚路灯（本地原点）=================
reset()
lb = lathe([(0, 0), (0.2, 0), (0.2, 0.08), (0.14, 0.14), (0.12, 0.3), (0.06, 0.38), (0.055, 2.3), (0.08, 2.35), (0.08, 2.4), (0, 2.4)], 16,
           r_fn=lambda t, z: 1 + (0.12 * math.cos(8 * t) if 0.4 < z < 2.2 else 0))
cage = lathe([(0, 2.38), (0.15, 2.4), (0.15, 2.44), (0.03, 2.46), (0, 2.46)], 8)
merge(lb, cage)
top = lathe([(0, 2.86), (0.2, 2.86), (0.22, 2.9), (0.05, 3.08), (0.025, 3.18), (0, 3.18)], 8)
merge(lb, top)
for k in range(4):     # 灯笼四根角柱
    a = math.pi / 4 + k * math.pi / 2
    merge(lb, tube([Vector((math.cos(a) * 0.14, math.sin(a) * 0.14, 2.44)), Vector((math.cos(a) * 0.19, math.sin(a) * 0.19, 2.88))], 0.012, 6))
finish(lb, 'Lp_Iron', (0.12, 0.11, 0.14), var=0.1, seed=219, cells=4)
reset()
gl = lathe([(0, 2.45), (0.13, 2.45), (0.175, 2.86), (0, 2.86)], 8)
finish(gl, 'Lp_Glass', (1.0, 0.86, 0.55), var=0.05, seed=221, cells=3)

# ================= 地面 =================
reset()
prof = [(0, -0.3)] + [(r, -0.3) for r in (60,)] + [(60, -0.02)] + [(r, 0.0) for r in (40, 20, 10, 6, 4, 2)] + [(0, 0.0)]
lawn = lathe(prof, 64)
finish(lawn, 'Gr_Lawn', (0.2, 0.22, 0.12), var=0.4, dark=(0.13, 0.14, 0.08), seed=223, cells=10, uv=uv_world(3.0))


def flagstones(path):
    N = 512; g = 6; r2 = np.random.default_rng(9)
    pts = (np.stack(np.meshgrid(np.arange(g), np.arange(g)), -1).reshape(-1, 2) + 0.15 + 0.7 * r2.random((g * g, 2))) / g
    y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N
    d = np.full((N, N, 2), 9.0, np.float32); idx = np.zeros((N, N), int)
    for i, (px, py) in enumerate(pts):
        dx = np.abs(x - px); dx = np.minimum(dx, 1 - dx); dy = np.abs(y - py); dy = np.minimum(dy, 1 - dy)
        dd = np.sqrt(dx * dx + dy * dy)
        closer = dd < d[..., 0]
        d[..., 1] = np.where(closer, d[..., 0], np.minimum(d[..., 1], dd))
        idx = np.where(closer, i, idx); d[..., 0] = np.where(closer, dd, d[..., 0])
    edge = (d[..., 1] - d[..., 0]) < 0.012
    tone = r2.random(g * g)[idx]
    col = np.stack([0.4 + 0.12 * tone, 0.38 + 0.1 * tone, 0.43 + 0.1 * tone], -1).astype(np.float32)
    col *= (0.9 + 0.1 * r2.random((N, N, 1))).astype(np.float32)
    col[edge] = [0.2, 0.26, 0.16]
    save_rgb(path, col)


reset()
patio = lathe([(0, 0.0), (3.95, 0.0), (3.95, 0.008), (0, 0.008)], 64, r_fn=lambda t, z: 1 + 0.02 * math.sin(7 * t))
for v in patio.verts: v.co += U(0, 0, -1.3)
finish(patio, 'Gr_Patio', tex=flagstones, uv=uv_world(2.2))
reset()
path = ubox(-0.85, 0.85, 0.0, 0.003, -36.3, -5.1)


def gravel(path):
    """碎石：深灰底 + 上千颗带明暗的小卵石（可平铺）"""
    N = 1024; r = np.random.default_rng(17); y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    col = np.full((N, N, 3), [0.24, 0.23, 0.25], np.float32) * (0.9 + 0.1 * r.random((N, N, 1))).astype(np.float32)
    for _ in range(2600):
        cx, cy = r.uniform(0, N), r.uniform(0, N); rr = r.uniform(4, 11)
        tone = r.choice([0.42, 0.5, 0.58, 0.36, 0.62]); warm = r.uniform(-0.04, 0.05)
        x0, x1, y0, y1 = int(cx - rr - 1), int(cx + rr + 2), int(cy - rr - 1), int(cy + rr + 2)
        for ox in (-N, 0, N):
            for oy in (-N, 0, N):
                xa, xb, ya, yb = max(0, x0 + ox), min(N, x1 + ox), max(0, y0 + oy), min(N, y1 + oy)
                if xa >= xb or ya >= yb: continue
                sx_, sy_ = x[ya:yb, xa:xb] - cx - ox, y[ya:yb, xa:xb] - cy - oy
                d = (sx_ * sx_ + sy_ * sy_ * 1.3) / (rr * rr)
                m = d < 1
                shade = (1.15 - 0.45 * np.clip((sx_ + sy_) / rr * 0.5 + 0.5, 0, 1))
                c = np.stack([tone + warm, tone, tone - warm], -1) * shade[..., None]
                blk = col[ya:yb, xa:xb]; blk[m] = c[m]
    save_rgb(path, col)


finish(path, 'Gr_Path', tex=gravel, uv=uv_world(1.2))
# 路沿石 / 石板地边石 / 喷泉广场边石（一块块略不规则的圆角石）
reset()
cb = bmesh.new(); kk = 0
for sx in (-1, 1):
    z = -5.3
    while z > -36.2:
        merge(cb, blob(*U(sx * 0.93, 0.035, z), 0.2, 0.448, 0.12, amp=0.01, freq=7, cuts=2, seed=kk)); z -= 0.46; kk += 1


def ring_curb(cx, cz, R, gaps, kk, depth=0.24, h=0.13):
    """圆环边石：一圈楔形石（内窄外宽）沿切线紧密拼接，石缝约 1.5cm；gaps=[(角度°, 半宽°)] 处留口"""
    n = max(12, round(math.tau * R / 0.46))
    step = math.tau / n
    for k in range(n):
        a = (k + 0.5) * step
        deg = math.degrees(a)
        if any(abs((deg - g + 180) % 360 - 180) < w for g, w in gaps): continue
        w = step * R - 0.015
        b_ = blob(0, 0, 0.04, w, depth, h, amp=0.012, freq=7, cuts=3, seed=kk); kk += 1
        for v in b_.verts: v.co.x *= (R + v.co.y) / R               # 局部 X=切向，Y=径向朝外：按半径把石块做成楔形
        p = U(cx + math.cos(a) * R, 0, cz + math.sin(a) * R)
        ang = math.atan2(p.y - U(cx, 0, cz).y, p.x - U(cx, 0, cz).x) - math.pi / 2   # 局部 +Y 转到径向朝外
        M = Matrix.Translation(Vector((p.x, p.y, 0))) @ Matrix.Rotation(ang, 4, 'Z')
        for v in b_.verts: v.co = M @ v.co
        merge(cb, b_)
    return kk


kk = ring_curb(0, -1.3, 4.07, [(270, 13)], kk)                    # 藤架石板地边石：正前方（-Z）接小径处留口
kk = ring_curb(0, -16, 4.42, [(90, 12), (270, 12)], kk)           # 喷泉广场边石：前后两个口
finish(cb, 'Gr_Curb', (0.27, 0.26, 0.3), var=0.25, dark=(0.2, 0.22, 0.18), seed=227, cells=12)

json.dump(report, open(os.path.join(OUT, 'garden_report.json'), 'w'), indent=1)
print('GARDEN', {k: v['faces'] for k, v in report.items()})

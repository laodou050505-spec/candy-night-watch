"""花园 v6 第二批（分部件程序化）：Blender -b -P Tools/blender_garden3.py -- <输出目录>
墙外三面 + 前方的成组场景（全部 Unity 坐标，keep_origin 装配；可复用的小件在本地原点，装配时按实例摆放）：
  左·山坡墓园   Gv_Hill 山坡 / Gv_Fence 铁栅栏（随坡起伏）/ Mz_Stone Mz_Roof Mz_Door 希腊复兴式陵墓
               Gv_Round Gv_Cross Gv_Obelisk Gv_Slab 四种墓碑（本地原点）
  右·南瓜田     Pp_Soil 田垄 / Pp_Vine Pp_Leaf 地上的藤叶 / Pp_Pumpkin Pp_Stem 南瓜（本地原点）
               Pp_Hay 草垛 / Pp_Corn 玉米秆束 / Pp_Cart 木推车（本地原点）
  后·玻璃温室   Gh_Base 砖基 / Gh_Frame 铁架 / Gh_Glass 发光玻璃（贴图里画着植物剪影）/ Gh_Door
  前·喷泉广场   Ft_Plaza 碎石圆场 / Ft_Stone 三层喷泉 / Ft_Water 水面"""
import bpy, bmesh, math, sys, os, json, random
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)
R = random.Random(77)
LAYOUT = {}       # 给装配 JSON 用的实例位置


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
    def f(ob):
        me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
        for p in me.polygons:
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uvl[li].uv = (co.x / scale, co.y / scale)
    return f


def stand(bm, normal_b):
    """XY 薄片（法线 +Z）立起来，法线转到 Blender 方向 normal_b（水平）"""
    M = Matrix.Rotation(math.radians(90), 4, 'X')        # +Z → -Y
    ang = math.atan2(normal_b.y, normal_b.x) - math.atan2(-1, 0)
    Rz = Matrix.Rotation(ang, 4, 'Z')
    for v in bm.verts: v.co = Rz @ (M @ v.co)
    return bm


def pumpkin(r, h, seg=32, ribs=8):
    return lathe([(0, 0)] + [(r * math.sin(math.pi * k / 14) ** 0.8, h * (1 - math.cos(math.pi * k / 14)) / 2) for k in range(1, 14)] + [(0, h * 0.94)], seg,
                 r_fn=lambda t, z: 1 - 0.09 * (0.5 - 0.5 * math.cos(ribs * t)))


# ================= 左·山坡墓园 =================
HC = (-15.0, -1.0); HR, HH = 10.5, 1.3


def hill_y(x, z):
    d = math.hypot(x - HC[0], z - HC[1]) / HR
    return HH * max(0.0, 1 - d * d) ** 1.5 - 0.02


reset()
prof = [(0, -0.3), (HR + 0.6, -0.3), (HR + 0.6, -0.02)] + [(HR * (1 - k / 12), hill_y(HC[0] + HR * (1 - k / 12), HC[1])) for k in range(1, 12)] + [(0, HH - 0.02)]
hill = lathe(prof, 48, r_fn=lambda t, z: 1 + 0.05 * math.sin(3 * t + 0.4))
for v in hill.verts: v.co += U(HC[0], 0, HC[1])
from mathutils.bvhtree import BVHTree
_HB = BVHTree.FromBMesh(hill)


def ground(x, z):
    """山坡网格的实际表面高度（三角面 + 半径起伏后与解析的 hill_y 有出入，摆东西一律按网格打射线）"""
    loc, *_ = _HB.ray_cast(U(x, 50, z), Vector((0, 0, -1)), 100)
    return loc.z if loc else hill_y(x, z)


def ground_min(x, z, r=0.22):
    """脚印范围内最低处：摆件底面按它放，再稍微压进土里，斜坡上也不会有一边悬空"""
    return min(ground(x + dx, z + dz) for dx, dz in ((0, 0), (r, 0), (-r, 0), (0, r), (0, -r)))
finish(hill, 'Gv_Hill', (0.19, 0.3, 0.16), var=0.35, dark=(0.12, 0.2, 0.11), seed=501, cells=10, uv=uv_world(3.0))
# 墓地范围（栅栏围起来）：x -21..-9.5，z -8..6；入口朝 +X（朝花园）
GX0, GX1, GZ0, GZ1 = -21.0, -9.5, -8.0, 6.0
reset()
fb = bmesh.new()
def fence_line(a, b, gap=None):
    n = int((b - a).length / 0.16)
    prev = None
    for i in range(n + 1):
        p = a + (b - a) * i / n
        if gap and gap[0] < p.z < gap[1] and abs(p.x - GX1) < 0.01: prev = None; continue
        y = hill_y(p.x, p.z)
        merge(fb, tube([U(p.x, y - 0.08, p.z), U(p.x, y + 1.05, p.z)], 0.012, 6))
        tip = lathe([(0, 0), (0.024, 0), (0, 0.07)], 6)
        for v in tip.verts: v.co += U(p.x, y + 1.05, p.z)
        merge(fb, tip)
    for hy in (0.2, 0.9):
        segs, cur = [], []
        for i in range(n + 1):
            p = a + (b - a) * i / n
            if gap and gap[0] < p.z < gap[1] and abs(p.x - GX1) < 0.01:
                if len(cur) > 1: segs.append(cur)
                cur = []; continue
            cur.append(U(p.x, hill_y(p.x, p.z) + hy, p.z))
        if len(cur) > 1: segs.append(cur)
        for sgm in segs: merge(fb, tube(sgm, 0.014, 6))
corners = [Vector((GX0, 0, GZ0)), Vector((GX1, 0, GZ0)), Vector((GX1, 0, GZ1)), Vector((GX0, 0, GZ1))]
fence_line(corners[0], corners[1]); fence_line(corners[1], corners[2], gap=(-2.0, 0.0)); fence_line(corners[2], corners[3]); fence_line(corners[3], corners[0])
for c in corners + [Vector((GX1, 0, -2.0)), Vector((GX1, 0, 0.0))]:      # 角柱 + 门柱
    y = hill_y(c.x, c.z)
    merge(fb, ubox(c.x - 0.09, c.x + 0.09, y - 0.1, y + 1.25, c.z - 0.09, c.z + 0.09, 0.01))
    ball = lathe([(0, 0), (0.07, 0.04), (0.08, 0.1), (0.04, 0.17), (0, 0.19)], 10)
    for v in ball.verts: v.co += U(c.x, y + 1.25, c.z)
    merge(fb, ball)
arch = [U(GX1, hill_y(GX1, -1) + 1.25 + 0.45 * math.sin(math.pi * t), -2.0 + 2.0 * t) for t in np.linspace(0, 1, 16)]
merge(fb, tube(arch, 0.02, 6))
finish(fb, 'Gv_Fence', (0.13, 0.11, 0.15), var=0.12, seed=503, cells=4)
# 希腊复兴式陵墓：坐落在坡顶后方，正面朝 +X（朝花园和墓园入口）
MX, MZ = -18.2, -0.8
MY = hill_y(MX, MZ) - 0.1
reset()
st = bmesh.new()
for k in range(3):     # 三级台阶（往 +X 方向逐级下降、嵌套）
    merge(st, ubox(MX - 2.4 - 0.02 * k, MX + 2.6 + 0.45 * k, MY - 0.4 - 0.02 * k, MY + 0.45 - 0.15 * k, MZ - 2.2 - 0.03 * k, MZ + 2.2 + 0.03 * k, 0.02))
merge(st, ubox(MX - 2.2, MX + 0.6, MY + 0.44, MY + 3.4, MZ - 1.9, MZ + 1.9, 0.02))           # 墓室
for zc in (-1.6, -0.55, 0.55, 1.6):                                                        # 门廊四柱
    col = lathe([(0, MY + 0.44), (0.24, MY + 0.44), (0.24, MY + 0.6), (0.18, MY + 0.66), (0.17, MY + 3.1), (0.24, MY + 3.2), (0.24, MY + 3.35), (0, MY + 3.35)], 16,
                r_fn=lambda t, z: 1 + (0.06 * math.cos(12 * t) if MY + 0.7 < z < MY + 3.05 else 0))
    for v in col.verts: v.co += U(MX + 1.75, 0, MZ + zc)
    merge(st, col)
merge(st, ubox(MX - 2.35, MX + 2.15, MY + 3.33, MY + 3.65, MZ - 2.15, MZ + 2.15, 0.02))       # 檐部
for zc in (-2.0, 2.0):                                                                     # 角上石瓮
    urn = lathe([(0, 0), (0.15, 0), (0.15, 0.05), (0.07, 0.1), (0.2, 0.28), (0.21, 0.4), (0, 0.38)], 16)
    for v in urn.verts: v.co += U(MX + 1.9, MY + 0.45, MZ + zc)
    merge(st, urn)
def stone_tex(path):
    N = 512; r = np.random.default_rng(41); y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    row = (y // 85).astype(int); off = (row % 2) * 64
    mortar = ((y % 85) < 3) | (((x + off) % 128) < 3)
    col = np.full((N, N, 3), [0.56, 0.55, 0.6], np.float32) * (0.85 + 0.15 * r.random((N, N, 1))).astype(np.float32)
    g = smooth_noise(N, 8, r)
    moss = np.clip((g - 0.62) * 4, 0, 1)[..., None]
    col = col * (1 - moss) + np.array([0.3, 0.4, 0.24], np.float32) * moss
    col[mortar] = [0.36, 0.35, 0.4]
    save_rgb(path, col)
finish(st, 'Mz_Stone', tex=stone_tex)
reset()
# 屋顶：两坡 + 山墙三角（被下面石质山花遮住）；鱼鳞石板瓦贴图按坡面展开，瓦行平行屋檐；屋脊压一道圆脊瓦
RX0, RX1, RY0, RY1, RW = MX - 2.4, MX + 2.25, MY + 3.63, MY + 4.55, 2.2
rf = bmesh.new()
pa = [U(RX1, RY0, MZ + z_) for z_ in (-RW, RW)] + [U(RX1, RY1, MZ)]
pb_ = [U(RX0, RY0, MZ + z_) for z_ in (-RW, RW)] + [U(RX0, RY1, MZ)]
vf = [rf.verts.new(p) for p in pa]; vb = [rf.verts.new(p) for p in pb_]
rf.faces.new(vf); rf.faces.new(vb[::-1])
for i in range(3):
    j = (i + 1) % 3; rf.faces.new((vf[j], vf[i], vb[i], vb[j]))
merge(rf, tube([U(RX0 - 0.05, RY1 + 0.02, MZ), U(RX1 + 0.05, RY1 + 0.02, MZ)], 0.07, 10))
SL = math.hypot(RW, RY1 - RY0)


def roof_uv(ob):
    me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
    cy = U(0, 0, MZ).y
    for p in me.polygons:
        slope = abs(p.normal.z) > 0.2 and abs(p.normal.z) < 0.95
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if slope: uvl[li].uv = (co.x / 1.2, (RW - abs(co.y - cy)) / RW * SL / 1.2)
            else: uvl[li].uv = (co.y / 1.2, co.z / 1.2)


def slate_tex(path):
    """鱼鳞石板瓦：一行行半圆瓦片（1.2m 一个周期：6 片 × 10 行），每片明暗不同，瓦缝深色，零星青苔"""
    N = 1024; r = np.random.default_rng(55); y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    TW, TH = N / 6, N / 10
    row = np.floor(y / TH).astype(int); off = (row % 2) * TW / 2
    cx = np.floor((x + off) / TW); lx = (x + off) - cx * TW - TW / 2; ly = y - row * TH
    # 瓦片下缘是半圆：越靠行底越窄
    edge = np.sqrt(np.clip(1 - (lx / (TW / 2)) ** 2, 0, 1)) * TH * 0.55 + TH * 0.45
    inside = ly < edge
    tone = r.random((12, 8)).astype(np.float32)
    t = tone[row % 12, (cx.astype(int)) % 8]
    col = np.stack([0.27 + 0.08 * t, 0.28 + 0.08 * t, 0.36 + 0.09 * t], -1)
    col *= (1.0 - 0.25 * (ly / TH))[..., None]                       # 每片上浅下深，像压在上一片底下
    col[~inside] *= 0.55
    seam = (np.abs(ly - edge) < 3) | (np.abs(np.abs(lx) - TW / 2) < 2)
    col[seam] = [0.13, 0.13, 0.18]
    g = smooth_noise(N, 10, r)
    moss = np.clip((g - 0.66) * 4, 0, 0.8)[..., None]
    col = col * (1 - moss) + np.array([0.28, 0.38, 0.22], np.float32) * moss
    save_rgb(path, col.astype(np.float32))


finish(rf, 'Mz_Roof', tex=slate_tex, uv=roof_uv)
# 正面山花（石材）：内凹三角山花板 + 两道斜檐口 + 顶上与两角的石饰（顶部棕叶饰、两角小瓮）+ 山花中间一圈月桂环
reset()
pd = bmesh.new()
ped_h = (RY1 - RY0) * 0.86
tri = slab([(-RW * 0.9, 0), (RW * 0.9, 0), (0, ped_h)], 0.08)          # 局部 XY 平面
for v in tri.verts: v.co = Matrix.Rotation(math.pi / 2, 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'X') @ v.co
for v in tri.verts: v.co += U(RX1 + 0.02, RY0 + 0.02, MZ)
merge(pd, tri)
for sz in (-1, 1):                                                    # 斜檐口：从檐角斜上到屋脊
    a0 = U(RX1 + 0.08, RY0 - 0.02, MZ + sz * (RW + 0.12)); a1 = U(RX1 + 0.08, RY1 + 0.06, MZ)
    merge(pd, tube([a0, a1], 0.09, 4))
apex = lathe([(0, 0), (0.16, 0), (0.16, 0.08), (0.06, 0.14), (0.22, 0.4), (0.12, 0.62), (0.03, 0.75), (0, 0.78)], 12)
for v in apex.verts: v.co += U(RX1 + 0.05, RY1 + 0.02, MZ)
merge(pd, apex)
for sz in (-1, 1):
    u2 = lathe([(0, 0), (0.12, 0), (0.12, 0.05), (0.05, 0.09), (0.15, 0.22), (0.15, 0.3), (0, 0.29)], 12)
    for v in u2.verts: v.co += U(RX1 + 0.05, RY0 - 0.03, MZ + sz * (RW - 0.05))
    merge(pd, u2)
wr = torus(0.26, 0.045, 24, 8)                                       # 月桂环（竖立，朝 +X）
for v in wr.verts: v.co = Matrix.Rotation(math.pi / 2, 4, "Y") @ v.co + U(RX1 + 0.09, RY0 + ped_h * 0.38, MZ)
merge(pd, wr)
finish(pd, 'Mz_Pediment', tex=stone_tex)
reset()
dr = ubox(MX + 0.55, MX + 0.66, MY + 0.45, MY + 2.55, MZ - 0.6, MZ + 0.6)
finish(dr, 'Mz_Door', (0.18, 0.32, 0.26), var=0.2, seed=507, streak=0.4)
# 墓碑：四种，本地原点（正面朝 Unity +Z）
def outline_round(w, h):
    return [(-w / 2, 0), (w / 2, 0)] + [(w / 2 * math.cos(a), h - w / 2 + w / 2 * math.sin(a)) for a in np.linspace(0, math.pi, 14)]
reset()
b = slab(outline_round(0.6, 0.95), 0.14); stand(b, Vector((0, -1, 0)))
merge(b, ubox(-0.38, 0.38, -0.25, 0.08, -0.14, 0.14, 0.02))
finish(b, 'Gv_Round', tex=stone_tex)
reset()
b = ubox(-0.07, 0.07, -0.25, 1.15, -0.07, 0.07, 0.015); merge(b, ubox(-0.32, 0.32, 0.7, 0.84, -0.065, 0.065, 0.015))
merge(b, ubox(-0.22, 0.22, -0.3, 0.06, -0.18, 0.18, 0.02))
finish(b, 'Gv_Cross', tex=stone_tex)
reset()
b = lathe([(0, -0.3), (0.3, -0.3), (0.3, 0.1), (0.2, 0.15), (0.17, 1.4), (0.0, 1.62)], 4)
for v in b.verts: v.co = Matrix.Rotation(math.pi / 4, 4, 'Z') @ v.co
finish(b, 'Gv_Obelisk', tex=stone_tex)
reset()
b = slab([(-0.35, 0), (0.35, 0), (0.33, 0.62), (0.15, 0.7), (-0.2, 0.66), (-0.34, 0.55)], 0.16); stand(b, Vector((0, -1, 0)))
merge(b, ubox(-0.4, 0.4, -0.25, 0.05, -0.15, 0.15, 0.02))
finish(b, 'Gv_Slab', tex=stone_tex)
graves = []
for row, xr in enumerate((-10.6, -12.3, -14.0, -15.7)):
    for k in range(7):
        z = -7.0 + k * 2.0 + (0.9 if row % 2 else 0) + R.uniform(-0.25, 0.25)
        if z > 5.4: continue
        if -2.6 < z < 0.6: continue                    # 中间留一条通往陵墓的小路
        if row == 3 and -4.0 < z < 2.4: continue       # 陵墓台阶前
        x = xr + R.uniform(-0.3, 0.3)
        graves.append(dict(kind=R.choice(['Round', 'Round', 'Cross', 'Slab', 'Obelisk', 'Round']), x=x, z=z, y=ground_min(x, z) - 0.04, yaw=R.uniform(-12, 12), tilt=R.uniform(-7, 7), s=R.uniform(0.85, 1.25)))
LAYOUT['graves'] = graves
LAYOUT['hill_y'] = {f'{x:.1f},{z:.1f}': ground(x, z) for x in np.arange(-24, -5, 0.5) for z in np.arange(-10, 8, 0.5)}

# ================= 右·南瓜田 =================
PX0, PX1, PZ0, PZ1 = 8.5, 19.0, -11.0, 3.0
reset()
soil = bmesh.new()
for k in range(5):        # 五条田垄（沿 z 方向）
    x = PX0 + 1.2 + k * 2.0
    rd = blob(*U(x, 0.0, (PZ0 + PZ1) / 2), 1.3, PZ1 - PZ0, 0.36, amp=0.05, freq=1.5, cuts=8, seed=k)
    merge(soil, rd)
finish(soil, 'Pp_Soil', (0.28, 0.2, 0.15), var=0.3, dark=(0.2, 0.14, 0.1), seed=511, cells=16)
pumpkins = []
reset()
vb = bmesh.new(); lb = bmesh.new()
for k in range(5):
    x = PX0 + 1.2 + k * 2.0
    pts = [U(x + 0.25 * math.sin(t * 1.3 + k), 0.16 + 0.02 * math.sin(t * 3), t) for t in np.linspace(PZ0 + 0.4, PZ1 - 0.4, 40)]
    merge(vb, tube(pts, 0.025, 6))
    for i in range(2, 40, 2):
        p = pts[i]; fd = Vector((R.uniform(-0.4, 0.4), R.uniform(-0.4, 0.4), 1)).normalized()
        lf = slab([(0.22 * math.cos(a) * (0.6 + 0.4 * abs(math.cos(2.5 * a))), 0.22 * math.sin(a) * (0.6 + 0.4 * abs(math.cos(2.5 * a)))) for a in np.linspace(0, math.tau, 20, endpoint=False)], 0.01)
        rot = Vector((0, 0, 1)).rotation_difference(fd).to_matrix().to_4x4()
        side = Vector((R.uniform(-1, 1), R.uniform(-1, 1), 0)).normalized() * 0.2
        for v in lf.verts: v.co = rot @ v.co + p + side + Vector((0, 0, 0.03))
        merge(lb, lf)
    for j in range(5):
        z = PZ0 + 1.2 + j * (PZ1 - PZ0 - 2.4) / 4 + R.uniform(-0.4, 0.4)
        pumpkins.append(dict(x=x + R.uniform(-0.35, 0.35), z=z, s=R.uniform(0.6, 1.5), yaw=R.uniform(0, 360)))
finish(vb, 'Pp_Vine', (0.25, 0.4, 0.15), var=0.2, seed=513, cells=5)
finish(lb, 'Pp_Leaf', (0.28, 0.46, 0.18), var=0.25, dark=(0.18, 0.32, 0.12), seed=515, cells=6)
LAYOUT['pumpkins'] = pumpkins
reset()
finish(pumpkin(0.3, 0.42), 'Pp_Pumpkin', (1.0, 0.5, 0.1), var=0.14, dark=(0.85, 0.36, 0.06), seed=517, cells=4)
reset()
finish(tube([Vector((0, 0, 0.38)), Vector((0.02, 0, 0.47)), Vector((0.06, 0, 0.52))], 0.035, 8, r_fn=lambda u: 1 - 0.4 * u), 'Pp_Stem', (0.36, 0.33, 0.14), var=0.2, seed=519, cells=3)
reset()
hay = blob(0, 0, 0.42, 1.25, 0.65, 0.86, amp=0.04, freq=6.0, cuts=8, seed=3)
for tw in (-0.3, 0.3):
    merge(hay, tube([Vector((tw, -0.36, 0.0)), Vector((tw, -0.36, 0.88)), Vector((tw, 0.36, 0.88)), Vector((tw, 0.36, 0.0))], 0.012, 6))
finish(hay, 'Pp_Hay', (0.86, 0.72, 0.36), var=0.25, dark=(0.7, 0.56, 0.26), seed=521, cells=24, streak=0.6)
reset()
corn = bmesh.new()
for k in range(26):
    a = k / 26 * math.tau; rr = 0.18 + 0.05 * (k % 3)
    top = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 1.9 + 0.2 * R.random()))
    mid = Vector((math.cos(a) * 0.12, math.sin(a) * 0.12, 0.8))
    bot = Vector((math.cos(a) * rr * 1.6, math.sin(a) * rr * 1.6, -0.02))
    merge(corn, tube([bot, mid, top, top + Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.15))], 0.018, 5, r_fn=lambda u: 1 - 0.7 * u))
merge(corn, lathe([(0, 0.95), (0.15, 0.95), (0.15, 1.05), (0, 1.05)], 16))     # 捆扎绳
finish(corn, 'Pp_Corn', (0.78, 0.66, 0.4), var=0.25, dark=(0.6, 0.5, 0.3), seed=523, streak=0.7)
reset()
cart = ubox(-0.6, 0.6, 0.35, 0.42, -0.4, 0.4, 0.01)
for sx in (-1, 1):
    merge(cart, ubox(-0.6, 0.6, 0.42, 0.75, sx * 0.4 - 0.03, sx * 0.4 + 0.03, 0.01))
    merge(cart, ubox(sx * 0.6 - 0.03, sx * 0.6 + 0.03, 0.42, 0.75, -0.4, 0.4, 0.01))
for sz in (-0.46, 0.46):
    wh = torus(0.3, 0.04, 24, 8)
    M = Matrix.Rotation(math.radians(90), 4, 'X')
    for v in wh.verts: v.co = M @ v.co + U(0.2, 0.32, sz)
    merge(cart, wh)
merge(cart, tube([U(0.2, 0.32, -0.5), U(0.2, 0.32, 0.5)], 0.03, 8))
for sz in (-0.3, 0.3):
    merge(cart, tube([U(-0.6, 0.45, sz), U(-1.3, 0.62, sz * 1.2)], 0.025, 6))
finish(cart, 'Pp_Cart', (0.45, 0.3, 0.18), var=0.25, dark=(0.32, 0.2, 0.12), seed=525, streak=0.6)

# ================= 后·玻璃温室（正面朝 -Z，朝玩家）=================
GHZ, GHW, GHD, GHH = 13.0, 9.0, 5.0, 3.2       # 中心 z、宽、进深、墙高
reset()
base = ubox(-GHW / 2, GHW / 2, 0, 0.7, GHZ - GHD / 2, GHZ + GHD / 2, 0.02)
merge(base, ubox(-GHW / 2 - 0.1, GHW / 2 + 0.1, 0.68, 0.8, GHZ - GHD / 2 - 0.1, GHZ + GHD / 2 + 0.1, 0.02))
def bricks(path):
    N = 512; y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    row = (y // 32).astype(int); off = (row % 2) * 32
    mortar = ((y % 32) < 3) | (((x + off) % 64) < 3)
    col = np.tile(np.array([0.5, 0.3, 0.26], np.float32), (N, N, 1)) * (0.85 + 0.25 * np.random.default_rng(3).random((N // 32 + 1, N // 64 + 2))[row, ((x + off) // 64).astype(int) % (N // 64 + 2)][..., None])
    col[mortar] = [0.6, 0.56, 0.52]
    save_rgb(path, col.astype(np.float32))
finish(base, 'Gh_Base', tex=bricks)
reset()
glass = bmesh.new(); frame = bmesh.new()
x0, x1, z0, z1 = -GHW / 2 + 0.05, GHW / 2 - 0.05, GHZ - GHD / 2 + 0.05, GHZ + GHD / 2 - 0.05
merge(glass, ubox(x0, x1, 0.79, GHH, z0, z1))                         # 玻璃墙体（实心块，外表面就是玻璃）
# 拱顶：半圆柱（沿 x）
vault = bmesh.new()
segs = 16
ring = [Vector((0, (GHD / 2 - 0.09) * math.cos(a), (GHD / 2 - 0.09) * 0.75 * math.sin(a) - 0.01)) for a in np.linspace(0, math.pi, segs)]   # 比墙体略收、略低一点，避免与墙顶角点重合
L_, Rr = [], []
for p in ring:
    L_.append(vault.verts.new(U(x0, GHH, GHZ) + Vector((0, p.y, p.z))))
    Rr.append(vault.verts.new(U(x1, GHH, GHZ) + Vector((0, p.y, p.z))))
for i in range(segs - 1):
    vault.faces.new((L_[i], L_[i + 1], Rr[i + 1], Rr[i]))
vault.faces.new(L_[::-1]); vault.faces.new(Rr)
vault.faces.new((L_[0], Rr[0], Rr[-1], L_[-1]))     # 半圆柱底面
merge(glass, vault)
def glass_tex(path):
    """温室玻璃：青绿色微光，透出深色的大叶植物剪影（柔边、2 倍超采样抗锯齿）+ 细窗格"""
    N = 1024; S = 2; M = N * S
    r = np.random.default_rng(55); y, x = np.mgrid[0:M, 0:M].astype(np.float32)
    col = np.full((M, M, 3), [0.3, 0.6, 0.54], np.float32) * (0.7 + 0.3 * (y / M))[..., None]
    leaf = np.zeros((M, M), np.float32)
    for _ in range(130):
        cx, cy, rr = r.uniform(0, M), r.uniform(M * 0.3, M), r.uniform(36, 120) * S / 2
        a_ = r.uniform(0, np.pi)
        u = ((x - cx) * np.cos(a_) + (y - cy) * np.sin(a_)) / rr; v = (-(x - cx) * np.sin(a_) + (y - cy) * np.cos(a_)) / (rr * 0.42)
        d = u * u + v * v
        leaf = np.maximum(leaf, np.clip((1 - d) * 6, 0, 1) * r.uniform(0.6, 1.0))
    col = col * (1 - leaf[..., None] * 0.7) + np.array([0.05, 0.16, 0.1], np.float32) * leaf[..., None] * 0.7
    col = col.reshape(N, S, N, S, 3).mean((1, 3))
    yy, xx = np.mgrid[0:N, 0:N]
    grid = ((xx % 128) < 2) | ((yy % 128) < 2)
    col[grid] = col[grid] * 0.35
    save_rgb(path, col.astype(np.float32))
# —— 门厅：从正面凸出的玻璃山墙小厅（朝玩家），尖顶 + 尖饰
EX, ED, EH = 1.4, 1.7, 3.5
merge(glass, ubox(-EX + 0.05, EX - 0.05, 0.3, EH, z0 - ED, z0 + 0.3))     # 门厅落在 0.3 米高的石台上
eg = bmesh.new()
fa = [eg.verts.new(U(x_, EH - 0.01, z0 - ED - 0.08)) for x_ in (-EX - 0.12, EX + 0.12)] + [eg.verts.new(U(0, EH + 1.5, z0 - ED - 0.08))]
fb2 = [eg.verts.new(U(x_, EH - 0.01, z0 + 0.4)) for x_ in (-EX - 0.12, EX + 0.12)] + [eg.verts.new(U(0, EH + 1.5, z0 + 0.4))]
eg.faces.new(fa); eg.faces.new(fb2[::-1])
for i in range(3):
    j = (i + 1) % 3; eg.faces.new((fa[j], fa[i], fb2[i], fb2[j]))
merge(glass, eg)
# —— 屋顶小塔亭（八角玻璃灯笼 + 尖顶）
cup = lathe([(0, 0), (0.75, 0), (0.75, 0.9), (0, 0.9)], 8)
for v in cup.verts: v.co += U(0, GHH + (GHD / 2) * 0.75 - 0.15, GHZ)
merge(glass, cup)
finish(glass, 'Gh_Glass', tex=glass_tex)
# —— 铁架：竖框每 0.6 米、两道横框、拱肋；门厅四角柱 + 山墙边框；塔亭肋条
for x in np.arange(x0, x1 + 0.01, 0.6):
    merge(frame, ubox(x - 0.03, x + 0.03, 0.78, GHH + 0.02, z0 - 0.05, z0 + 0.01))
    merge(frame, ubox(x - 0.03, x + 0.03, 0.78, GHH + 0.02, z1 - 0.01, z1 + 0.05))
    arc = [U(x, GHH, GHZ) + Vector((0, (GHD / 2) * math.cos(a), (GHD / 2) * 0.75 * math.sin(a))) for a in np.linspace(0, math.pi, 16)]
    merge(frame, tube(arc, 0.028, 6))
for y_ in (1.65, 2.5):
    merge(frame, ubox(x0 - 0.02, x1 + 0.02, y_ - 0.03, y_ + 0.03, z0 - 0.05, z0 + 0.01))
for z_ in np.arange(z0, z1 + 0.01, 0.6):
    for xs in (x0, x1):
        merge(frame, ubox(xs - 0.05, xs + 0.01 if xs == x0 else xs + 0.05, 0.78, GHH + 0.02, z_ - 0.03, z_ + 0.03) if xs == x0 else ubox(xs - 0.01, xs + 0.05, 0.78, GHH + 0.02, z_ - 0.03, z_ + 0.03))
merge(frame, ubox(x0 - 0.05, x1 + 0.05, GHH - 0.05, GHH + 0.06, z0 - 0.07, z1 + 0.07, 0.01))
ridge = [U(x, GHH + (GHD / 2) * 0.75 + 0.05, GHZ) for x in np.linspace(x0, x1, 2)]
merge(frame, tube(ridge, 0.05, 8))
for x in np.linspace(x0 + 0.5, x1 - 0.5, 12):
    if abs(x) < 1.0: continue
    tip = lathe([(0, 0), (0.05, 0), (0, 0.22)], 6)
    for v in tip.verts: v.co += U(x, GHH + (GHD / 2) * 0.75 + 0.05, GHZ)
    merge(frame, tip)
for sx in (-1, 1):
    merge(frame, ubox(sx * EX - 0.06, sx * EX + 0.06, 0.78, EH + 0.02, z0 - ED - 0.1, z0 - ED + 0.02))
    for y_ in (1.65, 2.5):
        merge(frame, ubox(sx * EX - 0.06, sx * EX + 0.06, y_ - 0.03, y_ + 0.03, z0 - ED, z0))
    rake = [U(sx * (EX + 0.12) * (1 - t), EH + 1.5 * t, z0 - ED - 0.12) for t in np.linspace(0, 1, 2)]
    merge(frame, tube(rake, 0.045, 6))
    # 雕花封檐板：顺着山墙斜边的一条板，下沿是连续的小半圆（维多利亚 gingerbread）
    e0 = Vector((sx * (EX + 0.12), EH)); e1 = Vector((sx * 0.03, EH + 1.5 - 0.04))     # 两侧板在尖顶处各留一点缝，不共用顶点
    dd = (e1 - e0); Ld = dd.length; dd /= Ld; nn = Vector((dd.y, -dd.x)) * (1 if sx < 0 else -1)   # 板的下方（朝外下）
    if nn.y > 0: nn = -nn
    top = [e0 + dd * Ld * t for t in np.linspace(0, 1, 2)]
    bot = []
    nsc = 9
    for k in range(nsc):
        for q in np.linspace(0, 1, 7)[(1 if k else 0):]:      # 相邻半圆共用端点，不重复取点
            t = (k + q) / nsc
            dep = 0.1 + 0.07 * math.sin(math.pi * q)
            bot.append(e0 + dd * Ld * t + nn * dep)
    outline = [top[0], top[1]] + bot[::-1]
    outline = [(p.x, p.y) for p in outline]
    if sx < 0: outline = outline[::-1]
    bd = slab(outline, 0.04)
    for v in bd.verts:
        ux, uy, uz = v.co.x, v.co.y, v.co.z
        v.co = U(ux, uy, z0 - ED - 0.13 + uz)
    merge(frame, bd)
    # 门厅正面、侧面的窗格竖框与横档
    for x_ in np.arange(-EX + 0.45, EX - 0.3, 0.45):
        if abs(x_) < 0.75: continue
        merge(frame, ubox(x_ - 0.025, x_ + 0.025, 0.3, EH, z0 - ED - 0.06, z0 - ED - 0.0))
    for z_ in np.arange(z0 - ED + 0.45, z0, 0.45):
        merge(frame, ubox(sx * (EX - 0.05) - 0.01 * sx, sx * (EX - 0.05) + 0.05 * sx, 0.3, EH, z_ - 0.025, z_ + 0.025) if sx > 0 else ubox(sx * (EX - 0.05) - 0.05, sx * (EX - 0.05) + 0.01, 0.3, EH, z_ - 0.025, z_ + 0.025))
for y_ in (1.65, 2.5):
    merge(frame, ubox(-EX + 0.05, -0.65, y_ - 0.025, y_ + 0.025, z0 - ED - 0.06, z0 - ED))
    merge(frame, ubox(0.65, EX - 0.05, y_ - 0.025, y_ + 0.025, z0 - ED - 0.06, z0 - ED))
for t in (0.33, 0.66):        # 山墙三角里的两道横档
    yy_ = EH + 1.5 * t; hw = (EX + 0.05) * (1 - t)
    merge(frame, ubox(-hw, hw, yy_ - 0.025, yy_ + 0.025, z0 - ED - 0.1, z0 - ED - 0.04))
merge(frame, ubox(-EX - 0.12, EX + 0.12, EH - 0.06, EH + 0.04, z0 - ED - 0.12, z0 - ED - 0.02))
# 门：门框 + 中缝 + 窗格横档（压在发光门板外侧）
for x_ in (-0.6, 0.0, 0.6):
    merge(frame, ubox(x_ - 0.05, x_ + 0.05, 0.31, 2.6, z0 - ED - 0.2, z0 - ED - 0.12))
for y_ in (1.0, 1.6, 2.1, 2.6):
    merge(frame, ubox(-0.62, 0.62, y_ - 0.04, y_ + 0.04, z0 - ED - 0.2, z0 - ED - 0.12))
fin = lathe([(0, 0), (0.07, 0), (0.09, 0.15), (0.03, 0.3), (0.05, 0.45), (0, 0.75)], 8)
for v in fin.verts: v.co += U(0, EH + 1.48, z0 - ED - 0.1)
merge(frame, fin)
ct = GHH + (GHD / 2) * 0.75 - 0.15
cr = lathe([(0, ct + 0.88), (0.85, ct + 0.88), (0.85, ct + 0.98), (0.1, ct + 1.6), (0.04, ct + 2.1), (0, ct + 2.15)], 8)
for v in cr.verts: v.co += U(0, 0, GHZ)
merge(frame, cr)
finish(frame, 'Gh_Frame', (0.88, 0.86, 0.82), var=0.12, dark=(0.66, 0.64, 0.6), seed=531, cells=5)
reset()
door = ubox(-0.55, 0.55, 0.31, 2.55, z0 - ED - 0.14, z0 - ED - 0.04)
def door_tex(path):
    N = 256; y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N
    col = np.stack([0.95 - 0.25 * y, 0.62 - 0.25 * y, 0.28 - 0.12 * y], -1)
    col *= (0.85 + 0.15 * np.sin(x * 40) ** 2)[..., None]          # 像隔着布帘的竖向明暗
    save_rgb(path, col.astype(np.float32))
finish(door, 'Gh_Door', tex=door_tex)
reset()
steps = ubox(-EX - 0.1, EX + 0.1, -0.02, 0.31, z0 - ED - 0.1, z0 - 0.05, 0.02)          # 门厅石台
merge(steps, ubox(-0.9, 0.9, -0.04, 0.16, z0 - ED - 0.5, z0 - ED + 0.05, 0.01))           # 一级台阶
finish(steps, 'Gh_Steps', tex=stone_tex)

# ================= 前·喷泉广场 =================
FZ = -16.0
reset()
plaza = lathe([(0, 0.0), (4.3, 0.0), (4.3, 0.004), (0, 0.004)], 48)
for v in plaza.verts: v.co += U(0, 0, FZ)
finish(plaza, 'Ft_Plaza', (0.46, 0.43, 0.42), var=0.3, dark=(0.34, 0.32, 0.3), seed=541, cells=40, uv=uv_world(1.5))
reset()
fs = lathe([(0, 0), (2.3, 0), (2.35, 0.05), (2.35, 0.55), (2.2, 0.62), (2.05, 0.62), (2.05, 0.3), (0.5, 0.3), (0.35, 0.5), (0.28, 1.3), (0.45, 1.4), (1.1, 1.55), (1.15, 1.68), (1.0, 1.7),
            (0.3, 1.62), (0.2, 1.9), (0.16, 2.4), (0.45, 2.55), (0.5, 2.65), (0.12, 2.65), (0.1, 2.9), (0.2, 3.0), (0.17, 3.15), (0, 3.25)], 36,
           r_fn=lambda t, z: 1 + (0.04 * math.cos(8 * t) if z < 0.62 else 0))
for v in fs.verts: v.co += U(0, 0, FZ)
finish(fs, 'Ft_Stone', tex=stone_tex)
reset()
wt = lathe([(0, 0.25), (2.08, 0.25), (2.08, 0.5), (0, 0.5)], 36)
w2 = lathe([(0, 1.5), (1.02, 1.5), (1.02, 1.6), (0, 1.6)], 28)
merge(wt, w2)
for v in wt.verts: v.co += U(0, 0, FZ)
finish(wt, 'Ft_Water', (0.2, 0.42, 0.55), var=0.25, dark=(0.35, 0.65, 0.75), seed=543, cells=6)

json.dump(LAYOUT, open(os.path.join(OUT, 'garden3_layout.json'), 'w'))
json.dump(report, open(os.path.join(OUT, 'garden3_report.json'), 'w'), indent=1)
print('GARDEN3', {k: v['faces'] for k, v in report.items()})

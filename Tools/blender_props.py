"""精细程序化道具（Tripo 版本过不了严格拓扑审计的改走这里）：Blender -b -P Tools/blender_props.py -- <输出目录>
本地原点在底面中心，正面朝 Unity +Z（Blender -Y）。
  Hb_Bale  草垛：圆角草块 + 四周翘出的草茎；Hb_Twine 两道捆绳（闭合绳圈，嵌进草面 1/3）
  Cs_Stalk 玉米秆束的秆子（底部散开、腰部收拢、顶部炸开）/ Cs_Leaf 下垂的长叶带 / Cs_Tie 腰间捆绳
  Ln_Iron  铁提灯框架（顶环、顶盖、底座、卷草竖条）/ Ln_Glass 发光灯罩"""
import bpy, bmesh, math, sys, os, json, random
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)
R = random.Random(8)


def save_rgb(path, col):
    N = col.shape[0]
    rgba = np.concatenate([np.clip(col, 0, 1), np.ones((N, N, 1), np.float32)], -1).astype(np.float32)
    im = bpy.data.images.new(os.path.basename(path), N, N, alpha=False); im.pixels = rgba[::-1].ravel()
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()


def finish(bm, name, base=None, tex=None, **kw):
    ob = to_obj(bm, name); uv_smart(ob, 0.005)
    if tex: tex(os.path.join(OUT, f'{name}_albedo.png'))
    else: noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    export(ob, os.path.join(OUT, f'{name}.fbx'))


def straw_tex(path, base=(0.86, 0.68, 0.3)):
    """稻草纤维贴图：上万根不同深浅的短线，方向大致一致略有交错（可平铺）"""
    N = 1024; r = np.random.default_rng(12)
    col = np.tile(np.array(base, np.float32) * 0.78, (N, N, 1))
    for _ in range(14000):
        x0, y0 = r.uniform(0, N), r.uniform(0, N); L = r.uniform(25, 90); a = r.normal(0.0, 0.35)
        tone = r.uniform(0.75, 1.25); w = r.choice([1, 1, 2])
        t = np.linspace(0, 1, int(L))
        xs = ((x0 + np.cos(a) * L * t) % N).astype(int); ys = ((y0 + np.sin(a) * L * t) % N).astype(int)
        c = np.clip(np.array(base, np.float32) * tone, 0, 1)
        col[ys, xs] = c
        if w == 2: col[(ys + 1) % N, xs] = c * 0.85
    save_rgb(path, col)


# ================= 草垛 =================
BW, BD, BH = 1.15, 0.58, 0.52
reset()
bale = blob(0, 0, BH / 2, BW, BD, BH, amp=0.025, freq=5, cuts=10, seed=4)
for k in range(140):        # 翘出的草茎：从表面往外伸、末端尖
    face = R.choice(['top', 'top', 'side', 'side', 'end'])
    if face == 'top':
        p = Vector((R.uniform(-BW / 2 + 0.08, BW / 2 - 0.08), R.uniform(-BD / 2 + 0.06, BD / 2 - 0.06), BH - 0.02)); n = Vector((R.uniform(-0.6, 0.6), R.uniform(-0.6, 0.6), 1))
    elif face == 'side':
        sy = R.choice([-1, 1]); p = Vector((R.uniform(-BW / 2 + 0.06, BW / 2 - 0.06), sy * (BD / 2 - 0.02), R.uniform(0.06, BH - 0.06))); n = Vector((R.uniform(-0.7, 0.7), sy, R.uniform(-0.4, 0.6)))
    else:
        sx = R.choice([-1, 1]); p = Vector((sx * (BW / 2 - 0.02), R.uniform(-BD / 2 + 0.05, BD / 2 - 0.05), R.uniform(0.06, BH - 0.06))); n = Vector((sx, R.uniform(-0.7, 0.7), R.uniform(-0.4, 0.6)))
    n.normalize(); L = R.uniform(0.06, 0.16)
    merge(bale, tube([p - n * 0.03, p + n * L * 0.5 + Vector((0, 0, -0.01)), p + n * L], 0.006, 4, r_fn=lambda u: 1 - 0.8 * u))
finish(bale, 'Hb_Bale', tex=straw_tex)
reset()
tw = bmesh.new()
for x in (-0.3, 0.3):
    loop = []
    hw, hh, rr = BD / 2 + 0.004, BH / 2 + 0.004, 0.09         # 圆角矩形截面（略大于草块，绳子嵌进去约 1/3）
    for k in range(48):
        a = k / 48 * math.tau
        c, s = math.cos(a), math.sin(a)
        y = math.copysign(min(abs(c) * 1.4, 1.0), c) * (hw - rr) + c * rr
        z = math.copysign(min(abs(s) * 1.4, 1.0), s) * (hh - rr) + s * rr
        loop.append(Vector((x + 0.01 * math.sin(a * 3), y, BH / 2 + z)))
    merge(tw, tube_loop(loop, 0.014, 8))
finish(tw, 'Hb_Twine', (0.36, 0.25, 0.15), var=0.2, seed=811, streak=0.6)

def leaf_strip(P, ws, side, thick, twist=0.0):
    """叶片扁带：宽度方向固定为绕秆的切向 side（叶面朝外），只在叶片后半段按 twist 扭转；首尾封口、始终封闭"""
    bm = bmesh.new(); n = len(P); rings = []
    for i in range(n):
        T = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        Wd = (side - T * side.dot(T)).normalized(); Th = T.cross(Wd)
        a = twist * max(0.0, (i - 3) / (n - 4)) ** 1.5
        Wd, Th = Wd * math.cos(a) + Th * math.sin(a), Th * math.cos(a) - Wd * math.sin(a)
        w = ws[i] / 2
        rings.append([bm.verts.new(P[i] + Wd * sx * w + Th * sy * thick / 2) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for a_, b_ in zip(rings[:-1], rings[1:]):
        for k in range(4): bm.faces.new((a_[k], a_[(k + 1) % 4], b_[(k + 1) % 4], b_[k]))
    bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    return bm


# ================= 玉米秆束 =================
reset()
st = bmesh.new(); lf = bmesh.new()
TIE = 0.9
for k in range(46):
    a = k / 46 * math.tau + R.uniform(-0.08, 0.08)
    rb = R.uniform(0.22, 0.42); rt = R.uniform(0.18, 0.5); top = R.uniform(1.75, 2.15)
    c, s = math.cos(a), math.sin(a)
    pts = []
    for t in np.linspace(0, 1, 12):
        z = t * top
        if z < TIE: r = rb + (0.11 - rb) * (z / TIE) ** 0.8
        else: r = 0.11 + (rt - 0.11) * ((z - TIE) / (top - TIE)) ** 1.6
        pts.append(Vector((c * r, s * r, z - 0.02)))
    rad = R.uniform(0.012, 0.018)
    merge(st, tube(pts, rad, 6, r_fn=lambda u: 1 - 0.55 * u))
    out = Vector((c, s, 0)); side = Vector((-s, c, 0))
    for j in range(R.randint(1, 3)):          # 叶子：叶鞘先贴着秆子往下包一段（长在秆上），再从节上向外伸出、下垂打弯
        i0 = R.randint(6, 10); p0 = pts[i0]
        T = (pts[i0 + 1] - pts[i0 - 1]).normalized()
        rs = rad * (1 - 0.55 * i0 / 11)
        L = R.uniform(0.5, 0.95); W = R.uniform(0.05, 0.075); tw = R.uniform(-1.0, 1.0)
        lp = [p0 - T * 0.15 + out * rs * 0.55, p0 - T * 0.08 + out * rs * 0.65, p0 - T * 0.02 + out * rs * 0.8]
        ws = [W * 0.12, W * 0.42, W * 0.62]            # 叶鞘下端收尖，不留方头
        for t in np.linspace(0.08, 1, 8):
            lp.append(p0 + out * (rs * 0.8 + L * 0.55 * math.sin(t * 1.4)) + Vector((0, 0, L * (0.35 * t - 1.0 * t * t))) + side * 0.08 * math.sin(t * 3 + j) * t)
            ws.append(max(0.004, W * min(1.0, 0.75 + t * 2.5) * (1 - t) ** 0.7))
        merge(lf, leaf_strip(lp, ws, side, 0.004, tw))
finish(st, 'Cs_Stalk', tex=lambda p: straw_tex(p, (0.74, 0.6, 0.36)))
finish(lf, 'Cs_Leaf', tex=lambda p: straw_tex(p, (0.8, 0.68, 0.42)))
reset()
tie = bmesh.new()
for dz in (0.0, 0.06):
    merge(tie, tube_loop([Vector((math.cos(a) * 0.125, math.sin(a) * 0.125, TIE - 0.03 + dz + 0.01 * math.sin(a * 2))) for a in np.linspace(0, math.tau, 32, endpoint=False)], 0.022, 8))
merge(tie, tube([Vector((0.12, -0.03, TIE)), Vector((0.2, -0.06, TIE - 0.1)), Vector((0.22, -0.08, TIE - 0.25))], 0.014, 6))
finish(tie, 'Cs_Tie', (0.42, 0.3, 0.18), var=0.2, seed=813, streak=0.6)

# ================= 铁提灯 =================
reset()
ir = bmesh.new()
base = lathe([(0, 0), (0.12, 0), (0.13, 0.02), (0.11, 0.04), (0.09, 0.05), (0, 0.05)], 16)
merge(ir, base)
cap = lathe([(0, 0.34), (0.12, 0.34), (0.125, 0.36), (0.07, 0.42), (0.03, 0.44), (0, 0.44)], 16)
merge(ir, cap)
ring = torus(0.045, 0.01, 16, 6)
for v in ring.verts: v.co = Matrix.Rotation(math.pi / 2, 4, 'X') @ v.co + Vector((0, 0, 0.49))
merge(ir, ring)
for k in range(6):
    a = k / 6 * math.tau; c, s = math.cos(a), math.sin(a)
    bar = [Vector((c * 0.1, s * 0.1, 0.045))] + [Vector((c * (0.1 + 0.03 * math.sin(math.pi * t)), s * (0.1 + 0.03 * math.sin(math.pi * t)), 0.045 + 0.3 * t)) for t in np.linspace(0.1, 0.9, 7)] + [Vector((c * 0.1, s * 0.1, 0.345))]
    merge(ir, tube(bar, 0.007, 6))
    curl = [Vector((c * (0.11 + 0.035 * math.cos(u) * (1 - u / 7)), s * (0.11 + 0.035 * math.cos(u) * (1 - u / 7)), 0.19 + 0.035 * math.sin(u) * (1 - u / 7))) for u in np.linspace(0, 6.5, 16)]
    merge(ir, tube(curl, 0.005, 5))
finish(ir, 'Ln_Iron', (0.13, 0.12, 0.15), var=0.12, seed=821, cells=3)
reset()
gl = lathe([(0, 0.05), (0.085, 0.05), (0.11, 0.19), (0.085, 0.34), (0, 0.34)], 20)
finish(gl, 'Ln_Glass', (1.0, 0.75, 0.38), var=0.15, dark=(1.0, 0.9, 0.6), seed=823, cells=3)

def stone_tex(path):
    """风化石：平滑分形噪声的灰石 + 柔边苔藓斑"""
    noise_tex(path, (0.56, 0.55, 0.6), var=0.12, size=1024, seed=41, cells=6)      # 不做大块苔藓：车削件智能展 UV 的接缝会把斑块切成色块


def wood_tex(path, base=(0.5, 0.31, 0.18)):
    """木板：竖向纹理 + 每 1/8 一道板缝 + 深浅不一的板"""
    N = 1024; r = np.random.default_rng(5); y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    plank = (x // (N / 8)).astype(int)
    tone = r.uniform(0.8, 1.15, 9)[plank]
    grain = 0.88 + 0.12 * np.sin(x * 0.35 + 3 * np.sin(y * 0.02 + plank)) 
    col = np.array(base, np.float32)[None, None] * (tone * grain)[..., None]
    col[(x % (N / 8)) < 5] *= 0.45
    save_rgb(path, col.astype(np.float32))


# ================= 石瓮（瓮身凹槽、卷边口沿、两侧卷草把手、方座）=================
reset()
urn = lathe([(0, 0.0), (0.2, 0.0), (0.2, 0.08), (0.12, 0.1), (0.1, 0.16), (0.08, 0.2), (0.12, 0.24), (0.2, 0.3), (0.27, 0.4), (0.3, 0.48), (0.3, 0.52), (0.33, 0.54), (0.33, 0.58), (0.28, 0.59), (0.27, 0.55), (0, 0.55)], 32,
            r_fn=lambda t, z: 1 + (0.05 * math.cos(16 * t) if 0.26 < z < 0.5 else 0))
merge(urn, box(0, 0, -0.06, 0.48, 0.48, 0.14, bevel=0.02))
for sx in (-1, 1):
    hd = [Vector((sx * (0.28 + 0.07 * math.cos(u) * (1 - u / 9)), 0, 0.46 + 0.07 * math.sin(u) * (1 - u / 9))) for u in np.linspace(0.3, 7.5, 18)]
    merge(urn, tube(hd, 0.016, 6))
for v in urn.verts: v.co.z += 0.13
finish(urn, 'Su_Stone', tex=stone_tex)
reset()
finish(lathe([(0, 0.66), (0.27, 0.66), (0.24, 0.7), (0, 0.72)], 24), 'Su_Soil', (0.18, 0.13, 0.1), var=0.3, seed=831, cells=8)

# ================= 木桶（桶板起伏、三道闭合铁箍、内凹桶盖）=================
reset()
H = 0.9
prof = [(0, 0.0), (0.3, 0.0)] + [(0.3 + 0.07 * math.sin(math.pi * t), H * t) for t in np.linspace(0.04, 0.96, 12)] + [(0.3, H), (0.27, H), (0.27, H - 0.04), (0, H - 0.04)]
barrel = lathe(prof, 36, r_fn=lambda t, z: 1 + 0.012 * abs(math.sin(9 * t)) - 0.006)
finish(barrel, 'Br_Wood', tex=wood_tex)
reset()
hp = bmesh.new()
for t in (0.12, 0.5, 0.88):
    rr = 0.3 + 0.07 * math.sin(math.pi * t) + 0.012
    merge(hp, tube_loop([Vector((math.cos(a) * rr, math.sin(a) * rr, H * t)) for a in np.linspace(0, math.tau, 40, endpoint=False)], 0.018, 6))
finish(hp, 'Br_Hoop', (0.16, 0.15, 0.17), var=0.15, seed=833, cells=4)

# ================= 女巫帽（波浪帽檐、歪折尖顶、橙色帽带、金扣）=================
reset()
brim = lathe([(0, 0.0), (0.42, -0.005), (0.44, 0.015), (0.4, 0.03), (0, 0.035)], 40, r_fn=lambda t, z: 1 + 0.05 * math.sin(5 * t + 0.5))
for v in brim.verts:
    rr = math.hypot(v.co.x, v.co.y); v.co.z += 0.05 * (rr / 0.42) ** 2 * (0.5 + 0.5 * math.sin(math.atan2(v.co.y, v.co.x) * 2))
cone = lathe([(0, 0.02)] + [(0.19 * (1 - k / 14) ** 1.1 + 0.008, 0.03 + 0.5 * k / 14) for k in range(14)] + [(0, 0.54)], 24)
for v in cone.verts:
    u = max(0.0, (v.co.z - 0.2) / 0.34); v.co.x += 0.18 * u ** 2; v.co.z -= 0.1 * u ** 3
merge(brim, cone)
finish(brim, 'Wh_Hat', (0.42, 0.2, 0.62), var=0.15, dark=(0.3, 0.14, 0.46), seed=835, cells=6)
reset()
finish(lathe([(0, 0.04), (0.195, 0.04), (0.19, 0.12), (0, 0.12)], 24), 'Wh_Band', (1.0, 0.5, 0.12), var=0.1, seed=837, cells=3)
reset()
bk = box(0, -0.19, 0.08, 0.1, 0.03, 0.08, bevel=0.008)
finish(bk, 'Wh_Buckle', (0.95, 0.75, 0.25), var=0.1, seed=839, cells=3)

# ================= 花园长椅（木条座面与靠背、铸铁卷草侧架）=================
reset()
wd = bmesh.new()
for k in range(4):
    merge(wd, box(0, -0.18 + k * 0.12, 0.44, 1.5, 0.1, 0.035, bevel=0.008))
for k in range(3):
    zc = 0.6 + k * 0.13
    merge(wd, box(0, 0.24 + 0.03 * k, zc, 1.5, 0.035, 0.1, bevel=0.008))
finish(wd, 'Bn_Wood', tex=lambda p: wood_tex(p, (0.52, 0.32, 0.17)))
reset()
ir = bmesh.new()
for sx in (-1, 1):
    x = sx * 0.68
    side = [Vector((x, -0.25, 0.0)), Vector((x, -0.2, 0.2)), Vector((x, -0.24, 0.42)), Vector((x, -0.18, 0.62)), Vector((x, -0.26, 0.66))]
    merge(ir, tube(side, 0.03, 8))                                                       # 前腿 + 扶手
    back = [Vector((x, 0.26, 0.0)), Vector((x, 0.18, 0.25)), Vector((x, 0.22, 0.45)), Vector((x, 0.3, 0.75)), Vector((x, 0.27, 0.92))]
    merge(ir, tube(back, 0.03, 8))                                                       # 后腿 + 靠背立柱
    arm = [Vector((x, -0.26, 0.66)) + Vector((0, 0.53 * t, 0.12 * math.sin(math.pi * t))) for t in np.linspace(0, 1, 10)]
    merge(ir, tube(arm, 0.025, 8))
    for (cy, cz, rr, ph) in ((-0.3, 0.6, 0.05, 0.0), (-0.24, 0.06, 0.045, 1.5), (0.3, 0.9, 0.045, 3.0)):   # 卷草
        curl = [Vector((x, cy + rr * math.cos(u + ph) * (1 - u / 9), cz + rr * math.sin(u + ph) * (1 - u / 9))) for u in np.linspace(0, 7.0, 16)]
        merge(ir, tube(curl, 0.014, 6))
    merge(ir, tube([Vector((x, -0.2, 0.41)), Vector((x, 0.0, 0.41)), Vector((x, 0.2, 0.41))], 0.02, 6))   # 座面横撑
finish(ir, 'Bn_Iron', (0.12, 0.11, 0.14), var=0.12, seed=841, cells=4)

# ================= 苹果木箱 =================
reset()
cw = bmesh.new()
W, D, H = 0.7, 0.48, 0.34
for z0 in (0.04, 0.19):        # 两层侧板（板间留缝）
    for sy in (-1, 1): merge(cw, box(0, sy * (D / 2 - 0.012), z0 + 0.06, W - 0.06, 0.024, 0.12, bevel=0.006))
    for sx in (-1, 1): merge(cw, box(sx * (W / 2 - 0.012), 0, z0 + 0.06, 0.024, D - 0.08, 0.12, bevel=0.006))
for sx in (-1, 1):
    for sy in (-1, 1): merge(cw, box(sx * (W / 2 - 0.03), sy * (D / 2 - 0.03), H / 2 + 0.01, 0.05, 0.05, H + 0.02, bevel=0.006))
merge(cw, box(0, 0, 0.035, W - 0.08, D - 0.08, 0.02))
finish(cw, 'Ac_Wood', tex=lambda p: wood_tex(p, (0.62, 0.42, 0.22)))
reset()
ap = bmesh.new(); stb = bmesh.new()
rng2 = random.Random(3)
spots = [(x, y) for x in (-0.24, -0.08, 0.08, 0.24) for y in (-0.13, 0.0, 0.13)]
for i, (x, y) in enumerate(spots):
    r = rng2.uniform(0.062, 0.072); zc = 0.27 + rng2.uniform(-0.015, 0.03) + (0.03 if i % 3 == 1 else 0)
    a = lathe([(0, -0.78 * r), (0.35 * r, -0.92 * r), (0.72 * r, -0.8 * r), (0.95 * r, -0.4 * r), (1.0 * r, 0.05 * r), (0.93 * r, 0.5 * r), (0.68 * r, 0.82 * r), (0.32 * r, 0.82 * r), (0, 0.6 * r)], 24)      # 苹果轮廓：上下凹窝、腰部饱满
    tilt = Matrix.Rotation(rng2.uniform(-0.4, 0.4), 4, 'X') @ Matrix.Rotation(rng2.uniform(-0.4, 0.4), 4, 'Y')
    for v in a.verts: v.co = tilt @ v.co + Vector((x + rng2.uniform(-0.01, 0.01), y + rng2.uniform(-0.01, 0.01), zc))
    merge(ap, a)
    tip = tilt @ Vector((0, 0, r * 0.58)) + Vector((x, y, zc))
    merge(stb, tube([tip, tip + tilt @ Vector((0.005, 0, 0.045))], 0.006, 5))
finish(ap, 'Ac_Apple', (0.8, 0.13, 0.1), var=0.06, seed=851, cells=3)      # 低变化：避免智能展 UV 的块间色差显成斑块
finish(stb, 'Ac_Stem', (0.3, 0.22, 0.12), var=0.2, seed=853, cells=3)

json.dump(report, open(os.path.join(OUT, 'props_report.json'), 'w'), indent=1)
print('PROPS2', {k: v['faces'] for k, v in report.items()})


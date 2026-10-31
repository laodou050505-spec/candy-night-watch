"""稻草人（分部件程序化）：Blender -b -P Tools/blender_scarecrow.py -- <输出目录>
共同坐标系：Blender Z 向上，正面朝 -Y（= Unity +Z，装配体约定）。总高约 2.2m。
部件：Scr_Wood 木桩+横杆 / Scr_Shirt 格子衬衫（衣身+两袖）/ Scr_Rope 腰绳 / Scr_Head 南瓜头（正面画南瓜灯脸）
     Scr_Stem 南瓜梗 / Scr_Hat 破草帽（帽檐+歪帽尖）/ Scr_Straw 袖口、衣摆、帽下的稻草束"""
import bpy, bmesh, math, sys, os, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(4)


def finish(bm, name, base=None, tex=None, uv='smart', **kw):
    ob = to_obj(bm, name)
    if uv == 'smart': uv_smart(ob)
    else: uv(ob)
    if tex is None: noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    else: tex(os.path.join(OUT, f'{name}_albedo.png'))
    export(ob, os.path.join(OUT, f'{name}.fbx'))


def save_rgb(path, col):
    N = col.shape[0]
    rgba = np.concatenate([np.clip(col, 0, 1), np.ones((N, N, 1), np.float32)], -1).astype(np.float32)
    im = bpy.data.images.new(os.path.basename(path), N, N, alpha=False); im.pixels = rgba[::-1].ravel()
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()


BAR_Z, NECK_Z = 1.38, 1.47
# ---------------- 木桩 + 横杆 ----------------
reset()
pole = [(0.006 * math.sin(i * 0.7), 0.004 * math.cos(i * 0.5), 1.6 * i / 20) for i in range(21)]
bm = tube(pole, 0.045, 10, r_fn=lambda u: 1.05 - 0.15 * u)
bar = [(-0.78 + 1.56 * i / 20, -0.0, BAR_Z + 0.01 * math.sin(i * 0.9)) for i in range(21)]
merge(bm, tube(bar, 0.035, 10))
finish(bm, 'Scr_Wood', (0.45, 0.31, 0.18), var=0.25, dark=(0.3, 0.2, 0.11), seed=21, streak=0.35)

# ---------------- 衬衫：衣身（车削）+ 两袖（管）----------------
reset()
prof = [(0, 0.8), (0.27, 0.8), (0.3, 0.84), (0.25, 0.98), (0.22, 1.08), (0.23, 1.2), (0.25, 1.33), (0.24, 1.41), (0.16, NECK_Z), (0.1, NECK_Z + 0.01), (0, NECK_Z + 0.01)]
bm = lathe(prof, 28, r_fn=lambda t, z: 1 + 0.06 * math.sin(3 * t + z * 5))
for s in (-1, 1):
    sl = [(s * (0.16 + 0.5 * i / 12), 0, BAR_Z - 0.005 - 0.02 * (i / 12)) for i in range(13)]
    merge(bm, tube(sl, 0.095, 14, r_fn=lambda u: 1.0 + 0.25 * u))


def plaid(path):
    N = 512; y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    base = np.array([0.24, 0.36, 0.62], np.float32)
    col = np.tile(base, (N, N, 1))
    bx = ((x // 32) % 4 == 0); by = ((y // 32) % 4 == 0)
    col[bx] *= 0.7; col[by] *= 0.7
    thin = ((x % 64) < 4) | ((y % 64) < 4)
    col[thin] = col[thin] * 0.4 + np.array([0.85, 0.55, 0.2]) * 0.6
    col *= (0.92 + 0.08 * rng.random((N, N, 1))).astype(np.float32)
    save_rgb(path, col)


finish(bm, 'Scr_Shirt', tex=plaid)

# ---------------- 腰绳 ----------------
reset()
rope = [(math.cos(i / 48 * math.tau) * 0.235, math.sin(i / 48 * math.tau) * 0.235, 1.08) for i in range(48)]
rope.append(rope[0])
bm = tube(rope + [(rope[1][0], rope[1][1] - 0.001, 1.08)], 0.02, 8)
# 绳结垂下来的两股
merge(bm, tube([(0.05, -0.235, 1.08), (0.06, -0.25, 1.0), (0.08, -0.25, 0.93)], 0.016, 8))
merge(bm, tube([(-0.03, -0.235, 1.08), (-0.05, -0.25, 1.0), (-0.06, -0.255, 0.95)], 0.016, 8))
finish(bm, 'Scr_Rope', (0.72, 0.58, 0.34), var=0.25, seed=23, streak=0.5)

# ---------------- 南瓜头（正面画南瓜灯脸）----------------
reset()
HZ, HR, HH = NECK_Z - 0.02, 0.21, 0.34
prof = [(0, 0)] + [(HR * math.sin(math.pi * k / 18) ** 0.75, HH * (1 - math.cos(math.pi * k / 18)) / 2) for k in range(1, 18)] + [(0, HH * 0.95)]
bm = lathe(prof, 56, r_fn=lambda t, z: 1 - 0.08 * (0.5 - 0.5 * math.cos(9 * t)))
for v in bm.verts: v.co += Vector((0, 0, HZ))


def head_uv(ob):
    me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
    for p in me.polygons:
        front = p.normal.y < -0.15
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if front: uvl[li].uv = (0.5 + co.x / HR * 0.5 * 0.95, (co.z - HZ) / HH * 0.95 + 0.02)
            else: uvl[li].uv = (0.02 + 0.06 * (math.atan2(co.y, co.x) / math.tau % 1), 0.02 + 0.06 * (co.z - HZ) / HH)


def face(path):
    N = 512; y, x = np.mgrid[0:N, 0:N].astype(np.float32); u = x / N; v = 1 - y / N
    col = np.tile(np.array([1.0, 0.52, 0.1], np.float32), (N, N, 1))
    col *= (0.88 + 0.12 * np.cos((u - 0.5) * 40))[..., None]          # 南瓜瓣明暗
    glow = np.array([1.0, 0.86, 0.3], np.float32)
    def tri(cx, cy, w, h, up=True):
        yy = (v - cy) / h; xx = (u - cx) / w
        return (yy > 0) & (yy < 1) & (np.abs(xx) < (1 - yy if up else yy)) if True else None
    m = tri(0.36, 0.55, 0.075, 0.16) | tri(0.64, 0.55, 0.075, 0.16) | tri(0.5, 0.44, 0.04, 0.08)
    mouth = (np.abs(u - 0.5) < 0.2) & (v > 0.22) & (v < 0.22 + 0.13 * np.sqrt(np.clip(1 - ((u - 0.5) / 0.2) ** 2, 0, 1)) + 0.0) & (v > 0.3 - 0.11 * np.sqrt(np.clip(1 - ((u - 0.5) / 0.2) ** 2, 0, 1)))
    teeth = mouth & (((u * 40).astype(int) % 3) == 0) & (v > 0.3)
    m = m | (mouth & ~teeth)
    col[m] = glow
    col[0:N // 8, 0:N // 8] = col[0:N // 8, 0:N // 8] * 0 + np.array([0.95, 0.5, 0.1])
    save_rgb(path, col)


finish(bm, 'Scr_Head', tex=face, uv=head_uv)
reset()
st = [(0.01 * math.sin(i / 8 * 2), 0.01 * i / 8, HZ + HH * 0.9 + i / 8 * 0.08) for i in range(9)]
finish(tube(st, 0.025, 8, r_fn=lambda u: 1 - 0.3 * u), 'Scr_Stem', (0.36, 0.33, 0.14), var=0.2, seed=25, cells=3)

# ---------------- 破草帽：帽檐 + 歪帽尖 ----------------
reset()
TZ = HZ + HH * 0.78
brim = lathe([(0, TZ - 0.015), (0.36, TZ - 0.03), (0.38, TZ - 0.012), (0.34, TZ + 0.012), (0, TZ + 0.02)], 40,
             r_fn=lambda t, z: 1 + 0.05 * math.sin(5 * t) + 0.03 * math.sin(11 * t))
bm = brim
cone = []
for k in range(13):
    u = k / 12
    cone.append((0.2 * (1 - u) ** 1.1 + 0.005, TZ + 0.0 + 0.42 * u))
cone[-1] = (0, cone[-1][1])
cb = lathe([(0, TZ - 0.005)] + cone[1:], 24)
for v in cb.verts:      # 帽尖往后（+Y）、往右歪
    u = max(0.0, (v.co.z - TZ) / 0.42)
    v.co.y += 0.18 * u * u; v.co.x += 0.06 * u * u * u
merge(bm, cb)
finish(bm, 'Scr_Hat', (0.5, 0.36, 0.2), var=0.22, dark=(0.36, 0.25, 0.13), seed=27, streak=0.3)

# ---------------- 稻草束 ----------------
reset()
bm = bmesh.new()
def straw(p, d, n, L, spread):
    d = Vector(d).normalized()
    for i in range(n):
        a = rng.uniform(0, math.tau); sp = rng.uniform(0.3, 1) * spread
        side = d.orthogonal().normalized(); side.rotate(Matrix.Rotation(a, 3, d))
        q = Vector(p) + side * 0.02 * rng.uniform(0.2, 1)
        dd = (d + side * sp).normalized() * L * rng.uniform(0.7, 1.1)
        merge(bm, tube([q, q + dd * 0.5, q + dd], 0.008, 5, r_fn=lambda u: 1 - 0.8 * u))
for s in (-1, 1):
    straw((s * 0.64, 0, BAR_Z - 0.03), (s, 0, -0.2), 9, 0.13, 0.5)
for k in range(12):
    a = k / 12 * math.tau
    straw((math.cos(a) * 0.24, math.sin(a) * 0.24, 0.82), (math.cos(a) * 0.3, math.sin(a) * 0.3, -1), 2, 0.14, 0.25)
for k in range(10):
    a = k / 10 * math.tau
    straw((math.cos(a) * 0.17, math.sin(a) * 0.17, TZ - 0.03), (math.cos(a), math.sin(a), -0.6), 2, 0.1, 0.3)
finish(bm, 'Scr_Straw', (0.93, 0.78, 0.36), var=0.2, seed=29, streak=0.4)

json.dump(report, open(os.path.join(OUT, 'scarecrow_report.json'), 'w'), indent=1)
print('SCARECROW', {k: v['faces'] for k, v in report.items()})

"""钟楼 + 墓园部件（分部件程序化）：Blender -b -P Tools/blender_belltower.py -- <输出目录>
共同坐标系：Blender Z 向上，正面朝 -Y（= Unity +Z）。
钟楼（总高约 8m）：Bt_Plinth 草地台 / Bt_Stone 方塔身+腰线+钟亭四角柱+顶板 / Bt_Roof 尖屋顶 / Bt_Bell 铜钟 / Bt_Beam 吊钟横梁
                  Bt_Clock 钟面 / Bt_Hands 指针与钟面边框 / Bt_Door 门 / Bt_Vane 屋顶风向标（蝙蝠）
墓园：Gy_Plinth 椭圆草地台 / Gy_Fence 铁栅栏（立柱带尖头 + 两道横栏，前面留口）"""
import bpy, bmesh, math, sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)


def finish(bm, name, base, **kw):
    ob = to_obj(bm, name); uv_smart(ob)
    noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    export(ob, os.path.join(OUT, f'{name}.fbx'))


def stand(bm, x, y, z):
    """XY 平面薄片（法线 +Z）立成朝 -Y 的竖片，放到 (x, y, z)"""
    M = Matrix.Rotation(math.radians(90), 4, 'X')
    for v in bm.verts: v.co = M @ v.co + Vector((x, y, z))
    return bm


G = 0.35
W = 2.2            # 塔身宽
H1 = 4.6           # 塔身顶
BEL0, BEL1 = H1 + 0.25, H1 + 2.15   # 钟亭
reset()
finish(lathe([(0, 0), (2.3, 0), (2.5, 0.1), (2.45, 0.25), (2.2, G - 0.02), (1.9, G), (0, G)], 40,
             r_fn=lambda t, z: 1 + 0.05 * math.sin(3 * t + 1) + 0.03 * math.sin(8 * t)), 'Bt_Plinth',
       (0.3, 0.5, 0.25), var=0.22, dark=(0.2, 0.36, 0.18), seed=81, cells=8)
reset()
bm = box(0, 0, G + 0.15, W + 0.4, W + 0.4, 0.34, bevel=0.03)                 # 台基
merge(bm, box(0, 0, (G + 0.3 + H1) / 2, W, W, H1 - G - 0.3))                # 塔身
merge(bm, box(0, 0, H1 + 0.1, W + 0.25, W + 0.25, 0.3, bevel=0.03))           # 腰线
for sx in (-1, 1):
    for sy in (-1, 1):
        merge(bm, box(sx * (W / 2 - 0.2), sy * (W / 2 - 0.2), (BEL0 + BEL1) / 2, 0.4, 0.4, BEL1 - BEL0 + 0.02))
merge(bm, box(0, 0, BEL1 + 0.12, W + 0.3, W + 0.3, 0.26, bevel=0.03))        # 钟亭顶板
finish(bm, 'Bt_Stone', (0.55, 0.5, 0.62), var=0.18, dark=(0.4, 0.36, 0.48), seed=83, cells=12)
reset()
TOP = BEL1 + 0.25
roof = lathe([(0, TOP - 0.01), (1.75, TOP - 0.01), (1.7, TOP + 0.15)] + [(1.45 * (1 - k / 10) ** 1.3 + 0.02, TOP + 0.15 + 2.3 * k / 10) for k in range(1, 11)] + [(0, TOP + 2.5)], 4)
rotz_m = Matrix.Rotation(math.radians(45), 4, 'Z')
for v in roof.verts: v.co = rotz_m @ v.co
finish(roof, 'Bt_Roof', (0.27, 0.24, 0.43), var=0.22, dark=(0.18, 0.16, 0.3), seed=85, streak=0.55)
reset()
bz = BEL0 + 0.35
bell = lathe([(0, bz), (0.5, bz), (0.52, bz + 0.06), (0.42, bz + 0.2), (0.33, bz + 0.6), (0.3, bz + 0.85), (0.18, bz + 0.98), (0.06, bz + 1.02), (0.06, bz + 1.2), (0, bz + 1.2)], 32)
finish(bell, 'Bt_Bell', (0.85, 0.62, 0.25), var=0.15, dark=(0.55, 0.42, 0.2), seed=87, cells=5)
reset()
beam = box(0, 0, bz + 1.25, W - 0.4, 0.22, 0.2)
finish(beam, 'Bt_Beam', (0.38, 0.26, 0.17), var=0.2, seed=89, streak=0.5)
# 钟面（正面 -Y，塔身上部）
reset()
CZ = H1 - 0.9
disc = [(0.6 * math.cos(k / 32 * math.tau), 0.6 * math.sin(k / 32 * math.tau)) for k in range(32)]
finish(stand(slab(disc, 0.1), 0, -W / 2 - 0.02, CZ), 'Bt_Clock', (0.95, 0.9, 0.72), var=0.08, seed=91, cells=3)
reset()
hb = torus(0.63, 0.06, 36, 8)
stand(hb, 0, -W / 2 - 0.06, CZ)
h1 = box(0, 0, 0, 0.06, 0.04, 0.42); h2 = box(0, 0, 0, 0.3, 0.04, 0.05)
for v in h1.verts: v.co = Matrix.Rotation(math.radians(20), 4, 'Y') @ v.co + Vector((0.07, -W / 2 - 0.09, CZ + 0.18))
for v in h2.verts: v.co += Vector((0.14, -W / 2 - 0.09, CZ - 0.02))
merge(hb, h1); merge(hb, h2)
for k in range(12):
    a = k / 12 * math.tau
    t = box(0.48 * math.cos(a), -W / 2 - 0.08, CZ + 0.48 * math.sin(a), 0.06, 0.03, 0.06); merge(hb, t)
finish(hb, 'Bt_Hands', (0.18, 0.14, 0.2), var=0.1, seed=93, cells=3)
reset()
arch = [(0.45, -0.7)] + [(0.45 * math.cos(math.pi * k / 10), 0.7 + 0.45 * math.sin(math.pi * k / 10)) for k in range(11)] + [(-0.45, -0.7)]
finish(stand(slab(arch, 0.12), 0, -W / 2 - 0.02, G + 0.3 + 0.7), 'Bt_Door', (0.42, 0.22, 0.13), var=0.2, seed=95, streak=0.5)
# 风向标：竖杆 + 蝙蝠剪影
reset()
VZ = TOP + 2.45
vb = tube([(0, 0, VZ - 0.1), (0, 0, VZ + 0.7)], 0.035, 8)
bat = []
for k in range(41):
    t = k / 40; x = -0.55 + 1.1 * t
    yv = 0.18 * (1 - abs(x) / 0.55) ** 0.5 + 0.06 * math.cos(abs(x) * 25) * (abs(x) > 0.12)
    bat.append((x, yv))
for k in range(41):
    t = k / 40; x = 0.55 - 1.1 * t
    bat.append((x, -0.08 - 0.05 * math.sin(abs(x) * 18) * (abs(x) > 0.12)))
merge(vb, stand(slab(bat[:-1], 0.03), 0, 0, VZ + 0.55))
finish(vb, 'Bt_Vane', (0.15, 0.12, 0.17), var=0.1, seed=97, cells=3)

# ---------------- 墓园 ----------------
reset()
finish(lathe([(0, 0), (1.7, 0), (1.85, 0.08), (1.8, 0.2), (1.6, 0.25), (0, 0.25)], 40,
             r_fn=lambda t, z: (1.25 + 0.25 * math.cos(2 * t)) * (1 + 0.04 * math.sin(5 * t))), 'Gy_Plinth',
       (0.28, 0.42, 0.24), var=0.22, dark=(0.2, 0.3, 0.18), seed=99, cells=8)
reset()
fb = bmesh.new()
pts = []
for k in range(36):
    a = k / 36 * math.tau
    if abs(math.degrees(a) - 270) < 30: continue          # 正面（-Y）留口
    rr = (1.25 + 0.25 * math.cos(2 * a)) * 1.45
    pts.append((a, Vector((math.cos(a) * rr, math.sin(a) * rr, 0.22))))
# 从缺口一侧开始按角度排成一条连续弧（缺口在 270°）
pts.sort(key=lambda t: (t[0] - math.radians(300)) % math.tau)
for a, p in pts:
    merge(fb, tube([p - Vector((0, 0, 0.12)), p + Vector((0, 0, 0.6))], 0.025, 8))
    tip = lathe([(0, p.z + 0.58), (0.055, p.z + 0.6), (0.03, p.z + 0.66), (0, p.z + 0.76)], 8)
    for v in tip.verts: v.co += Vector((p.x, p.y, 0))
    merge(fb, tip)
for hz in (0.18, 0.48):
    merge(fb, tube([p + Vector((0, 0, hz)) for _, p in pts], 0.018, 8))
finish(fb, 'Gy_Fence', (0.16, 0.13, 0.18), var=0.12, seed=101, cells=3)
json.dump(report, open(os.path.join(OUT, 'belltower_report.json'), 'w'), indent=1)
print('BELLTOWER', {k: v['faces'] for k, v in report.items()})

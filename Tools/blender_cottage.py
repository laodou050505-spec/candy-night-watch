"""女巫小屋（分部件程序化）：Blender -b -P Tools/blender_cottage.py -- <输出目录>
共同坐标系：Blender Z 向上，正面朝 -Y（= Unity +Z）。总高约 6.3m。
部件：Ct_Plinth 草地台 / Ct_Wall 圆形石墙（底部石圈）/ Ct_Roof 两层波浪茅草檐 + 歪女巫帽尖 / Ct_Band 帽带
     Ct_Door 拱门 / Ct_Window 圆窗（发光）/ Ct_Trim 门框、窗框、帽扣 / Ct_Chimney 歪烟囱 / Ct_Pumpkin 门口南瓜
     Ct_MushStem + Ct_MushCap 墙脚蘑菇"""
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


def face_front(bm, ang, r, z, push=0.0):
    """把建在 XY 平面（法线 +Z）的薄片立起来贴到圆墙上：方位 ang（0 = 正前 -Y），半径 r，高度 z"""
    M = Matrix.Rotation(math.radians(90), 4, 'X')        # 法线 +Z → -Y
    R = Matrix.Rotation(math.radians(ang), 4, 'Z')
    for v in bm.verts:
        v.co = R @ (M @ v.co + Vector((0, -(r + push), z)))
    return bm


G, WR, WH = 0.4, 1.6, 2.5
# 草地台
reset()
finish(lathe([(0, 0), (2.7, 0), (2.95, 0.12), (2.9, 0.28), (2.65, G - 0.02), (2.3, G), (0, G)], 44,
             r_fn=lambda t, z: 1 + 0.06 * math.sin(4 * t + 0.5) + 0.03 * math.sin(9 * t)), 'Ct_Plinth',
       (0.3, 0.5, 0.25), var=0.22, dark=(0.2, 0.36, 0.18), seed=51, cells=8)
# 石墙
reset()
prof = [(0, G - 0.03), (WR + 0.14, G - 0.03), (WR + 0.16, G + 0.12), (WR + 0.12, G + 0.3), (WR + 0.02, G + 0.36)]
prof += [(WR * (1 + 0.03 * math.sin(math.pi * k / 8)), G + 0.36 + (WH - 0.36) * k / 8) for k in range(9)]
prof += [(0, G + WH)]
finish(lathe(prof, 40, r_fn=lambda t, z: 1 + 0.015 * math.sin(13 * t + z * 4)), 'Ct_Wall',
       (0.56, 0.52, 0.6), var=0.2, dark=(0.42, 0.38, 0.47), seed=53, cells=12)
# 屋顶：两层波浪檐 + 歪帽尖
reset()
R1, R2 = 2.25, 1.75
t1 = lathe([(0, G + WH - 0.1), (R1 * 0.8, G + WH - 0.25), (R1, G + WH - 0.45), (R1 * 0.97, G + WH - 0.3), (R1 * 0.7, G + WH + 0.35), (0, G + WH + 0.6)], 48,
           r_fn=lambda t, z: 1 + 0.07 * math.sin(9 * t) * max(0.0, min(1.0, (G + WH + 0.2 - z) / 0.6)))
t2 = lathe([(0, G + WH + 0.35), (R2 * 0.8, G + WH + 0.3), (R2, G + WH + 0.15), (R2 * 0.96, G + WH + 0.3), (R2 * 0.6, G + WH + 0.95), (0, G + WH + 1.1)], 48,
           r_fn=lambda t, z: 1 + 0.08 * math.sin(7 * t + 1) * max(0.0, min(1.0, (G + WH + 0.7 - z) / 0.6)))
bm = merge(t1, t2)
HB = G + WH + 0.85
cone = [(0, HB)] + [(1.0 * (1 - k / 14) ** 1.2 + 0.01, HB + 2.6 * k / 14) for k in range(14)] + [(0, HB + 2.62)]
hat = lathe(cone, 28)
for v in hat.verts:
    u = max(0.0, (v.co.z - HB) / 2.6); v.co.x += 0.9 * u ** 2.5; v.co.y += 0.35 * u ** 3; v.co.z -= 0.35 * u ** 4
merge(bm, hat)
finish(bm, 'Ct_Roof', (0.36, 0.26, 0.45), var=0.22, dark=(0.25, 0.18, 0.33), seed=55, streak=0.5)
reset()
finish(lathe([(0, HB + 0.05), (0.96, HB + 0.05), (0.98, HB + 0.32), (0, HB + 0.32)], 28), 'Ct_Band', (0.95, 0.55, 0.15), var=0.1, seed=57, cells=3)
# 门（拱形薄板）+ 窗（圆形薄板）+ 框
reset()
arch = [(0.45, -0.65)] + [(0.45 * math.cos(math.pi * k / 10), 0.65 + 0.45 * math.sin(math.pi * k / 10)) for k in range(11)] + [(-0.45, -0.65)]
door = face_front(slab(arch, 0.14), 0, WR - 0.05, G + 0.36 + 0.65)
finish(door, 'Ct_Door', (0.42, 0.22, 0.13), var=0.2, seed=59, streak=0.5)
reset()
circ = [(0.3 * math.cos(k / 20 * math.tau), 0.3 * math.sin(k / 20 * math.tau)) for k in range(20)]
wb = bmesh.new()
for a in (-48, 50):
    merge(wb, face_front(slab(circ, 0.14), a, WR - 0.05, G + 1.55))
finish(wb, 'Ct_Window', (1.0, 0.82, 0.38), var=0.1, seed=61, cells=3)
reset()
fb = bmesh.new()
ring = [(0.33 * math.cos(k / 24 * math.tau), 0.33 * math.sin(k / 24 * math.tau), 0) for k in range(25)]
for a in (-48, 50):
    r_ = torus(0.33, 0.045, 32, 8)
    merge(fb, face_front(r_, a, WR - 0.01, G + 1.55, push=0.05))
    cross = box(0, 0, 0, 0.6, 0.05, 0.05); merge(cross, box(0, 0, 0, 0.05, 0.6, 0.05))
    merge(fb, face_front(cross, a, WR, G + 1.55, push=0.04))
fr = [(0.53 * math.cos(math.pi * k / 12), 0.65 + 0.53 * math.sin(math.pi * k / 12), 0) for k in range(13)]
fr = [(0.53, -0.66, 0)] + fr + [(-0.53, -0.66, 0)]
merge(fb, face_front(tube(fr, 0.06, 8), 0, WR - 0.02, G + 0.36 + 0.65, push=0.06))
buckle = box(0, 0, 0, 0.36, 0.3, 0.08)
for v in buckle.verts: v.co = Matrix.Rotation(math.radians(90), 4, 'X') @ v.co + Vector((0.0, -0.98, HB + 0.18))
merge(fb, buckle)
finish(fb, 'Ct_Trim', (0.28, 0.2, 0.26), var=0.2, seed=63, streak=0.4)
# 歪烟囱（穿过屋檐）
reset()
bm = bmesh.new()
for k in range(5):
    b = box(0, 0, 0, 0.5, 0.5, 0.36, bevel=0.03)
    for v in b.verts:
        v.co = Matrix.Rotation(math.radians(k * 8), 4, 'Z') @ v.co + Vector((0.95 + 0.05 * k, 0.75, G + WH - 0.1 + k * 0.33))
    merge(bm, b)
finish(bm, 'Ct_Chimney', (0.5, 0.36, 0.33), var=0.2, dark=(0.36, 0.25, 0.22), seed=65)
# 南瓜
reset()
bm = bmesh.new()
for a, r in ((-28, 0.24), (24, 0.18), (34, 0.13)):
    h = r * 1.5; ang = math.radians(a); d = WR + 0.55
    pk = lathe([(0, 0)] + [(r * math.sin(math.pi * k / 14) ** 0.8, h * (1 - math.cos(math.pi * k / 14)) / 2) for k in range(1, 14)] + [(0, h * 0.94)], 32,
               r_fn=lambda t, z: 1 - 0.08 * (0.5 - 0.5 * math.cos(8 * t)))
    for v in pk.verts: v.co += Vector((math.sin(ang) * d, -math.cos(ang) * d, G - 0.005))
    merge(bm, pk)
finish(bm, 'Ct_Pumpkin', (1.0, 0.5, 0.1), var=0.14, dark=(0.85, 0.36, 0.06), seed=67, cells=4)
# 蘑菇
reset()
sb = bmesh.new(); cb = bmesh.new()
for a, s in ((-95, 1.0), (-110, 0.7), (115, 0.85)):
    ang = math.radians(a); d = WR + 0.35; px, py = math.sin(ang) * d, -math.cos(ang) * d
    st = lathe([(0, G - 0.01), (0.07 * s, G - 0.01), (0.06 * s, G + 0.3 * s), (0, G + 0.3 * s)], 12)
    cp = lathe([(0, G + 0.27 * s), (0.24 * s, G + 0.25 * s), (0.22 * s, G + 0.33 * s), (0.12 * s, G + 0.42 * s), (0, G + 0.45 * s)], 20)
    for v in st.verts: v.co += Vector((px, py, 0))
    for v in cp.verts: v.co += Vector((px, py, 0))
    merge(sb, st); merge(cb, cp)
finish(sb, 'Ct_MushStem', (0.92, 0.88, 0.78), var=0.1, seed=69, cells=3)
finish(cb, 'Ct_MushCap', (0.6, 0.3, 0.85), var=0.2, dark=(0.95, 0.9, 1.0), seed=71, cells=5)

json.dump(report, open(os.path.join(OUT, 'cottage_report.json'), 'w'), indent=1)
print('COTTAGE', {k: v['faces'] for k, v in report.items()})

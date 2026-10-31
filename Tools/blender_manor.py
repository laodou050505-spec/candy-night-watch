"""鬼屋（分部件程序化）：Blender -b -P Tools/blender_manor.py -- <输出目录>
共同坐标系：Blender Z 向上，正面朝 -Y（= Unity +Z，装配体约定）。总高约 9m（场景里再缩放）。
部件：Mn_Plinth 草地台 / Mn_Walls 主楼两层+侧翼+圆塔墙体 / Mn_Trim 深色木梁、窗框、门廊柱 / Mn_Roof 正面山墙屋顶、塔尖、侧翼尖顶、门廊顶
     Mn_Window 发光窗（Unity 里自发光）/ Mn_Door 门 / Mn_Steps 台阶 / Mn_Chimney 烟囱 / Mn_Pumpkin 门口南瓜灯
每块几何都是封闭体；嵌入式拼装（窗、梁略微嵌进墙里），没有共面重叠。"""
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


def prism_y(x0, x1, y0, y1, z0, h):
    """山墙屋顶：三角截面在 XZ 平面，沿 Y 拉伸（山墙朝前后）"""
    bm = bmesh.new(); xm = (x0 + x1) / 2
    f = [bm.verts.new(p) for p in ((x0, y0, z0), (x1, y0, z0), (xm, y0, z0 + h))]
    b = [bm.verts.new(p) for p in ((x0, y1, z0), (x1, y1, z0), (xm, y1, z0 + h))]
    bm.faces.new(f[::-1]); bm.faces.new(b)
    for i in range(3):
        j = (i + 1) % 3; bm.faces.new((f[i], f[j], b[j], b[i]))
    return bm


def rotz(bm, ang, c=(0, 0, 0)):
    M = Matrix.Translation(c) @ Matrix.Rotation(ang, 4, 'Z') @ Matrix.Translation((-c[0], -c[1], -c[2]))
    for v in bm.verts: v.co = M @ v.co
    return bm


G = 0.45                 # 地台顶面
FY = -1.5                # 一楼正面
UY = -1.6                # 二楼正面（外挑 0.1）
TX, TY, TR = -2.15, -0.6, 0.9

# ---------------- 草地台 ----------------
reset()
prof = [(0, 0), (4.0, 0), (4.25, 0.12), (4.2, 0.3), (3.95, G - 0.02), (3.6, G), (0, G)]
finish(lathe(prof, 48, r_fn=lambda t, z: 1 + 0.06 * math.sin(3 * t) + 0.03 * math.sin(7 * t + 1)), 'Mn_Plinth',
       (0.3, 0.5, 0.25), var=0.22, dark=(0.2, 0.36, 0.18), seed=31, cells=8)

# ---------------- 墙体（一楼、略歪的二楼、侧翼、圆塔）----------------
reset()
bm = box(0, 0, G + 1.6 - 0.02, 4.0, 3.0, 3.2)
merge(bm, rotz(box(0, 0, G + 3.2 + 1.2 - 0.04, 4.2, 3.2, 2.4), math.radians(2)))
merge(bm, box(2.6, 0.4, G + 1.3 - 0.02, 1.6, 2.0, 2.6))
tw = lathe([(0, G - 0.02), (TR, G - 0.02), (TR, G + 6.0), (TR + 0.08, G + 6.1), (0, G + 6.1)], 32)
for v in tw.verts: v.co += Vector((TX, TY, 0))
merge(bm, tw)
finish(bm, 'Mn_Walls', (0.47, 0.42, 0.57), var=0.16, dark=(0.36, 0.32, 0.45), seed=33, streak=0.45)

# ---------------- 窗（发光）+ 窗框（深色梁件）----------------
wins = []   # (cx, cy, cz, w, h, 法线方向 'y-'/'y+'/'x+')
wins += [(-1.15, FY, G + 1.7, 0.9, 1.2, 'y-')]
wins += [(x, UY - 0.03, G + 4.35, 0.75, 1.05, 'y-') for x in (-1.3, 0.05, 1.35)]
wins += [(2.6, -0.6, G + 1.5, 0.7, 0.95, 'y-')]
wins += [(-1.0, 1.5, G + 1.7, 0.9, 1.1, 'y+'), (1.0, 1.6, G + 4.35, 0.8, 1.0, 'y+')]
wins += [(3.4, 0.4, G + 1.5, 0.7, 0.95, 'x+')]
tower_wins = [(G + 2.0, 0.55, 0.85), (G + 3.9, 0.55, 0.85), (G + 5.35, 0.45, 0.6)]
reset()
wb = bmesh.new(); fb = bmesh.new()
for cx, cy, cz, w, h, d in wins:
    if d[0] == 'y':
        merge(wb, box(cx, cy, cz, w, 0.1, h))
        sgn = -1 if d == 'y-' else 1
        merge(fb, box(cx, cy + sgn * 0.04, cz + h / 2 + 0.06, w + 0.24, 0.14, 0.12))     # 上框
        merge(fb, box(cx, cy + sgn * 0.04, cz - h / 2 - 0.06, w + 0.3, 0.18, 0.12))      # 窗台
        merge(fb, box(cx, cy + sgn * 0.045, cz, 0.06, 0.1, h))                            # 竖棂
        merge(fb, box(cx, cy + sgn * 0.045, cz + h * 0.12, w, 0.1, 0.06))                 # 横棂
    else:
        merge(wb, box(cx, cy, cz, 0.1, w, h))
        merge(fb, box(cx + 0.04, cy, cz + h / 2 + 0.06, 0.14, w + 0.24, 0.12))
        merge(fb, box(cx + 0.04, cy, cz - h / 2 - 0.06, 0.18, w + 0.3, 0.12))
        merge(fb, box(cx + 0.045, cy, cz, 0.1, 0.06, h))
for cz, w, h in tower_wins:      # 塔窗朝正前偏左
    a = math.radians(-120)
    p = Vector((TX + math.cos(a) * TR, TY + math.sin(a) * TR, cz))
    b1 = box(0, 0, 0, w, 0.24, h); b2 = box(0, 0, h / 2 + 0.07, w + 0.2, 0.3, 0.12); b3 = box(0, 0, -h / 2 - 0.07, w + 0.24, 0.32, 0.12)
    for bb_ in (b1, b2, b3):
        rotz(bb_, a + math.pi / 2)
        for v in bb_.verts: v.co += p
    merge(wb, b1); merge(fb, b2); merge(fb, b3)
# 山墙圆窗
merge(wb, box(0.05, UY - 0.32, G + 6.35, 0.55, 0.1, 0.55))
merge(fb, box(0.05, UY - 0.36, G + 6.35, 0.7, 0.1, 0.08)); merge(fb, box(0.05, UY - 0.36, G + 6.35, 0.08, 0.1, 0.7))
finish(wb, 'Mn_Window', (1.0, 0.82, 0.38), var=0.1, seed=35, cells=3)
reset()
# 角柱、楼层腰线、门廊柱
for x in (-2.0, 2.0):
    for y in (FY, 1.5):
        merge(fb, box(x, y, G + 1.6, 0.18, 0.18, 3.2))
merge(fb, box(0, FY - 0.02, G + 3.2, 4.3, 0.2, 0.18))
merge(fb, box(0, 1.52, G + 3.2, 4.3, 0.2, 0.18))
for x in (-0.55, 1.25):
    merge(fb, box(x, FY - 1.15, G + 0.1 + 1.16, 0.14, 0.14, 2.32))
finish(fb, 'Mn_Trim', (0.24, 0.18, 0.24), var=0.2, seed=37, streak=0.4)

# ---------------- 屋顶 ----------------
reset()
bm = prism_y(-2.45, 2.45, UY - 0.45, 1.6 + 0.45, G + 5.55, 2.6)                     # 主山墙顶（压在二楼上方）
cone = lathe([(0, G + 5.95), (TR + 0.38, G + 5.95)] + [(0.01 + (TR + 0.3) * (1 - k / 12) ** 1.4, G + 6.1 + 2.7 * k / 12) for k in range(1, 13)] + [(0, G + 8.85)], 24)
for v in cone.verts:
    u = max(0.0, (v.co.z - G - 6.0) / 2.85); v.co.x += 0.35 * u ** 3
    v.co += Vector((TX, TY, 0))
merge(bm, cone)
pyr = lathe([(0, G + 2.5), (1.35, G + 2.5), (0, G + 4.0)], 4)
rotz(pyr, math.radians(45))
for v in pyr.verts: v.co.x *= 0.85; v.co += Vector((2.6, 0.4, 0))
merge(bm, pyr)
porch = prism_y(-0.85, 1.55, FY - 1.45, FY + 0.05, G + 2.38, 0.55)
merge(bm, porch)
finish(bm, 'Mn_Roof', (0.27, 0.24, 0.43), var=0.22, dark=(0.18, 0.16, 0.3), seed=39, streak=0.55)

# ---------------- 门、台阶、烟囱、南瓜 ----------------
reset()
door = box(0.35, FY - 0.03, G + 0.95, 0.95, 0.1, 1.9)
finish(door, 'Mn_Door', (0.42, 0.22, 0.14), var=0.2, seed=41, streak=0.5)
reset()
bm = box(0.35, FY - 0.725, G + 0.05, 2.5, 1.45, 0.1)          # 门廊木平台（嵌进地台 5cm）
merge(bm, box(0.35, FY - 1.6, G + 0.0, 1.5, 0.32, 0.1))      # 一级台阶
finish(bm, 'Mn_Steps', (0.44, 0.31, 0.2), var=0.22, seed=43, streak=0.5)
reset()
bm = box(1.25, 0.7, G + 7.0, 0.55, 0.55, 2.6)
merge(bm, box(1.25, 0.7, G + 8.35, 0.75, 0.75, 0.18))
finish(bm, 'Mn_Chimney', (0.5, 0.32, 0.3), var=0.2, dark=(0.35, 0.22, 0.2), seed=45)
reset()
bm = bmesh.new()
for px, py, r in ((-0.85, FY - 1.8, 0.26), (1.75, FY - 1.75, 0.2), (2.2, FY - 1.45, 0.14)):
    h = r * 1.5
    pk = lathe([(0, 0)] + [(r * math.sin(math.pi * k / 14) ** 0.8, h * (1 - math.cos(math.pi * k / 14)) / 2) for k in range(1, 14)] + [(0, h * 0.94)], 32,
               r_fn=lambda t, z: 1 - 0.08 * (0.5 - 0.5 * math.cos(8 * t)))
    for v in pk.verts: v.co += Vector((px, py, G - 0.005))
    merge(bm, pk)
finish(bm, 'Mn_Pumpkin', (1.0, 0.5, 0.1), var=0.14, dark=(0.85, 0.36, 0.06), seed=47, cells=4)

json.dump(report, open(os.path.join(OUT, 'manor_report.json'), 'w'), indent=1)
print('MANOR', {k: v['faces'] for k, v in report.items()})

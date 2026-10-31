"""环廊拱柱（分部件程序化）：Blender -b -P Tools/blender_column.py -- <输出目录>
部件（共同坐标系，Blender Z 向上；柱轴在原点；拱臂伸向 -Y = Unity +Z；正面 +X = Unity -X）：
  Col_Stone   两层柱基 + 带石缝凹槽的柱身 + 柱头 + 顶板（各自封闭）
  Col_Arch    7 块拱石组成的半拱（块与块留 2cm 缝，像真的砌石）
  Col_Vine    绕柱螺旋的藤 + 5 根卷须
  Col_Leaf    沿藤分布的心形叶
  Col_Pumpkin 顶板上的小南瓜（8 瓣）
  Col_Stem    南瓜梗
每个部件都过封闭流形检查；贴图为平滑噪声卡通贴图。"""
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


ABACUS_TOP = 6.23
# ---------------- 石柱 ----------------
reset()
bm = box(0, 0, 0.175, 1.3, 1.3, 0.35, bevel=0.03)
merge(bm, box(0, 0, 0.465, 1.08, 1.08, 0.25, bevel=0.03))
prof = [(0, 0.58), (0.5, 0.58), (0.53, 0.62), (0.53, 0.68), (0.47, 0.72), (0.42, 0.75)]
z = 0.75
for k in range(4):                     # 柱身分 4 段，段间一道石缝凹槽
    z1 = 0.75 + (k + 1) * 1.2
    r = 0.42 - 0.01 * k
    prof += [(r, z1 - 0.05), (r - 0.025, z1 - 0.03), (r - 0.025, z1 - 0.01), (r - 0.01, z1)]
prof += [(0.38, 5.55), (0.42, 5.62), (0.5, 5.78), (0.58, 5.92), (0.6, 5.99), (0, 5.99)]
merge(bm, lathe(prof, 32))
merge(bm, box(0, 0, (5.98 + ABACUS_TOP) / 2, 1.4, 1.4, ABACUS_TOP - 5.98, bevel=0.03))
finish(bm, 'Col_Stone', (0.52, 0.47, 0.64), var=0.16, dark=(0.36, 0.32, 0.46), seed=3)

# ---------------- 拱石 ----------------
reset()
RI, RO, DEP = 1.3, 1.9, 0.6
RM = (RI + RO) / 2
cen = (-RM, ABACUS_TOP - 0.01)       # 圆心在 -Y 侧，a=0 处正好落在柱顶
bm = bmesh.new()
N = 7; A1 = math.radians(150); gap = math.radians(1.0)
for k in range(N):
    a0 = A1 * k / N + (gap if k else 0); a1 = A1 * (k + 1) / N - gap
    merge(bm, arc_block(cen, RI, RO + (0.06 if k == N - 1 else 0), a0, a1, DEP))
finish(bm, 'Col_Arch', (0.55, 0.5, 0.67), var=0.18, dark=(0.38, 0.33, 0.48), seed=5)

# ---------------- 藤 + 卷须 ----------------
reset()
RV, TURNS, Z0, Z1 = 0.5, 2.6, 0.82, 5.45
pts = []
for i in range(181):
    u = i / 180; a = u * TURNS * math.tau + 0.4
    pts.append((math.cos(a) * RV, math.sin(a) * RV, Z0 + (Z1 - Z0) * u))
bm = tube(pts, 0.06, 12, r_fn=lambda u: 1.1 - 0.4 * u)
VINE = pts
for k, u in enumerate([0.12, 0.3, 0.48, 0.66, 0.84]):
    p = Vector(VINE[int(u * 180)]); out = Vector((p.x, p.y, 0)).normalized()
    side = out.cross(Vector((0, 0, 1)))
    cur = []
    for i in range(40):        # 平面螺线卷须，从藤上向外长出
        t = i / 39; ang = t * 2.6 * math.pi; rr = 0.16 * (1 - 0.75 * t)
        q = p + out * (0.03 + 0.12 * t) + side * (math.sin(ang) * rr * (1 if k % 2 else -1)) + Vector((0, 0, (1 - math.cos(ang)) * rr * 0.8))
        cur.append(q)
    merge(bm, tube(cur, 0.016, 8, r_fn=lambda u: 1 - 0.6 * u))
finish(bm, 'Col_Vine', (0.22, 0.42, 0.16), var=0.2, seed=7, cells=6)

# ---------------- 叶子 ----------------
reset()
heart = []
for i in range(28):
    t = i / 28 * math.tau
    x = 16 * math.sin(t) ** 3; y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
    heart.append((x / 17 * 0.26, -y / 17 * 0.26))     # 尖端朝 +Y 方向翻到下面：叶柄在 y≈+0.07（心形凹口）
bm = bmesh.new()
for k in range(18):
    u = 0.04 + 0.92 * k / 17
    p = Vector(VINE[int(u * 180)]); out = Vector((p.x, p.y, 0)).normalized()
    leaf = slab(heart, 0.018, bend=0.8)
    # 叶面朝外：局部 Z（叶面法线）→ out；叶尖朝下斜
    tilt = Matrix.Rotation(math.radians(25 if k % 2 else -25), 4, 'Z')
    rot = (Vector((0, 0, 1)).rotation_difference(out).to_matrix().to_4x4())
    for v in leaf.verts: v.co = rot @ (tilt @ v.co) + p + out * 0.1 + Vector((0, 0, -0.06))
    merge(bm, leaf)
finish(bm, 'Col_Leaf', (0.36, 0.66, 0.26), var=0.18, dark=(0.25, 0.5, 0.18), seed=9, cells=5)

# ---------------- 小南瓜 + 梗 ----------------
reset()
PX, PY, PR, PH = 0.48, 0.48, 0.24, 0.36
prof = [(0, 0)] + [(PR * math.sin(math.pi * k / 16) ** 0.8, PH * (1 - math.cos(math.pi * k / 16)) / 2) for k in range(1, 16)] + [(0, PH * 0.94)]
bm = lathe(prof, 48, r_fn=lambda t, z: 1 - 0.09 * (0.5 - 0.5 * math.cos(8 * t)))
for v in bm.verts: v.co += Vector((PX, PY, ABACUS_TOP - 0.005))
finish(bm, 'Col_Pumpkin', (1.0, 0.5, 0.1), var=0.14, dark=(0.85, 0.36, 0.06), seed=11, cells=4)
reset()
st = [(PX + 0.03 * math.sin(i / 10 * 2), PY, ABACUS_TOP + PH * 0.86 + i / 10 * 0.11) for i in range(11)]
finish(tube(st, 0.028, 10, r_fn=lambda u: 1 - 0.3 * u), 'Col_Stem', (0.36, 0.33, 0.14), var=0.2, seed=13, cells=3)

json.dump(report, open(os.path.join(OUT, 'column_report.json'), 'w'), indent=1)
print('COLUMN', report)

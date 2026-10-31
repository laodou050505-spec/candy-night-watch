"""维多利亚（安妮女王式）大庄园（分部件程序化）：Blender -b -P Tools/blender_mansion.py -- <输出目录>
本地坐标（Unity）：原点在正立面中下方，正面朝 +Z（玩家方向），楼体向 -Z 延伸。约 18m 宽、19m 高。
部件：Ms_Wall 墙体（主楼三层、前凸侧翼、圆塔、门廊台基）/ Ms_Trim 奶油色木饰（角板、层间腰线、檐口、窗框、门廊柱与栏杆）
     Ms_Roof 石板屋顶（主坡顶、正面山墙、侧翼山墙、塔顶锥、门廊顶）/ Ms_Window 发光窗 / Ms_Door 大门
     Ms_Chimney 烟囱 / Ms_Iron 屋脊铁花与风向标 / Ms_Steps 门前台阶"""
import bpy, bmesh, math, sys, os, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)


def finish(bm, name, base, **kw):
    ob = to_obj(bm, name); uv_smart(ob)
    noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    export(ob, os.path.join(OUT, f'{name}.fbx'))


def prism(x0, x1, z0, z1, y0, h, along='x'):
    """坡屋顶：along='x' 屋脊沿 X（山墙朝左右）；along='z' 屋脊沿 Z（山墙朝前后）。Unity 坐标"""
    bm = bmesh.new()
    if along == 'z':
        xm = (x0 + x1) / 2
        f = [bm.verts.new(U(*p)) for p in ((x0, y0, z1), (x1, y0, z1), (xm, y0 + h, z1))]
        b = [bm.verts.new(U(*p)) for p in ((x0, y0, z0), (x1, y0, z0), (xm, y0 + h, z0))]
    else:
        zm = (z0 + z1) / 2
        f = [bm.verts.new(U(*p)) for p in ((x0, y0, z0), (x0, y0, z1), (x0, y0 + h, zm))]
        b = [bm.verts.new(U(*p)) for p in ((x1, y0, z0), (x1, y0, z1), (x1, y0 + h, zm))]
    bm.faces.new(f); bm.faces.new(b[::-1])
    for i in range(3):
        j = (i + 1) % 3; bm.faces.new((f[j], f[i], b[i], b[j]))
    return bm


def cyl(x, z, r, y0, y1, seg=24):
    c = lathe([(0, y0), (r, y0), (r, y1), (0, y1)], seg)
    for v in c.verts: v.co = Vector((v.co.x, v.co.y, v.co.z)) + U(x, 0, z)
    return c


FL = 3.3                      # 层高
W0, W1 = -7.0, 7.0            # 主楼 x 范围
D = 8.0                       # 主楼进深（z 从 0 到 -D）
WX0, WX1, WZ = 2.4, 7.4, 2.2  # 前凸侧翼 x 范围、前凸量
TX, TZ, TR = -7.2, -0.6, 2.3  # 圆塔
ROOF0 = 3 * FL                # 主楼檐口高
walls, trim, roof, win, iron = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
reset()
# ---------- 墙体 ----------
merge(walls, ubox(W0 - 0.3, W1 + 0.3, 0, 0.9, -D - 0.3, 0.35, 0.03))                 # 石砌台基
merge(walls, ubox(W0, W1, 0.85, ROOF0, -D, 0))                                     # 主楼
merge(walls, ubox(WX0, WX1, 0.85, ROOF0 - FL * 0.0, -1.0, WZ))                      # 前凸侧翼（三层）
merge(walls, ubox(WX0 - 0.25, WX1 + 0.25, 0, 0.9, -1.0, WZ + 0.3, 0.03))
merge(walls, cyl(TX, TZ, TR, 0.0, ROOF0 + FL * 0.6, 28))                           # 圆塔（比主楼高半层）
# ---------- 屋顶 ----------
merge(roof, prism(W0 - 0.5, W1 + 0.5, -D - 0.5, 0.5, ROOF0 - 0.05, 4.6, 'x'))      # 主坡顶，屋脊沿 X
merge(roof, prism(-2.6, 1.6, -3.5, 0.9, ROOF0 - 0.05, 4.3, 'z'))                   # 正面山墙（屋脊沿 Z）
merge(roof, prism(WX0 - 0.45, WX1 + 0.45, -3.0, WZ + 0.6, ROOF0 - 0.05, 4.0, 'z'))  # 侧翼山墙
TY = ROOF0 + FL * 0.6
tc = lathe([(0, TY - 0.05), (TR + 0.45, TY - 0.05), (TR + 0.4, TY + 0.25)] + [((TR + 0.3) * (1 - k / 14) ** 1.25 + 0.02, TY + 0.25 + 6.2 * k / 14) for k in range(1, 15)] + [(0, TY + 6.5)], 28)
for v in tc.verts:
    u = max(0.0, (v.co.z - TY) / 6.5); v.co.x -= 0.5 * u ** 3        # 塔尖微微歪
    v.co += U(TX, 0, TZ)
merge(roof, tc)
# 山墙面（三角墙，墙体材质）填在屋顶正面下方，避免看到屋顶内部
merge(walls, prism(-2.45, 1.45, 0.2, 0.75, ROOF0 - 0.05, 4.05, 'z'))
merge(walls, prism(WX0 - 0.3, WX1 + 0.3, WZ - 0.4, WZ + 0.05, ROOF0 - 0.05, 3.75, 'z'))
# 门廊（主楼正面左段到侧翼）：平台、柱、顶
PZ0, PZ1, PX0, PX1 = 0.0, 2.6, -4.6, WX0 - 0.05
merge(walls, ubox(PX0, PX1, 0, 0.9, PZ0, PZ1, 0.02))
merge(roof, ubox(PX0 - 0.25, PX1 + 0.1, FL + 0.55, FL + 0.75, PZ0, PZ1 + 0.35, 0.02))
for x in np.linspace(PX0 + 0.15, PX1 - 0.15, 6):
    merge(trim, ubox(x - 0.1, x + 0.1, 0.9, FL + 0.56, PZ1 - 0.2, PZ1, 0.01))
    merge(trim, ubox(x - 0.16, x + 0.16, FL + 0.35, FL + 0.56, PZ1 - 0.26, PZ1 + 0.06, 0.01))
for y in (1.1, 1.75):
    for a, b in ((PX0 + 0.15, -1.25), (0.25, PX1 - 0.15)):          # 栏杆（中间留台阶口）
        merge(trim, ubox(a, b, y - 0.04, y + 0.04, PZ1 - 0.14, PZ1 - 0.06))
xx = PX0 + 0.3
while xx < PX1 - 0.2:
    if not (-1.25 < xx < 0.25): merge(trim, ubox(xx - 0.025, xx + 0.025, 1.1, 1.75, PZ1 - 0.12, PZ1 - 0.08))
    xx += 0.16
steps = bmesh.new()
for k in range(4):
    merge(steps, ubox(-1.2 - 0.02 * k, 0.2 + 0.02 * k, -0.02 * k, 0.9 - k * 0.22, PZ1 - 0.05 + 0.01 * k, PZ1 + 0.32 + k * 0.32, 0.01))   # 每级往下一级嵌套，避免顶点重合
# ---------- 窗 ----------
def window(x, y, z, w, h, nz=1, side=None):
    """正面窗（法线 +Z）：发光玻璃 + 奶油色窗框/窗台/上楣 + 十字窗棂"""
    merge(win, ubox(x - w / 2, x + w / 2, y - h / 2, y + h / 2, z - 0.05, z + 0.05))
    merge(trim, ubox(x - w / 2 - 0.12, x + w / 2 + 0.12, y + h / 2, y + h / 2 + 0.16, z - 0.02, z + 0.14, 0.01))
    merge(trim, ubox(x - w / 2 - 0.1, x + w / 2 + 0.1, y - h / 2 - 0.12, y - h / 2, z - 0.02, z + 0.16, 0.01))
    for sx in (-1, 1): merge(trim, ubox(x + sx * w / 2 - 0.06, x + sx * w / 2 + 0.06, y - h / 2, y + h / 2, z - 0.02, z + 0.1))
    merge(trim, ubox(x - 0.035, x + 0.035, y - h / 2, y + h / 2, z, z + 0.09))
    merge(trim, ubox(x - w / 2, x + w / 2, y + h * 0.15 - 0.035, y + h * 0.15 + 0.035, z, z + 0.09))
for f in range(3):
    y = 0.85 + f * FL + 1.65
    for x in (-5.6, -3.4, 1.0) if f == 0 else (-5.6, -3.4, -1.2, 1.0):
        if f == 0 and x == 1.0: continue
        window(x, y, 0.0, 1.0, 1.7)
    for x in (3.6, 6.2):
        window(x, y, WZ, 1.0, 1.7)
window(-0.5, ROOF0 + 1.5, 0.75, 0.9, 1.3)                 # 正面山墙阁楼窗
window(4.9, ROOF0 + 1.4, WZ + 0.05, 1.0, 1.3)              # 侧翼山墙窗
# 圆塔窗：朝前偏右（面向玩家）
for f in range(4):
    a = math.radians(70)
    p = (TX + math.cos(a) * TR, 0.85 + f * FL + 1.65 if f < 3 else ROOF0 + 1.2, TZ + math.sin(a) * TR)
    gb = ubox(-0.45, 0.45, -0.8 if f < 3 else -0.55, 0.8 if f < 3 else 0.55, -0.25, 0.08)
    fr = ubox(-0.58, 0.58, 0.8 if f < 3 else 0.55, 0.96 if f < 3 else 0.7, -0.2, 0.14, 0.01)
    sl = ubox(-0.55, 0.55, -0.92 if f < 3 else -0.67, -0.8 if f < 3 else -0.55, -0.2, 0.16, 0.01)
    for b_ in (gb, fr, sl):
        M = Matrix.Rotation(a - math.pi / 2, 4, "Z")     # 让 +Z（Blender -Y）法线朝塔外
        for v in b_.verts: v.co = M @ v.co + U(*p)
    merge(win, gb); merge(trim, fr); merge(trim, sl)
# 大门
door = ubox(-0.95, 0.05, 0.9, 3.3, -0.06, 0.06)
merge(trim, ubox(-1.15, 0.25, 3.3, 3.55, -0.04, 0.16, 0.01))
merge(win, ubox(-0.85, -0.05, 3.55, 3.95, -0.05, 0.05))          # 门楣气窗
# ---------- 木饰：角板、腰线、檐口 ----------
for x in (W0, W1):
    merge(trim, ubox(x - 0.12, x + 0.12, 0.85, ROOF0, -0.12, 0.12))
for x in (WX0, WX1):
    merge(trim, ubox(x - 0.12, x + 0.12, 0.85, ROOF0, WZ - 0.12, WZ + 0.12))
for f in (1, 2):
    merge(trim, ubox(W0 - 0.05, WX0, 0.85 + f * FL - 0.1, 0.85 + f * FL + 0.1, -0.05, 0.12))
    merge(trim, ubox(WX0 - 0.05, WX1 + 0.05, 0.85 + f * FL - 0.1, 0.85 + f * FL + 0.1, WZ - 0.05, WZ + 0.12))
merge(trim, ubox(W0 - 0.2, W1 + 0.2, ROOF0 - 0.35, ROOF0, -0.1, 0.2))
merge(trim, ubox(WX0 - 0.2, WX1 + 0.2, ROOF0 - 0.35, ROOF0, WZ - 0.1, WZ + 0.2))
# 山墙锯齿挂檐（gingerbread）：沿山墙两条斜边的一串小三角
def gable_trim(x0, x1, z, y0, h, n=10):
    xm = (x0 + x1) / 2
    for side in (-1, 1):
        for k in range(n):
            t = (k + 0.5) / n
            x = xm + side * (x1 - xm) * (1 - t); y = y0 + h * t - 0.25
            tb = ubox(x - 0.08, x + 0.08, y - 0.18, y, z, z + 0.08)
            merge(trim, tb)
gable_trim(-2.6, 1.6, 0.9, ROOF0, 4.3)
gable_trim(WX0 - 0.45, WX1 + 0.45, WZ + 0.6, ROOF0, 4.0)
# ---------- 烟囱 ----------
chim = bmesh.new()
for x, z in ((-4.5, -5.0), (5.0, -6.0)):
    merge(chim, ubox(x - 0.45, x + 0.45, ROOF0, ROOF0 + 6.2, z - 0.45, z + 0.45))
    merge(chim, ubox(x - 0.6, x + 0.6, ROOF0 + 6.2, ROOF0 + 6.5, z - 0.6, z + 0.6, 0.02))
# ---------- 屋脊铁花 + 风向标 ----------
ry = ROOF0 - 0.05 + 4.6; rz = (-D - 0.5 + 0.5) / 2
merge(iron, tube([U(W0, ry + 0.02, rz), U(W1, ry + 0.02, rz)], 0.03, 6))
x = W0 + 0.2
while x < W1:
    merge(iron, tube([U(x, ry, rz), U(x, ry + 0.45, rz)], 0.02, 6))
    tip = lathe([(0, 0), (0.06, 0.0), (0, 0.16)], 6)
    for v in tip.verts: v.co += U(x, ry + 0.45, rz)
    merge(iron, tip); x += 0.5
vane = TY + 6.4
VX = TX + 0.5                                           # 塔尖歪向 Unity +X 0.5m（上面 v.co.x -= 0.5u³ 在 Blender 里是 -X）
bend = lambda y: TX + 0.5 * max(0.0, (y - TY) / 6.5) ** 3
merge(iron, tube([U(bend(TY + 5.7), TY + 5.7, TZ), U(bend(TY + 6.2), TY + 6.2, TZ), U(VX, TY + 6.5, TZ), U(VX, vane + 1.55, TZ)], 0.04, 6))   # 杆从塔尖里顺着歪的方向长出来
ball = lathe([(0, -0.1)] + [(0.1 * math.sin(math.pi * k / 8), -0.1 * math.cos(math.pi * k / 8)) for k in range(1, 8)] + [(0, 0.1)], 12)
for v in ball.verts: v.co += U(VX, vane + 0.45, TZ)
merge(iron, ball)
for k in range(4):                                      # 十字方位臂：从杆芯里伸出（起点在杆半径以内），端头各带一颗小尖
    a = k * math.pi / 2 + 0.05
    merge(iron, tube([U(VX + 0.02 * math.cos(a), vane + 0.8, TZ + 0.02 * math.sin(a)), U(VX + 0.45 * math.cos(a), vane + 0.8, TZ + 0.45 * math.sin(a))], 0.02, 6))
    tip = lathe([(0, -0.03), (0.035, 0.0), (0, 0.07)], 6)
    for v in tip.verts: v.co += U(VX + 0.47 * math.cos(a), vane + 0.8, TZ + 0.47 * math.sin(a))
    merge(iron, tip)
# 箭头 + 尾旗：箭杆横穿主杆，箭头三角板、尾旗都压在箭杆上
merge(iron, ubox(VX - 0.55, VX + 0.5, vane + 1.22, vane + 1.27, TZ - 0.02, TZ + 0.02))
head = slab([(0.0, -0.13), (0.0, 0.13), (-0.26, 0.0)], 0.03)        # 箭头三角（局部 -X = Unity +X）
for v in head.verts: v.co = Matrix.Rotation(math.pi / 2, 4, 'X') @ v.co + U(VX + 0.47, vane + 1.245, TZ)
merge(iron, head)
flag = bmesh.new()
for (x0, x1, y0, y1) in ((-0.62, -0.5, 1.12, 1.4), (-0.5, -0.38, 1.16, 1.36), (-0.38, -0.3, 1.2, 1.32)):
    merge(flag, ubox(VX + x0, VX + x1 + 0.01, vane + y0, vane + y1, TZ - 0.015, TZ + 0.015))
merge(iron, flag)
finish(walls, 'Ms_Wall', (0.42, 0.38, 0.5), var=0.18, dark=(0.32, 0.29, 0.4), seed=301, streak=0.5)
finish(trim, 'Ms_Trim', (0.86, 0.82, 0.74), var=0.12, dark=(0.7, 0.66, 0.6), seed=303, cells=6)
finish(roof, 'Ms_Roof', (0.22, 0.22, 0.32), var=0.25, dark=(0.15, 0.15, 0.24), seed=305, streak=0.6)
finish(win, 'Ms_Window', (1.0, 0.8, 0.42), var=0.12, seed=307, cells=4)
finish(door, 'Ms_Door', (0.36, 0.17, 0.12), var=0.2, seed=309, streak=0.5)
finish(chim, 'Ms_Chimney', (0.48, 0.3, 0.27), var=0.22, dark=(0.34, 0.2, 0.18), seed=311, cells=10)
finish(iron, 'Ms_Iron', (0.12, 0.11, 0.14), var=0.1, seed=313, cells=3)
finish(steps, 'Ms_Steps', (0.55, 0.52, 0.5), var=0.18, seed=315, cells=8)
json.dump(report, open(os.path.join(OUT, 'mansion_report.json'), 'w'), indent=1)
print('MANSION', {k: v['faces'] for k, v in report.items()})

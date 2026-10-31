"""桌腿旁的蛛网梯（小鬼从地面爬上桌的路）：Blender -b -P Tools/blender_ladder.py -- <输出目录>
本地坐标（Unity）：原点在梯脚中点（地面），+Z 指向桌子（往里），+Y 向上；梯顶搭在桌沿外缘 (0, 0.745, 0.15)。
规则层的爬梯路线就是从原点到梯顶的这条中线（Game.LegFloor → Game.LadderTop），小鬼正好走在两根边丝之间。
  Wl_Silk  两根边丝 + 横档 + 几缕下垂的乱丝（发白、微微发光）
  Wl_Web   梯顶两角（边丝与桌沿夹角）各一小片扇形蛛网"""
import bpy, bmesh, math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)
TOP = (0.0, 0.745, 0.15)
HW = 0.075                       # 梯子半宽
R = 0.0028                       # 丝的粗细


def P(x, y, z): return U(x, y, z)


def rail(sx):
    pts = []
    for t in np.linspace(0, 1, 14):
        # 边丝从桌沿垂到地面，中段略往外鼓（像被拉着的丝）
        y = TOP[1] * t; z = TOP[2] * t + 0.02 * math.sin(math.pi * t)
        pts.append(P(sx * HW * (1 - 0.15 * t), y, z))
    return pts


reset()
bm = bmesh.new()
for sx in (-1, 1): merge(bm, tube(rail(sx), R, 6))
n = 9
for k in range(1, n):
    t = k / n
    y = TOP[1] * t; z = TOP[2] * t + 0.02 * math.sin(math.pi * t); hw = HW * (1 - 0.15 * t)
    # 横档两端插进边丝里，中间微微下垂
    merge(bm, tube([P(-hw - 0.001, y, z), P(0, y - 0.008, z + 0.002), P(hw + 0.001, y, z)], R * 0.85, 5))
for sx, t0, t1 in ((-1, 0.3, 0.55), (1, 0.62, 0.85), (-1, 0.75, 0.95)):
    # 几缕从边丝上搭到下一档的斜丝
    a = rail(sx)[int(t0 * 13)]; b = rail(-sx)[int(t1 * 13)]
    mid = (a + b) * 0.5 + Vector((0, 0, -0.012))
    merge(bm, tube([a, mid, b], R * 0.6, 4))
ob = to_obj(bm, 'Wl_Silk'); uv_smart(ob)
noise_tex(os.path.join(OUT, 'Wl_Silk_albedo.png'), (0.9, 0.92, 0.98), var=0.04, seed=3, cells=3)
export(ob, os.path.join(OUT, 'Wl_Silk.fbx'))

# 梯顶两角的小蛛网：边丝顶端与桌沿夹角处各一片扇形网，往外侧张开（中间留给小鬼爬过去）
reset()
web = bmesh.new()
for sx in (-1, 1):
    hw = HW * 0.85
    c = P(sx * hw, TOP[1] - 0.004, TOP[2] - 0.004)
    def D(a, r):                                   # Unity 平面内：往外侧（sx）和往下
        return c + (P(sx * math.cos(a) * r, -math.sin(a) * r, 0) - P(0, 0, 0))
    for k in range(4):
        a = math.pi / 2 * (0.08 + 0.84 * k / 3)
        merge(web, tube([c, D(a, 0.075)], R * 0.7, 4))
    for ring in (0.028, 0.05, 0.07):
        merge(web, tube([D(math.pi / 2 * (0.08 + 0.84 * k / 6), ring * (0.92 if k % 2 else 1.0)) for k in range(7)], R * 0.55, 4))
ob = to_obj(web, 'Wl_Web'); uv_smart(ob)
noise_tex(os.path.join(OUT, 'Wl_Web_albedo.png'), (0.92, 0.94, 1.0), var=0.03, seed=4, cells=3)
export(ob, os.path.join(OUT, 'Wl_Web.fbx'))
print('LADDER', {k: v['faces'] for k, v in report.items()})

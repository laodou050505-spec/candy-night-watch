"""花园 v6 第三批：成组小场景用的部件（本地原点，Blender Z 向上）。
  Fp_Stone 篝火石圈 / Fp_Log 交叉木柴 / Fp_Fire 火舌（自发光，Unity 里闪烁）/ Fp_Ember 炭火底 / Fp_Tripod 三脚铁架 + 吊链
用法：Blender -b -P Tools/blender_garden4.py -- <输出目录>"""
import bpy, bmesh, math, sys, os, json, random
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)
R = random.Random(3)


def finish(bm, name, base, **kw):
    ob = to_obj(bm, name); uv_smart(ob)
    noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    export(ob, os.path.join(OUT, f'{name}.fbx'))


reset()
st = bmesh.new()
for k in range(10):
    a = k / 10 * math.tau + R.uniform(-0.1, 0.1)
    merge(st, blob(math.cos(a) * 0.58, math.sin(a) * 0.58, 0.07, 0.26, 0.22, 0.2, amp=0.03, freq=6, cuts=5, seed=k))
finish(st, 'Fp_Stone', (0.42, 0.4, 0.46), var=0.25, dark=(0.3, 0.29, 0.34), seed=601, cells=10)
reset()
lg = bmesh.new()
for k in range(5):
    a = k / 5 * math.tau
    base = Vector((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.03)); top = Vector((math.cos(a) * 0.06, math.sin(a) * 0.06, 0.42))
    merge(lg, tube([base, base.lerp(top, 0.5), top], 0.055, 8, r_fn=lambda u: 1 - 0.25 * u))
finish(lg, 'Fp_Log', (0.3, 0.2, 0.14), var=0.3, dark=(0.12, 0.08, 0.07), seed=603, streak=0.6)
reset()
em = blob(0, 0, 0.03, 0.62, 0.62, 0.08, amp=0.02, freq=8, cuts=6, seed=9)
finish(em, 'Fp_Ember', (1.0, 0.35, 0.08), var=0.4, dark=(0.25, 0.06, 0.03), seed=605, cells=12)
reset()
fr = bmesh.new()
for k in range(9):
    a = R.uniform(0, math.tau); rr = R.uniform(0.0, 0.2); H = R.uniform(0.28, 0.5); w = R.uniform(0.07, 0.13)
    prof = [(0, 0)] + [(w * math.sin(math.pi * min(1, t * 1.6)) ** 0.7 * (1 - t) ** 0.8 + 0.002, H * t) for t in np.linspace(0.05, 0.95, 10)] + [(0, H)]
    fl = lathe(prof, 10)
    lean = Vector((R.uniform(-0.12, 0.12), R.uniform(-0.12, 0.12), 0))
    for v in fl.verts:
        t = v.co.z / H
        v.co = Vector((v.co.x + lean.x * t * t + math.cos(a) * rr, v.co.y + lean.y * t * t + math.sin(a) * rr, v.co.z + 0.12))
    merge(fr, fl)
finish(fr, 'Fp_Fire', (1.0, 0.62, 0.15), var=0.2, dark=(1.0, 0.85, 0.4), seed=607, cells=4)
reset()
tp = bmesh.new()
apex = Vector((0, 0, 1.75))
for k in range(3):
    a = k / 3 * math.tau + 0.4
    foot = Vector((math.cos(a) * 0.95, math.sin(a) * 0.95, -0.02))
    merge(tp, tube([foot, foot.lerp(apex, 0.5), apex + Vector((math.cos(a) * 0.04, math.sin(a) * 0.04, 0.06))], 0.028, 8))
for sx in (-1, 1):       # 两根吊链：从顶点到坩埚两侧的拉环
    end = Vector((sx * 0.46, 0, 0.9))
    for i in range(12):
        p = apex.lerp(end, (i + 0.5) / 12)
        rg = torus(0.025, 0.007, 10, 5)
        M = Matrix.Rotation(math.pi / 2 * (i % 2), 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'X')
        for v in rg.verts: v.co = M @ v.co + p
        merge(tp, rg)
finish(tp, 'Fp_Tripod', (0.15, 0.13, 0.16), var=0.1, seed=609, cells=3)
json.dump(report, open(os.path.join(OUT, 'garden4_report.json'), 'w'), indent=1)
print('GARDEN4', {k: v['faces'] for k, v in report.items()})

"""卡通手套（方案 A：奶油白手套 + 南瓜橙袖口）的部件，运行时由手部追踪关节摆放：
  Blender -b -P Tools/blender_glove.py -- <输出目录>
全部按“归一化尺寸”建模，Unity 里按玩家真实手的尺寸缩放（Unity 坐标：+Z 指向手指，+Y 是手背）：
  Gl_Palm  掌部：鼓鼓的圆角软垫，x∈[-0.5,0.5] 宽、y∈[-0.5,0.5] 厚、z∈[0,1] 从手腕到指根；手背有三道深色缝线（贴图）
  Gl_Cuff  袖口：喇叭形翻边筒，中心线沿 Z，手腕处半径 1，z∈[-1,0.2]（往小臂方向张开），口沿卷一圈
  Gl_Seg   指节：圆头圆柱，长度 z∈[0,1]、半径 1（两端圆顶藏进关节球里）
  Gl_Ball  关节球：半径 1 的球（比 Unity 自带球更圆、法线连续，描边不断）"""
import bpy, bmesh, math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)


def save_rgb(path, col):
    N = col.shape[0]
    rgba = np.concatenate([np.clip(col, 0, 1), np.ones((N, N, 1), np.float32)], -1).astype(np.float32)
    im = bpy.data.images.new(os.path.basename(path), N, N, alpha=False); im.pixels = rgba[::-1].ravel()
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()


CREAM = np.array([0.98, 0.93, 0.83], np.float32)

# ---------- 掌部：圆角软垫 ----------
reset()
palm = blob(*U(0, 0, 0.5), 1.0, 1.0, 1.0, amp=0.0, freq=1, cuts=10, seed=1)
for v in palm.verts:
    # 圆角方块 → 往椭球再推一半：四角圆润，像鼓鼓的软垫
    c = v.co - U(0, 0, 0.5); L = c.length
    if L > 1e-6:
        v.co = U(0, 0, 0.5) + c.lerp(c.normalized() * 0.5, 0.45)
for v in palm.verts:
    # 手背更鼓、掌心略平；指根一侧略宽（像真手套）
    x, y, z = -v.co.x, v.co.z, -v.co.y               # Blender → Unity
    zz = (z - 0.0)                                    # 0 手腕 … 1 指根
    x *= 0.9 + 0.12 * zz
    if y > 0: y *= 1.0 + 0.15 * math.cos((x / 0.5) * math.pi / 2)
    v.co = U(x, y, z)


def palm_uv(ob):
    """顶视平面投影：u = x+0.5，v = z；手背（+Y）和掌心共用（掌心那面的缝线被手掌挡着看不到，干脆掌心不画：v 偏到无线区）"""
    me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
    for p in me.polygons:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            x, y, z = -co.x, co.z, -co.y
            uvl[li].uv = (x + 0.5, z * 0.5 + (0.5 if y > 0 else 0.0))


def palm_tex(path):
    """上半张：手背（三道缝线，从手腕往指根方向，渐隐）；下半张：掌心（纯色）。奶油白带一点布料噪点"""
    N = 512; y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N
    r = np.random.default_rng(3)
    col = np.tile(CREAM, (N, N, 1)) * (0.97 + 0.03 * r.random((N, N, 1))).astype(np.float32)
    vv = 1.0 - y                                      # save_rgb 把数组上下翻转写入：数组第 0 行是 UV 的 v=1
    back = vv >= 0.5                                  # UV 上半张 = 手背
    v = (vv - 0.5) * 2                                # 手背：0 手腕 … 1 指根
    for cx in (0.34, 0.5, 0.66):
        line = (np.abs(x - cx) < 0.012) & back & (v > 0.18) & (v < 0.72)
        col[line] = [0.42, 0.33, 0.45]
    save_rgb(path, col)


ob = to_obj(palm, 'Gl_Palm'); palm_uv(ob); palm_tex(os.path.join(OUT, 'Gl_Palm_albedo.png')); export(ob, os.path.join(OUT, 'Gl_Palm.fbx'))

# ---------- 袖口：喇叭形翻边 ----------
def lathe_loop(profile, seg):
    """闭合轮廓（不碰轴）绕 Z 轴车削成环形实体：首尾两圈也连起来"""
    bm = bmesh.new()
    rings = [[bm.verts.new((math.cos(i / seg * math.tau) * r, math.sin(i / seg * math.tau) * r, z)) for i in range(seg)] for r, z in profile]
    n = len(rings)
    for j in range(n):
        a_, b_ = rings[j], rings[(j + 1) % n]
        for i in range(seg): bm.faces.new((a_[i], a_[(i + 1) % seg], b_[(i + 1) % seg], b_[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


reset()
outer = [(1.06 + 0.36 * (k / 12) ** 1.6, 0.2 - 1.2 * k / 12) for k in range(13)]                  # 外壁：手腕 → 小臂张开
lip = [(1.42 + 0.12 * math.sin(a), -1.0 - 0.12 + 0.12 * math.cos(a)) for a in np.linspace(0.5, math.pi - 0.3, 6)]   # 口沿往外卷
# 实心袖口：手腕端和小臂端都收到轴上封口（第一人称往下看不会看进空筒里），小臂端是略凹的深紫色封面
cuff = lathe([(0, 0.2)] + outer + lip + [(1.3, -0.97), (0.8, -0.93), (0, -0.92)], 32)
# lathe 绕 Blender Z 轴；Unity 里袖口中心线沿 Z（手臂方向）= Blender -Y
for v in cuff.verts:
    x, y, z = v.co.x, v.co.y, v.co.z
    v.co = Vector((x, -z, y))
def cuff_uv(ob):
    """u = 绕袖口一圈的角度，v = 沿手臂方向（0 小臂口沿 … 1 手腕）"""
    me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
    for p in me.polygons:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            x, y, z = -co.x, co.z, -co.y                  # Unity 坐标；袖口中心线沿 Unity Z
            uvl[li].uv = ((math.atan2(y, x) / math.tau) % 1.0, min(1.0, max(0.0, (z + 1.15) / 1.4)))


def cuff_tex(path):
    N = 256; y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N
    v = 1.0 - y
    col = np.tile(np.array([1.0, 0.5, 0.1], np.float32), (N, N, 1))
    col *= (0.92 + 0.08 * np.cos(x * math.tau * 18) ** 2)[..., None]          # 竖向罗纹
    col[v < 0.12] = [0.36, 0.17, 0.42]                                            # 口沿卷边 + 小臂端封面：深紫
    col[(v > 0.88)] = [0.95, 0.42, 0.08]                                          # 手腕收口略深
    save_rgb(path, col.astype(np.float32))


ob = to_obj(cuff, 'Gl_Cuff'); cuff_uv(ob)
cuff_tex(os.path.join(OUT, 'Gl_Cuff_albedo.png'))
export(ob, os.path.join(OUT, 'Gl_Cuff.fbx'))

# ---------- 指节：圆头圆柱 ----------
reset()
seg_prof = [(0, -0.12)] + [(math.sin(a), -0.12 * math.cos(a)) for a in np.linspace(0.25, math.pi / 2, 4)] + \
           [(1.0, 1.0)] + [(math.cos(a), 1.0 + 0.12 * math.sin(a)) for a in np.linspace(0.25, math.pi / 2 - 0.25, 4)] + [(0, 1.12)]
seg = lathe(seg_prof, 16)
for v in seg.verts:
    x, y, z = v.co.x, v.co.y, v.co.z
    v.co = Vector((x, -z, y))                         # 轴向：Unity +Z
ob = to_obj(seg, 'Gl_Seg'); uv_smart(ob)
noise_tex(os.path.join(OUT, 'Gl_Seg_albedo.png'), tuple(CREAM), var=0.03, seed=7, cells=4)
export(ob, os.path.join(OUT, 'Gl_Seg.fbx'))

# ---------- 关节球 ----------
reset()
ball = lathe([(0, -1)] + [(math.sin(math.pi * k / 12), -math.cos(math.pi * k / 12)) for k in range(1, 12)] + [(0, 1)], 20)
ob = to_obj(ball, 'Gl_Ball'); uv_smart(ob)
noise_tex(os.path.join(OUT, 'Gl_Ball_albedo.png'), tuple(CREAM), var=0.03, seed=9, cells=4)
export(ob, os.path.join(OUT, 'Gl_Ball.fbx'))
print('GLOVE', {k: v['faces'] for k, v in report.items()})

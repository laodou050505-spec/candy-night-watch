"""花园 v7：照参考图补齐的“氛围件”（分部件程序化）：Blender -b -P Tools/blender_garden5.py -- <输出目录>
本地原点、正面朝 Unity +Z（Blender -Y）：
  Wb_Web  角落蛛网（竖直平面里的四分之一扇形：辐条 + 螺旋丝）
Unity 坐标直接摆好的：
  Lv_Leaves 散落的秋叶（数千片，按区域撒在地面/石板/小径边；贴图是四色色板，每片叶子取一种颜色）
  Gs_Tuft   墙根、路沿、柱脚的枯草丛（一簇簇细长弯叶）"""
import bpy, bmesh, math, sys, os, json, random
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from proc_lib import *
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
os.makedirs(OUT, exist_ok=True)
R = random.Random(21)


def save_rgb(path, col):
    N = col.shape[0]
    rgba = np.concatenate([np.clip(col, 0, 1), np.ones((N, N, 1), np.float32)], -1).astype(np.float32)
    im = bpy.data.images.new(os.path.basename(path), N, N, alpha=False); im.pixels = rgba[::-1].ravel()
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()


def finish(bm, name, base=None, tex=None, uv=None, **kw):
    ob = to_obj(bm, name)
    if uv: uv(ob)
    else: uv_smart(ob)
    if tex: tex(os.path.join(OUT, f'{name}_albedo.png'))
    else: noise_tex(os.path.join(OUT, f'{name}_albedo.png'), base, **kw)
    export(ob, os.path.join(OUT, f'{name}.fbx'))


# ================= 角落蛛网 =================
reset()
wb = bmesh.new()
RW = 0.75
spokes = 7
for k in range(spokes):
    a = math.pi / 2 * k / (spokes - 1)
    merge(wb, tube([Vector((0.01 * math.cos(a), 0, -0.01 * math.sin(a))), Vector((RW * math.cos(a), 0, -RW * math.sin(a)))], 0.0035, 4))
for ring in range(1, 8):
    rr = RW * ring / 8
    pts = []
    for k in range(spokes):
        a = math.pi / 2 * k / (spokes - 1)
        if k: # 两根辐条之间的丝往里垂一点
            am = (a + math.pi / 2 * (k - 1) / (spokes - 1)) / 2
            pts.append(Vector((rr * 0.9 * math.cos(am), 0.0, -rr * 0.9 * math.sin(am))))
        pts.append(Vector((rr * math.cos(a), 0.0, -rr * math.sin(a))))
    merge(wb, tube(pts, 0.0028, 4))
finish(wb, 'Wb_Web', (0.86, 0.86, 0.92), var=0.05, seed=721, cells=3)

# ================= 落叶（Unity 坐标撒点）=================
def leaf_poly(s):
    """叶片轮廓：两头尖的椭圆（长宽比约 2:1），叶尖略微偏一点；地上的小叶子 6 个点（两端叶尖 + 两侧各两点，面数省四成）"""
    pts = []
    for i in range(6):
        a = i / 6 * math.tau
        x = math.cos(a) * s; y = math.sin(a) * s * 0.48
        y *= (abs(math.cos(a)) ** 0.0) * (1 - 0.35 * abs(math.cos(a)) ** 3)      # 两端收尖
        pts.append((x, y + 0.08 * s * math.cos(a) ** 2 * (1 if math.cos(a) > 0 else 0)))
    return pts


def scatter_zone(cx, cz, rx, rz, n, avoid=None, ymin=0.012):
    out = []
    for _ in range(n):
        x = cx + R.gauss(0, rx); z = cz + R.gauss(0, rz)
        if avoid and avoid(x, z): continue
        out.append((x, z))
    return out


table_avoid = lambda x, z: math.hypot(x, z + 1.3) < 1.25         # 桌子底下不撒（被桌面挡住也看不见）
pts = []
pts += scatter_zone(0, -1.3, 2.4, 2.4, 420, table_avoid)                      # 藤架下石板
pts += scatter_zone(0, -9, 1.6, 4.5, 300)                                     # 拱门外小径两边
pts += scatter_zone(-5.2, 0, 0.6, 3.5, 150) + scatter_zone(5.2, 0, 0.6, 3.5, 150)   # 矮墙根
pts += scatter_zone(0, 4.2, 4.0, 0.5, 150)
pts += scatter_zone(-3.9, 3.0, 0.9, 0.9, 80) + scatter_zone(3.9, 3.4, 1.0, 0.8, 80)


def leaves_tex(path):
    N = 256; x = np.arange(N) / N
    cols = np.array([[1.0, 0.45, 0.08], [0.85, 0.2, 0.08], [1.0, 0.72, 0.18], [0.5, 0.3, 0.15]], np.float32)
    band = (x * 4).astype(int).clip(0, 3)
    col = np.tile(cols[band][None, :, :], (N, 1, 1)) * (0.85 + 0.15 * np.random.default_rng(2).random((N, N, 1))).astype(np.float32)
    save_rgb(path, col)


def build_leaves(P, name):
    """一批落叶合成一个网格；花园里（矮墙以内）和拱门外小径分成两个部件，远处那块能单独走 Mesh LOD"""
    reset()
    lv = bmesh.new(); bands = []
    for (x, z) in P:
        s_ = R.uniform(0.04, 0.075)
        sl = slab(leaf_poly(s_), 0.004)
        rot = Matrix.Rotation(R.uniform(0, math.tau), 4, 'Z') @ Matrix.Rotation(R.uniform(-0.25, 0.25), 4, 'X')
        y = 0.012 + R.uniform(0, 0.01)
        for v in sl.verts: v.co = rot @ v.co + U(x, y, z)
        merge(lv, sl); bands.append(R.choice([0, 0, 1, 1, 2, 3]))

    def leaves_uv(ob):
        me = ob.data; uvl = me.uv_layers.new(name='UVMap').data
        per = len(me.polygons) // len(bands)          # 每片叶子的面数固定，按顺序对应
        for li_, band in enumerate(bands):
            u = (band + 0.5) / 4.0
            for pi in range(li_ * per, (li_ + 1) * per):
                for lo in me.polygons[pi].loop_indices:
                    uvl[lo].uv = (u + R.uniform(-0.08, 0.08), R.uniform(0.2, 0.8))
    finish(lv, name, tex=leaves_tex, uv=leaves_uv)


build_leaves([p for p in pts if p[1] > -4.9], 'Lv_Leaves')
build_leaves([p for p in pts if p[1] <= -4.9], 'Lv_LeavesPath')
# ================= 枯草丛 =================
def build_tufts(spots, name):
    reset()
    tb = bmesh.new()
    for (x, z) in spots:
        base = U(x, -0.02, z)
        for k in range(R.randint(5, 9)):
            a = R.uniform(0, math.tau); h = R.uniform(0.18, 0.42); lean = R.uniform(0.15, 0.55)
            d = Vector((math.cos(a), math.sin(a), 0))
            pts_ = [base + d * 0.02, base + d * lean * h * 0.4 + Vector((0, 0, h * 0.55)), base + d * lean * h + Vector((0, 0, h * (0.85 + 0.1 * R.random())))]
            merge(tb, tube(pts_, 0.008, 4, r_fn=lambda u: 1 - 0.85 * u))
    finish(tb, name, (0.55, 0.5, 0.28), var=0.35, dark=(0.3, 0.36, 0.18), seed=731, streak=0.5)


spots = []
for sx in (-1, 1):
    for z in np.arange(-4.3, 4.6, 0.55): spots.append((sx * 5.85 + R.uniform(-0.1, 0.1), z + R.uniform(-0.2, 0.2)))        # 矮墙内侧墙根
for x in np.arange(-5.6, 5.7, 0.6): spots.append((x + R.uniform(-0.15, 0.15), 4.45 + R.uniform(-0.1, 0.05)))                  # 后墙根
for (px, pz) in ((-3.1, -4.1), (3.1, -4.1), (-3.1, -1.3), (3.1, -1.3), (-3.1, 1.5), (3.1, 1.5)):                              # 藤架柱脚
    for k in range(3): spots.append((px + R.uniform(-0.3, 0.3), pz + R.uniform(-0.3, 0.3)))
build_tufts(spots, 'Gs_Tuft')
spots = []
for sx in (-1, 1):
    for z in np.arange(-35.5, -5.5, 0.9): spots.append((sx * 1.12 + R.uniform(-0.06, 0.12) * sx, z + R.uniform(-0.3, 0.3)))   # 拱门外路沿外侧
build_tufts(spots, 'Gs_TuftPath')

json.dump(report, open(os.path.join(OUT, 'garden5_report.json'), 'w'), indent=1)
print('GARDEN5', {k: v['faces'] for k, v in report.items()})

"""天空盒远景剪影全景图（可环绕）：python3 Tools/make_sky_backdrop.py <输出.png>
横向 = 方位角一整圈（u=0.5 是 Unity +Z，u=0/1 是 -Z 正前方，u=0.25 是 -X 左，u=0.75 是 +X 右；与天空盒着色器 atan2(d.x,d.z) 一致），
纵向 = 高度 h（视线方向的 y 分量）从 H0 到 H1。
通道：R = 远层剪影（雾里的远山、远林、远处尖塔），G = 近层剪影（深色山丘、城堡、教堂、风车、村屋、枯树、十字架），
      B = 亮着的窗户（着色器里给暖色并闪烁），A 不用。
先按 2 倍分辨率画再缩小，边缘自带抗锯齿。所有形状用“弧度”定义，横纵比例一致。"""
import sys, math, random
import numpy as np
from PIL import Image, ImageDraw

OUT = sys.argv[1]
W, H = 12288, 1792                # 2 倍尺寸，最后缩到 6144×896
H0, H1 = -0.05, 0.42
PX = W / (2 * math.pi)            # 每弧度像素（横）
PY = H / (H1 - H0)                # 每单位 h 像素（纵；近地平线 h≈弧度）
R = random.Random(1031)

far = Image.new('L', (W, H), 0); near = Image.new('L', (W, H), 0); glow = Image.new('L', (W, H), 0)
dF, dN, dG = ImageDraw.Draw(far), ImageDraw.Draw(near), ImageDraw.Draw(glow)


def X(a): return a * PX                      # a：方位角（弧度，0..2π，对应 u*2π）
def Y(h): return (H1 - h) * PY
def ua(u): return u * 2 * math.pi             # u 转弧度


TF = None                                     # 地标整体放大：(方位中心, 底边高度, 倍数)，在中心底点处等比缩放


def poly(d, pts, fill=255):
    """pts：[(方位弧度, 高度)]；跨越接缝的图形在左右各画一份"""
    if TF: pts = [(TF[0] + (a - TF[0]) * TF[2], TF[1] + (h - TF[1]) * TF[2] - (0.015 if h < TF[1] + 0.006 else 0)) for a, h in pts]   # 底边再往山里压一点，放大后不悬空
    P = [(X(a), Y(h)) for a, h in pts]
    for off in (-W, 0, W):
        d.polygon([(x + off, y) for x, y in P], fill=fill)


def rect(d, a0, a1, h0, h1, fill=255): poly(d, [(a0, h0), (a1, h0), (a1, h1), (a0, h1)], fill)


def ridge(fn, d):
    """从函数 h(a) 生成整圈山脊并填到底"""
    n = 2400
    pts = [(2 * math.pi * i / n, fn(2 * math.pi * i / n)) for i in range(n + 1)]
    poly(d, pts + [(2 * math.pi, H0 - 0.02), (0, H0 - 0.02)])


# ---------- 远层：雾里的远山 + 一排尖顶针叶林 ----------
def far_h(a):
    return (0.03 + 0.022 * math.sin(2 * a + 1.0) + 0.016 * math.sin(5 * a + 2.3) + 0.008 * math.sin(11 * a + 0.4)
            + 0.05 * math.exp(-((a - ua(0.72)) / 0.25) ** 2)          # 城堡后面一座更高的远山
            + 0.035 * math.exp(-((a - ua(0.42)) / 0.2) ** 2))
ridge(far_h, dF)
a = 0.0
while a < 2 * math.pi:                        # 远林：山脊上一棵棵小尖树
    s = R.uniform(0.006, 0.014); hb = far_h(a) - 0.004
    poly(dF, [(a - s * 0.45, hb), (a, hb + s * 2.2), (a + s * 0.45, hb)])
    a += R.uniform(0.004, 0.012)
# 远处的锯齿山峰（城堡背后）
for k in range(7):
    ac = ua(0.66) + k * 0.05 + R.uniform(-0.01, 0.01); hp = far_h(ac) + R.uniform(0.03, 0.07)
    poly(dF, [(ac - 0.06, far_h(ac) - 0.01), (ac - 0.012, hp - 0.01), (ac, hp), (ac + 0.015, hp - 0.015), (ac + 0.07, far_h(ac) - 0.01)])
# 远处村落的小尖塔与亮窗
for uc in (0.08, 0.31, 0.55, 0.9):
    ac = ua(uc); hb = far_h(ac)
    rect(dF, ac - 0.004, ac + 0.004, hb - 0.01, hb + 0.03); poly(dF, [(ac - 0.006, hb + 0.03), (ac, hb + 0.055), (ac + 0.006, hb + 0.03)])
    for j in range(5):
        x = ac + R.uniform(-0.05, 0.05); hh = far_h(x) - 0.004
        rect(dF, x - 0.007, x + 0.007, hh - 0.01, hh + 0.008); poly(dF, [(x - 0.009, hh + 0.008), (x, hh + 0.016), (x + 0.009, hh + 0.008)])
        rect(dG, x - 0.0018, x + 0.0018, hh - 0.002, hh + 0.003)


# ---------- 近层：深色山丘 ----------
CAS = ua(0.70)                                 # 城堡方位（右后方，南瓜田那一侧最开阔）
CH = ua(0.86)                                  # 教堂（右前）
WM = ua(0.13)                                  # 风车（左前）
VL = ua(0.18)                                  # 村屋（左前）
WT = ua(0.40)                                  # 女巫斜塔（左后）
def near_h(a):
    def g(c, w, hgt):
        d_ = (a - c + math.pi) % (2 * math.pi) - math.pi
        return hgt * math.exp(-(d_ / w) ** 2)
    return (0.004 + 0.01 * math.sin(3 * a + 0.7) + 0.006 * math.sin(7 * a + 1.9) + 0.003 * math.sin(17 * a)
            + g(CAS, 0.16, 0.055) + g(WT, 0.08, 0.05) + g(CH, 0.12, 0.025) + g(WM, 0.07, 0.03))
ridge(near_h, dN)


def window(a, h, w=0.0035, hh=0.006, lit=True, arch=True):
    """窗：近层上挖空（画 0）后在 B 里点亮"""
    pts = [(a - w, h), (a + w, h), (a + w, h + hh)] + ([(a + w * 0.5, h + hh + w * 0.6), (a, h + hh + w * 0.8), (a - w * 0.5, h + hh + w * 0.6)] if arch else []) + [(a - w, h + hh)]
    poly(dN, pts, 0)
    if lit: poly(dG, pts, 255)


def crenel(a0, a1, h, n, d=dN, t=0.006):
    step = (a1 - a0) / (2 * n - 1)
    for i in range(n): rect(d, a0 + 2 * i * step, a0 + (2 * i + 1) * step, h - 0.001, h + t)


def tower(a, w, h0, h1, roof=0.05, d=dN, lean=0.0, flag=False):
    poly(d, [(a - w, h0), (a + w, h0), (a + w + lean, h1), (a - w + lean, h1)])
    if roof > 0:
        poly(d, [(a - w * 1.35 + lean, h1), (a + w * 1.35 + lean, h1), (a + lean * 1.4, h1 + roof)])
        rect(d, a + lean * 1.4 - 0.0007, a + lean * 1.4 + 0.0007, h1 + roof - 0.002, h1 + roof + 0.014)
        if flag: poly(d, [(a + lean * 1.4 + 0.0007, h1 + roof + 0.014), (a + lean * 1.4 + 0.018, h1 + roof + 0.009), (a + lean * 1.4 + 0.0007, h1 + roof + 0.005)])
    else:
        crenel(a - w * 1.15 + lean, a + w * 1.15 + lean, h1, 3, d)
        rect(d, a - w * 1.15 + lean, a + w * 1.15 + lean, h1 - 0.006, h1)


# ---------- 闹鬼城堡（右后山顶）----------
hb = near_h(CAS) - 0.01
poly(dN, [(CAS - 0.13, hb - 0.02), (CAS - 0.11, hb + 0.03), (CAS + 0.1, hb + 0.03), (CAS + 0.13, hb - 0.02)])   # 城墙基座/山崖
rect(dN, CAS - 0.05, CAS + 0.045, hb + 0.02, hb + 0.12)                                                          # 主楼
crenel(CAS - 0.052, CAS + 0.047, hb + 0.12, 6)
poly(dN, [(CAS - 0.03, hb + 0.12), (CAS + 0.02, hb + 0.12), (CAS - 0.005, hb + 0.165)])                          # 主楼坡顶
tower(CAS - 0.075, 0.016, hb + 0.01, hb + 0.17, 0.07, flag=True)                                                 # 左高塔
tower(CAS + 0.07, 0.019, hb + 0.01, hb + 0.21, 0.09, flag=True)                                                  # 右最高塔
tower(CAS - 0.005, 0.011, hb + 0.12, hb + 0.2, 0.06, lean=0.004)                                                 # 主楼上的细尖塔（微歪）
tower(CAS + 0.03, 0.009, hb + 0.1, hb + 0.165, 0.045)
tower(CAS - 0.115, 0.012, hb - 0.01, hb + 0.08, 0)                                                               # 城墙角楼（垛口）
tower(CAS + 0.115, 0.012, hb - 0.01, hb + 0.075, 0)
crenel(CAS - 0.11, CAS - 0.06, hb + 0.04, 4); crenel(CAS + 0.06, CAS + 0.11, hb + 0.04, 4)
rect(dN, CAS - 0.11, CAS - 0.06, hb + 0.02, hb + 0.04); rect(dN, CAS + 0.06, CAS + 0.11, hb + 0.02, hb + 0.04)
for (aa, hh) in ((CAS - 0.035, 0.05), (CAS - 0.02, 0.05), (CAS + 0.005, 0.05), (CAS + 0.025, 0.05), (CAS - 0.03, 0.085), (CAS + 0.015, 0.085),
                 (CAS - 0.075, 0.1), (CAS - 0.075, 0.135), (CAS + 0.07, 0.12), (CAS + 0.07, 0.16), (CAS - 0.004, 0.155), (CAS + 0.03, 0.13)):
    window(aa, hb + hh, lit=R.random() < 0.8)
poly(dN, [(CAS - 0.012, hb + 0.02), (CAS + 0.012, hb + 0.02), (CAS + 0.012, hb + 0.035), (CAS, hb + 0.045), (CAS - 0.012, hb + 0.035)], 0)  # 城门
poly(dG, [(CAS - 0.012, hb + 0.02), (CAS + 0.012, hb + 0.02), (CAS + 0.012, hb + 0.035), (CAS, hb + 0.045), (CAS - 0.012, hb + 0.035)], 160)

# ---------- 教堂（右前山坡）：放大 1.8 倍，尖塔要高出远景树梢 ----------
hb = near_h(CH) - 0.004
TF = (CH, hb, 1.8)
rect(dN, CH - 0.03, CH + 0.02, hb, hb + 0.028); poly(dN, [(CH - 0.032, hb + 0.028), (CH + 0.022, hb + 0.028), (CH - 0.005, hb + 0.045)])
tower(CH + 0.028, 0.008, hb, hb + 0.06, 0.055)
rect(dN, CH + 0.028 - 0.0008, CH + 0.028 + 0.0008, hb + 0.115, hb + 0.13); rect(dN, CH + 0.0235, CH + 0.0325, hb + 0.123, hb + 0.1255)   # 十字
window(CH + 0.028, hb + 0.035, 0.003, 0.008); window(CH - 0.018, hb + 0.01, 0.0025, 0.007); window(CH - 0.005, hb + 0.01, 0.0025, 0.007)
for k in range(9):                              # 教堂墓地的十字架和墓碑
    x = CH - 0.07 + k * 0.012 + R.uniform(-0.003, 0.003); h = near_h(x) - 0.002
    if k % 3 == 0:
        rect(dN, x - 0.0009, x + 0.0009, h, h + 0.016); rect(dN, x - 0.005, x + 0.005, h + 0.01, h + 0.0125)
    else:
        poly(dN, [(x - 0.003, h), (x + 0.003, h), (x + 0.003, h + 0.007), (x, h + 0.0095), (x - 0.003, h + 0.007)])

# ---------- 破风车（左前）----------
TF = None
hb = near_h(WM) - 0.003
TF = (WM, hb, 1.9)
poly(dN, [(WM - 0.011, hb), (WM + 0.011, hb), (WM + 0.006, hb + 0.055), (WM - 0.006, hb + 0.055)])
poly(dN, [(WM - 0.009, hb + 0.055), (WM + 0.009, hb + 0.055), (WM, hb + 0.068)])
hub = (WM, hb + 0.052)
for k, ang in enumerate((0.5, 2.1, 3.6, 5.2)):
    L = 0.05 if k != 2 else 0.028               # 一片扇叶断了
    c, s_ = math.cos(ang), math.sin(ang)
    poly(dN, [(hub[0] - s_ * 0.0012, hub[1] + c * 0.0012), (hub[0] + c * L - s_ * 0.0012, hub[1] + s_ * L + c * 0.0012),
              (hub[0] + c * L + s_ * 0.0012, hub[1] + s_ * L - c * 0.0012), (hub[0] + s_ * 0.0012, hub[1] - c * 0.0012)])
    poly(dN, [(hub[0] + c * L * 0.3, hub[1] + s_ * L * 0.3), (hub[0] + c * L, hub[1] + s_ * L),
              (hub[0] + c * L - s_ * 0.007, hub[1] + s_ * L + c * 0.007), (hub[0] + c * L * 0.3 - s_ * 0.007, hub[1] + s_ * L * 0.3 + c * 0.007)])
window(WM, hb + 0.02, 0.0025, 0.006)

# ---------- 歪斜村屋（左前）----------
TF = (VL + 0.05, near_h(VL + 0.05) - 0.003, 1.4)
for k in range(6):
    x = VL + k * 0.022 + R.uniform(-0.004, 0.004); h = near_h(x) - 0.003
    w = R.uniform(0.008, 0.012); hh = R.uniform(0.012, 0.02); lean = R.uniform(-0.002, 0.002)
    poly(dN, [(x - w, h), (x + w, h), (x + w + lean, h + hh), (x - w + lean, h + hh)])
    poly(dN, [(x - w * 1.25 + lean, h + hh), (x + w * 1.25 + lean, h + hh), (x + lean * 2, h + hh + w * 1.3)])
    rect(dN, x + w * 0.4, x + w * 0.7, h + hh, h + hh + w * 1.1)                                                  # 烟囱
    window(x - w * 0.4, h + hh * 0.35, 0.0022, 0.004, lit=R.random() < 0.85, arch=False)
    window(x + w * 0.35, h + hh * 0.35, 0.0022, 0.004, lit=R.random() < 0.6, arch=False)

# ---------- 女巫斜塔（左后山包上）----------
hb = near_h(WT) - 0.006
TF = (WT, hb, 1.7)
poly(dN, [(WT - 0.012, hb), (WT + 0.012, hb), (WT + 0.016, hb + 0.05), (WT + 0.02, hb + 0.085), (WT + 0.004, hb + 0.085), (WT + 0.0, hb + 0.05)])
poly(dN, [(WT - 0.0, hb + 0.085), (WT + 0.026, hb + 0.085), (WT + 0.034, hb + 0.13), (WT + 0.022, hb + 0.11)])   # 弯钩尖顶
poly(dN, [(WT + 0.0, hb + 0.03), (WT - 0.012, hb + 0.04), (WT - 0.01, hb + 0.052), (WT + 0.002, hb + 0.048)])     # 侧面小阁楼
window(WT + 0.011, hb + 0.06, 0.003, 0.008); window(WT + 0.004, hb + 0.03, 0.0025, 0.006)


# ---------- 枯树（近层山脊上散布）----------
TF = None
def branch(d, x, y, ang, L, w, depth):
    if depth == 0 or L < 0.002: return
    x2, y2 = x + math.cos(ang) * L, y + math.sin(ang) * L
    nx, ny = -math.sin(ang), math.cos(ang)
    w2 = w * 0.62
    poly(d, [(x - nx * w, y - ny * w), (x + nx * w, y + ny * w), (x2 + nx * w2, y2 + ny * w2), (x2 - nx * w2, y2 - ny * w2)])
    for _ in range(R.choice((2, 2, 3))):
        branch(d, x2, y2, ang + R.uniform(-0.75, 0.75), L * R.uniform(0.55, 0.78), w2, depth - 1)


busy = [(CAS, 0.15), (CH, 0.15), (WM, 0.06), (VL + 0.06, 0.11), (WT, 0.06)]
a = 0.0
while a < 2 * math.pi:
    a += R.uniform(0.05, 0.16)
    if any(abs((a - c + math.pi) % (2 * math.pi) - math.pi) < r for c, r in busy): continue
    s = R.uniform(0.026, 0.06)
    branch(dN, a, near_h(a) - 0.003, math.pi / 2 + R.uniform(-0.15, 0.15), s, s * 0.12, 6)

# ---------- 合成并缩小 ----------
img = Image.merge('RGB', (far, near, glow)).resize((W // 2, H // 2), Image.LANCZOS)
arr = np.array(img)
arr[..., 0] = np.maximum(arr[..., 0], arr[..., 1])          # 近层盖住的地方远层也算实（着色器按层叠合成）
Image.fromarray(arr).save(OUT)
print('SKY BACKDROP', OUT, img.size)

"""封面专用背景“月下纸剧场”（代码绘制，不用 AI 生图）：/tmp/upy/bin/python -I Tools/make_cover_backdrop.py（需要 numpy；系统 python 没装时用任意带 numpy 的环境）
10-29 用户：封面背景要更高级，不要和游戏场景一样。所以封面不再用游戏里的庄园花园，而是一座独立的“纸剧场”：
  天空（着色器程序生成：深紫渐变 + 星星 + 月亮周围的暖橙辉光）
  远景 far   ：起伏的远山 + 歪歪扭扭的小镇剪影，零星亮窗
  中景 mid   ：峭壁上的鬼堡（尖顶塔楼、垛口、亮着的拱窗），正好立在月亮下面——经典万圣节海报构图
  近景 near  ：墓园山坡（歪墓碑、十字架、铁栅栏、发光的南瓜灯、枯树）
  框景 frame ：左右两棵扭曲的大树，枝杈往中间上方伸，像剧场的幕布把画面框起来
  雾带 fog   ：夹在各层之间、缓慢流动的雾（可水平无缝平铺）
  月亮 moon  ：带环形山的满月 + 光晕
每层是一张宽图，贴在以玩家为圆心的一整圈圆柱面上（u=0/1 在正后方，正中是正前方），左右两端无缝相接。
风格跟 UI 一致：平涂、轮廓边缘手抖、迎月一侧有一道亮边（剪纸被月光照亮），不做写实渐变。
输出到 Assets/_Game/Art/Cover/。"""
import math, random
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets' / '_Game' / 'Art' / 'Cover'
SS = 2                       # 超采样
ARC = 360.0                  # 一整圈（10-29 用户：身后也要正常显示，不要黑色过渡）；左右两端无缝相接
MOON_ANG = -16.0             # 月亮方位（度，负 = 玩家左手边），和 GameplayBuilder 里一致

C = {
    'far': (78, 46, 128), 'far_hi': (150, 104, 196),
    'mid': (44, 24, 84), 'mid_hi': (255, 160, 92),
    'near': (24, 13, 46), 'near_hi': (150, 112, 255),
    'frame': (11, 6, 22), 'frame_hi': (120, 88, 230),
    'win': (255, 206, 104), 'win2': (255, 150, 60), 'pump': (255, 128, 28), 'lime': (190, 240, 80),
}


def u_of(deg):
    """方位角（度）→ 横向 0..1"""
    return (deg + ARC / 2) / ARC


def canvas(w, h):
    return Image.new('L', (w * SS, h * SS), 0)


def wob(pts, amp, seed):
    """多边形顶点手抖：沿边插点再随机偏移，轮廓不会是死板的直线"""
    rnd = random.Random(seed)
    out = []
    for i in range(len(pts)):
        x0, y0 = pts[i]; x1, y1 = pts[(i + 1) % len(pts)]
        n = max(1, int(math.hypot(x1 - x0, y1 - y0) / (14 * SS)))
        for k in range(n):
            t = k / n
            out.append((x0 + (x1 - x0) * t + rnd.uniform(-amp, amp), y0 + (y1 - y0) * t + rnd.uniform(-amp, amp)))
    return out


def ridge(w, base, amps, seed):
    """平滑起伏的山脊高度（像素，向上为正）。周期取整圈的整数分之一，首尾无缝"""
    rnd = random.Random(seed)
    xs = np.arange(w)
    y = np.full(w, float(base))
    for a, period in amps:
        ph = rnd.uniform(0, 6.28)
        cyc = max(1, round(w / period))
        y += a * np.sin(xs / w * cyc * 2 * math.pi + ph)
    return y


class WD:
    """环绕绘制：每笔同时画在 x-w、x、x+w 三处，跨过接缝的东西两端各露一半，贴到圆柱上正好接起来"""
    def __init__(self, img):
        self.d = ImageDraw.Draw(img); self.w = img.size[0]

    @staticmethod
    def _shift(xy, dx):
        if len(xy) and isinstance(xy[0], (tuple, list)):
            return [(p[0] + dx, p[1]) for p in xy]
        xy = list(xy)
        return [xy[0] + dx, xy[1], xy[2] + dx, xy[3]]

    def _each(self, name, xy, *a, **k):
        for dx in (-self.w, 0, self.w):
            getattr(self.d, name)(self._shift(xy, dx), *a, **k)

    def polygon(self, xy, **k): self._each('polygon', xy, **k)
    def line(self, xy, **k): self._each('line', xy, **k)
    def ellipse(self, xy, **k): self._each('ellipse', xy, **k)
    def rectangle(self, xy, **k): self._each('rectangle', xy, **k)
    def rounded_rectangle(self, xy, **k): self._each('rounded_rectangle', xy, **k)
    def arc(self, xy, start, end, **k): self._each('arc', xy, start, end, **k)


def fill_ridge(d, ys, H):
    pts = [(0, H)] + [(x, H - ys[x]) for x in range(0, len(ys), 3)] + [(len(ys) - 1, H - ys[-1]), (len(ys) - 1, H)]
    d.polygon(pts, fill=255)


# ---------- 剪影零件 ----------
def tower(d, win, x, ground, w, h, roof, seed, lean=0.0, windows=3):
    rnd = random.Random(seed)
    top = ground - h
    body = [(x - w / 2, ground), (x - w / 2 + lean * h * .3, top), (x + w / 2 + lean * h * .3, top), (x + w / 2, ground)]
    d.polygon(wob(body, 1.2 * SS, seed), fill=255)
    cx = x + lean * h * .3
    if roof == 'cone':
        rh = w * rnd.uniform(1.4, 2.0)
        tip = (cx + rnd.uniform(-.25, .25) * w, top - rh)
        d.polygon(wob([(cx - w * .68, top + 2), tip, (cx + w * .68, top + 2)], 1.0 * SS, seed + 1), fill=255)
        d.line([tip, (tip[0] + w * .08, tip[1] - w * .5)], fill=255, width=max(2, int(w * .05)))   # 尖顶上的小旗杆
        d.polygon([(tip[0] + w * .08, tip[1] - w * .5), (tip[0] + w * .4, tip[1] - w * .42), (tip[0] + w * .08, tip[1] - w * .32)], fill=255)
    else:   # 垛口
        n = 4; cw = w / (2 * n - 1)
        for k in range(n):
            bx = cx - w / 2 + k * 2 * cw
            d.rectangle([bx, top - cw * 1.1, bx + cw, top], fill=255)
    for k in range(windows):
        wy = top + h * (0.18 + 0.24 * k)
        if wy > ground - w * .6: break
        ww, wh = w * .22, w * .36
        wx = cx + rnd.uniform(-.15, .15) * w
        if rnd.random() < .82:
            win.rounded_rectangle([wx - ww / 2, wy, wx + ww / 2, wy + wh], radius=ww / 2, fill=255)


def hat(d, cx, top, w, rh, bend, seed):
    """巫师帽尖顶：底宽 w、高 rh，尖端往一侧弯（bend 为尖端横向偏移占高的比例），帽檐略外挑"""
    pts = [(cx - w * .62, top + 2)]
    n = 10
    for i in range(1, n):
        t = i / n
        pts.append((cx - w * .5 * (1 - t) ** 1.25 + bend * rh * t ** 2.2, top - rh * t))
    tip = (cx + bend * rh, top - rh)
    pts.append(tip)
    for i in range(n - 1, 0, -1):
        t = i / n
        pts.append((cx + w * .5 * (1 - t) ** 1.25 + bend * rh * t ** 2.2, top - rh * t))
    pts.append((cx + w * .62, top + 2))
    d.polygon(wob(pts, .5 * SS, seed), fill=255)
    return tip


def spire_tower(d, win, x, ground, w, h, rh, bend, seed, windows=2, lean=0.0):
    top = ground - h
    tx = x + lean * h
    d.polygon(wob([(x - w / 2, ground), (tx - w / 2, top), (tx + w / 2, top), (x + w / 2, ground)], .8 * SS, seed), fill=255)
    # 塔身一圈挑出的腰线
    d.rectangle([tx - w * .62, top + w * .05, tx + w * .62, top + w * .2], fill=255)
    tip = hat(d, tx, top, w * 1.25, rh, bend, seed + 3)
    d.line([tip, (tip[0], tip[1] - rh * .12)], fill=255, width=max(2, int(w * .06)))
    d.polygon([(tip[0], tip[1] - rh * .12), (tip[0] + w * .45, tip[1] - rh * .1), (tip[0], tip[1] - rh * .05)], fill=255)   # 小旗
    rnd = random.Random(seed)
    for k in range(windows):
        wy = top + w * .5 + k * w * 1.1
        if wy > ground - w * .5: break
        if rnd.random() < .85:
            win.rounded_rectangle([tx - w * .13, wy, tx + w * .13, wy + w * .38], radius=w * .13, fill=255)


def windmill(d, win, x, ground, hh, seed):
    """风车：歪一点的塔身 + 四片带格子的风叶（剪纸）"""
    rnd = random.Random(seed)
    w = hh * .32
    d.polygon(wob([(x - w / 2, ground), (x - w * .28, ground - hh), (x + w * .28, ground - hh), (x + w / 2, ground)], .8 * SS, seed), fill=255)
    d.polygon([(x - w * .38, ground - hh + 1), (x, ground - hh - w * .45), (x + w * .38, ground - hh + 1)], fill=255)
    hub = (x, ground - hh * .92)
    a0 = rnd.uniform(0, 90)
    for k in range(4):
        a = math.radians(a0 + k * 90)
        L = hh * .78
        ex, ey = hub[0] + math.cos(a) * L, hub[1] - math.sin(a) * L
        d.line([hub, (ex, ey)], fill=255, width=max(2, int(hh * .025)))
        nx, ny = -math.sin(a), -math.cos(a)
        sw = hh * .11
        p0 = (hub[0] + math.cos(a) * L * .3, hub[1] - math.sin(a) * L * .3)
        poly = [p0, (ex, ey), (ex + nx * sw, ey + ny * sw), (p0[0] + nx * sw, p0[1] + ny * sw)]
        d.line(poly + [p0], fill=255, width=max(1, int(hh * .012)))
    win.rectangle([x - w * .07, ground - hh * .45, x + w * .07, ground - hh * .33], fill=255)


def manor(d, win, cx, g, h, seed):
    """正后方山头的鬼屋：几个歪斜的尖山墙、烟囱、一扇圆窗，窗户大多亮着"""
    rnd = random.Random(seed)
    u = h * .017
    d.polygon(wob([(cx - 16 * u, g + 3 * u), (cx - 12 * u, g - u), (cx + 12 * u, g - u), (cx + 16 * u, g + 3 * u)], SS, seed), fill=255)
    for dx, bw, bh, lean in ((-8, 9, 13, -.05), (2, 11, 18, .03), (10, 7, 11, .08)):
        x0 = cx + dx * u
        top = g - bh * u
        d.polygon(wob([(x0 - bw / 2 * u, g), (x0 - bw / 2 * u + lean * bh * u, top), (x0 + bw / 2 * u + lean * bh * u, top), (x0 + bw / 2 * u, g)], .8 * SS, seed + dx), fill=255)
        tx = x0 + lean * bh * u
        d.polygon(wob([(tx - bw * .62 * u, top + 2), (tx + rnd.uniform(-1, 1) * u, top - bw * .75 * u), (tx + bw * .62 * u, top + 2)], .8 * SS, seed + dx + 1), fill=255)
        for r in range(int(bh // 5)):
            for c in (-1, 1):
                if rnd.random() < .8:
                    wx = tx + c * bw * .22 * u; wy = top + (3 + r * 4.5) * u
                    win.rectangle([wx - .9 * u, wy, wx + .9 * u, wy + 2 * u], fill=255)
    d.rectangle([cx + 4 * u, g - 23 * u, cx + 6 * u, g - 16 * u], fill=255)
    d.rectangle([cx - 11 * u, g - 17 * u, cx - 9.4 * u, g - 12 * u], fill=255)
    win.ellipse([cx + 1 * u, g - 15 * u, cx + 3.6 * u, g - 12.4 * u], fill=255)


def scarecrow(d, x, ground, hh, seed):
    """稻草人：木杆、横担、破衣、尖帽（剪影）"""
    d.line([(x, ground), (x, ground - hh)], fill=255, width=max(2, int(hh * .04)))
    d.line([(x - hh * .32, ground - hh * .72), (x + hh * .32, ground - hh * .7)], fill=255, width=max(2, int(hh * .035)))
    d.polygon(wob([(x - hh * .2, ground - hh * .75), (x + hh * .2, ground - hh * .75), (x + hh * .26, ground - hh * .4), (x + hh * .1, ground - hh * .46), (x, ground - hh * .38), (x - hh * .12, ground - hh * .45), (x - hh * .25, ground - hh * .4)], .6 * SS, seed), fill=255)
    d.ellipse([x - hh * .09, ground - hh * .93, x + hh * .09, ground - hh * .76], fill=255)
    hat(d, x, ground - hh * .9, hh * .2, hh * .22, .25, seed + 1)


def floor_mist(N=1024, seed=5):
    """脚下的雾海（俯视的圆盘）：平面方向的云团（不用极坐标，圆心不会有放射状奇点），
    外圈从半径 0.45 开始淡出，平视略往下看时不会盖住远处的墓园"""
    rnd = np.random.RandomState(seed)
    ys, xs = np.mgrid[0:N, 0:N] / (N - 1) * 2 - 1
    r = np.sqrt(xs ** 2 + ys ** 2)
    v = np.zeros((N, N))
    for k in (2, 3, 5, 8, 13):
        th = rnd.uniform(0, np.pi)
        u = xs * np.cos(th) + ys * np.sin(th); w = -xs * np.sin(th) + ys * np.cos(th)
        v += np.sin(u * k * 2.2 + rnd.uniform(0, 6.28) + np.sin(w * k * 1.3) * .9) / k ** .7
    v = (v - v.min()) / (v.max() - v.min())
    alpha = np.clip(v * 1.4 - .25, 0, 1) * np.clip((.92 - r) / .47, 0, 1) ** 1.5 * .8
    rgb = np.zeros((N, N, 3)) + np.array((150, 120, 210))
    rgb = rgb * (0.75 + 0.25 * v[..., None])
    return Image.fromarray(np.dstack([rgb, alpha * 255]).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))


def castle(d, win, cx, g, h):
    """月下鬼堡：总高约 0.52h（尖顶不出贴图上沿）。轮廓高低错落、塔尖各往不同方向弯，像手剪的剪纸"""
    u = h * .01
    # 峭壁：锯齿状岩石托着城堡
    rock = [(cx - 22 * u, g + 6 * u), (cx - 19 * u, g - 1 * u), (cx - 15 * u, g + 1 * u), (cx - 12 * u, g - 2 * u), (cx + 11 * u, g - 2 * u),
            (cx + 14 * u, g + 1 * u), (cx + 18 * u, g - 1 * u), (cx + 23 * u, g + 6 * u)]
    d.polygon(wob(rock, SS * 1.2, 31), fill=255)
    base = g - 1.5 * u
    # 主堡：宽体 + 垛口
    d.polygon(wob([(cx - 12 * u, base), (cx - 11.5 * u, base - 14 * u), (cx + 10.5 * u, base - 15 * u), (cx + 11 * u, base)], SS, 33), fill=255)
    for k in range(9):
        bx = cx - 11.5 * u + k * 2.6 * u
        d.rectangle([bx, base - 16.6 * u + (k % 2) * .3 * u, bx + 1.4 * u, base - 14 * u], fill=255)
    # 大门：拱形，透出暖光
    win.rounded_rectangle([cx - 1.8 * u, base - 5 * u, cx + 1.8 * u, base], radius=1.8 * u, fill=255)
    for i, wx in enumerate((-8, -4.5, 4, 7.5)):
        win.rounded_rectangle([cx + wx * u - .7 * u, base - 11 * u, cx + wx * u + .7 * u, base - 8.6 * u], radius=.7 * u, fill=255)
    # 主塔（最高）：在主堡正中偏左，巫师帽尖顶
    spire_tower(d, win, cx - 1.5 * u, base - 13 * u, 6 * u, 16 * u, 18 * u, .12, 41, windows=3)
    # 两侧细高塔，帽尖往外弯
    spire_tower(d, win, cx - 9.5 * u, base - 12 * u, 3.6 * u, 12 * u, 11 * u, -.22, 43, windows=2, lean=-.03)
    spire_tower(d, win, cx + 7.5 * u, base - 13 * u, 4.2 * u, 10 * u, 13 * u, .25, 45, windows=2, lean=.02)
    spire_tower(d, win, cx + 3.2 * u, base - 14 * u, 2.6 * u, 6 * u, 8 * u, -.15, 47, windows=1)
    spire_tower(d, win, cx - 6 * u, base - 14 * u, 2.4 * u, 4 * u, 7 * u, .3, 49, windows=1)
    # 右侧孤塔 + 拱桥（桥下镂空）
    tx = cx + 20 * u; tg = g - .5 * u
    spire_tower(d, win, tx, tg, 4 * u, 22 * u, 10 * u, .35, 51, windows=4, lean=.015)
    bridge_y = base - 10 * u
    d.polygon([(cx + 10.5 * u, bridge_y - 1.4 * u), (tx - 2 * u, bridge_y - 2.2 * u), (tx - 2 * u, bridge_y + 1.5 * u), (cx + 10.5 * u, bridge_y + 2.2 * u)], fill=255)
    d.ellipse([cx + 12 * u, bridge_y + .2 * u, tx - 3.5 * u, bridge_y + 6 * u], fill=0)
    # 歪烟囱
    d.polygon([(cx - 4 * u, base - 15 * u), (cx - 3.6 * u, base - 19 * u), (cx - 2.4 * u, base - 19.2 * u), (cx - 2.6 * u, base - 15 * u)], fill=255)


def tree(d, x, y, length, ang, width, depth, seed, spread=34, droop=0.0):
    """扭曲的枯树：递归分叉，枝条弯折、末端打卷"""
    rnd = random.Random(seed)
    def br(x, y, L, a, wd, dp):
        segs = 5
        px, py = x, y
        for s in range(segs):
            a += rnd.uniform(-14, 14) + droop * (1 - dp / depth) * 6
            nx = px + math.cos(math.radians(a)) * L / segs
            ny = py - math.sin(math.radians(a)) * L / segs
            w0 = wd * (1 - s / segs * .55)
            d.line([(px, py), (nx, ny)], fill=255, width=max(1, int(w0)))
            d.ellipse([nx - w0 / 2, ny - w0 / 2, nx + w0 / 2, ny + w0 / 2], fill=255)
            px, py = nx, ny
        if dp <= 0 or wd < 1.5:
            # 末端小卷
            r = max(2, L * .12)
            d.arc([px - r, py - r, px + r, py + r], start=rnd.choice([0, 180]), end=rnd.choice([200, 330]), fill=255, width=max(1, int(wd * .8)))
            return
        k = rnd.choice([2, 2, 3])
        for i in range(k):
            na = a + (i - (k - 1) / 2) * spread + rnd.uniform(-10, 10)
            br(px, py, L * rnd.uniform(.62, .78), na, wd * .62, dp - 1)
    br(x, y, length, ang, width, depth)


def trunk(d, x, ground, height, w0, w1, bend, seed):
    """弯曲渐细的树干（二次贝塞尔中心线）+ 根部外撇 + 一个树洞"""
    pts_l, pts_r = [], []
    n = 24
    for i in range(n + 1):
        t = i / n
        cx = x + bend * (2 * t * (1 - t)) * 1.6 + bend * t * t * .4
        cy = ground - height * t
        wd = (w0 * (1 - t) + w1 * t) * (1 + .5 * (1 - t) ** 6)   # 根部外撇
        pts_l.append((cx - wd / 2, cy)); pts_r.append((cx + wd / 2, cy))
    d.polygon(wob(pts_l + pts_r[::-1], SS * 1.5, seed), fill=255)
    hx = x + bend * .5; hy = ground - height * .3
    d.ellipse([hx - w0 * .12, hy - w0 * .2, hx + w0 * .12, hy + w0 * .2], fill=0)
    return (x + bend * .4, ground - height)


def grave(d, x, ground, s, kind, tilt, seed):
    rnd = random.Random(seed)
    if kind == 'round':
        pts = [(x - s * .5, ground)] + [(x + math.cos(math.radians(t)) * s * .5, ground - s * 1.1 + (1 - math.sin(math.radians(t))) * s * .0 - math.sin(math.radians(t)) * s * .5)
                                          for t in range(180, -1, -15)] + [(x + s * .5, ground)]
        pts = [(px, py) for px, py in pts]
    elif kind == 'cross':
        a = s * .16
        pts = [(x - a, ground), (x - a, ground - s * .9), (x - s * .42, ground - s * .9), (x - s * .42, ground - s * 1.15), (x - a, ground - s * 1.15), (x - a, ground - s * 1.5),
               (x + a, ground - s * 1.5), (x + a, ground - s * 1.15), (x + s * .42, ground - s * 1.15), (x + s * .42, ground - s * .9), (x + a, ground - s * .9), (x + a, ground)]
    else:   # 尖顶
        pts = [(x - s * .42, ground), (x - s * .42, ground - s * .9), (x, ground - s * 1.3), (x + s * .42, ground - s * .9), (x + s * .42, ground)]
    c, sn = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
    pts = [(x + (px - x) * c - (py - ground) * sn, ground + (px - x) * sn + (py - ground) * c) for px, py in pts]
    d.polygon(wob(pts, .8 * SS, seed), fill=255)


def fence(d, x0, x1, ground, h, seed):
    rnd = random.Random(seed)
    step = h * .32
    d.line([(x0, ground - h * .25), (x1, ground - h * .3)], fill=255, width=max(2, int(h * .05)))
    d.line([(x0, ground - h * .78), (x1, ground - h * .72)], fill=255, width=max(2, int(h * .05)))
    x = x0
    while x < x1:
        t = rnd.uniform(-6, 6)
        top = ground - h * rnd.uniform(.95, 1.08)
        dx = math.tan(math.radians(t)) * h
        d.line([(x, ground), (x + dx, top)], fill=255, width=max(2, int(h * .06)))
        d.polygon([(x + dx - h * .07, top + h * .04), (x + dx, top - h * .14), (x + dx + h * .07, top + h * .04)], fill=255)
        x += step


def pumpkin(d, glow, x, ground, s, seed):
    rnd = random.Random(seed)
    for k, dx in enumerate((-.3, .3, 0)):
        w = s * (.62 if k < 2 else .7)
        d.ellipse([x + dx * s - w / 2, ground - s * .78, x + dx * s + w / 2, ground], fill=255)
    d.rectangle([x - s * .05, ground - s * .95, x + s * .05, ground - s * .72], fill=255)
    # 刻出来的脸（发光）
    ey = ground - s * .5
    for sx in (-1, 1):
        ex = x + sx * s * .2
        glow.polygon([(ex - s * .1, ey + s * .06), (ex, ey - s * .1), (ex + s * .1, ey + s * .06)], fill=255)
    my = ground - s * .26
    pts = [(x - s * .32, my - s * .04)]
    n = 5
    for i in range(n + 1):
        px = x - s * .32 + i * s * .64 / n
        pts.append((px, my + (s * .08 if i % 2 else -s * .02)))
    pts += [(x + s * .32, my - s * .04), (x + s * .2, my + s * .14), (x - s * .2, my + s * .14)]
    glow.polygon(pts, fill=255)


def house(d, win, x, ground, w, h, seed):
    rnd = random.Random(seed)
    lean = rnd.uniform(-.12, .12)
    body = [(x - w / 2, ground), (x - w / 2 + lean * h, ground - h), (x + w / 2 + lean * h, ground - h), (x + w / 2, ground)]
    d.polygon(wob(body, .8 * SS, seed), fill=255)
    rx = x + lean * h
    d.polygon(wob([(rx - w * .62, ground - h + 1), (rx + rnd.uniform(-.2, .2) * w, ground - h - w * rnd.uniform(.6, .95)), (rx + w * .62, ground - h + 1)], .8 * SS, seed + 2), fill=255)
    d.rectangle([rx + w * .18, ground - h - w * .7, rx + w * .3, ground - h - w * .25], fill=255)   # 烟囱
    for i in range(rnd.choice([1, 2, 2, 3])):
        if rnd.random() < .7:
            wx = x + rnd.uniform(-.28, .28) * w; wy = ground - h * rnd.uniform(.35, .8)
            win.rectangle([wx - w * .08, wy - w * .1, wx + w * .08, wy + w * .1], fill=255)


# ---------- 合成：剪影 + 迎月亮边 + 发光窗 ----------
def compose(mask, glow, base, hi, w, h, glow_cols=None, rim_px=3, rim_side=None, haze=0.0, out=None, fade_bottom=0.0):
    m = mask.resize((w, h), Image.LANCZOS)
    a = np.asarray(m).astype(np.float32) / 255
    rgb = np.zeros((h, w, 3), np.float32) + np.array(base, np.float32)
    # 迎月亮边：剪影上沿 + 朝月亮那一侧的边，亮一道（剪纸边缘被月光照亮）
    inner = np.asarray(m.filter(ImageFilter.MinFilter(rim_px * 2 + 1))).astype(np.float32) / 255
    edge = np.clip(a - inner, 0, 1)
    up = np.asarray(m.transform(m.size, Image.AFFINE, (1, 0, 0, 0, 1, rim_px * 2))).astype(np.float32) / 255   # 往下挪：上沿 = 本身有、上方没有
    top = np.clip(a - up, 0, 1)
    if rim_side is not None:
        xs = np.arange(w)[None, :] / w
        face = np.clip(1 - np.abs(xs - rim_side) * 2.2, 0.25, 1)   # 越靠近月亮那一侧越亮
    else:
        face = 1
    rim = np.clip(np.maximum(edge * .55, top) * face, 0, 1)[..., None]
    rgb = rgb * (1 - rim) + np.array(hi, np.float32) * rim
    if haze > 0:   # 远层底部融进雾里：越往下越淡、偏亮紫
        ys = (np.arange(h)[:, None] / h)
        k = np.clip((ys - .55) / .45, 0, 1)[..., None] * haze
        rgb = rgb * (1 - k) + np.array((120, 84, 170), np.float32) * k
    if glow is not None:
        g = np.asarray(glow.resize((w, h), Image.LANCZOS)).astype(np.float32)[..., None] / 255
        gc = np.array(glow_cols[0], np.float32)
        rgb = rgb * (1 - g) + gc * g
        a = np.maximum(a, g[..., 0])
    if fade_bottom > 0:   # 底边渐隐：交给下面的雾和更近的层去接
        ys = np.arange(h)[:, None] / h
        a = a * np.clip((1 - ys) / fade_bottom, 0, 1) ** 1.5
    img = np.dstack([np.clip(rgb, 0, 255), a * 255]).astype(np.uint8)
    im = Image.fromarray(img)
    if out: im.save(out)
    return im


def layer_far(W=7168, H=768):
    m = canvas(W, H); win = canvas(W, H)
    d = WD(m); dw = WD(win)
    w, h = W * SS, H * SS
    ys = ridge(w, h * .36, [(h * .07, w * .23), (h * .035, w * .07), (h * .012, w * .019)], 11)
    fill_ridge(d, ys.astype(int), h)
    rnd = random.Random(5)
    # 小镇：两三簇歪房子
    for cdeg in (-62, 34, 70, 118, 152, -118, -160):
        cx = u_of(cdeg) * w
        for i in range(rnd.randint(5, 8)):
            x = cx + rnd.uniform(-1, 1) * w * .035
            g = h - ys[int(min(w - 1, max(0, x)))] + h * .01
            house(d, dw, x, g, h * rnd.uniform(.05, .08), h * rnd.uniform(.05, .1), rnd.randint(0, 999))
    # 远处一座钟楼
    x = u_of(48) * w; g = h - ys[int(x)] + h * .01
    tower(d, dw, x, g, h * .05, h * .2, 'cone', 3, windows=2)
    # 零星枯树
    for deg in (-140, 172):   # 身后山上的风车
        x = u_of(deg) * w; windmill(d, dw, x, h - ys[int(x)] + h * .01, h * .2, deg + 7)
    for i in range(44):
        x = rnd.uniform(0, w); g = h - ys[int(x)] + 2
        tree(d, x, g, h * rnd.uniform(.05, .1), 90 + rnd.uniform(-8, 8), h * .008, 3, rnd.randint(0, 999), spread=40)
    return compose(m, win, C['far'], C['far_hi'], W, H, [C['win']], rim_px=2, rim_side=u_of(MOON_ANG), haze=.55, fade_bottom=.3)


def layer_mid(W=7168, H=1280):
    m = canvas(W, H); win = canvas(W, H)
    d = WD(m); dw = WD(win)
    w, h = W * SS, H * SS
    ys = ridge(w, h * .2, [(h * .05, w * .3), (h * .02, w * .06)], 23)
    # 鬼堡所在的峭壁：在月亮正下方隆起
    cx = u_of(MOON_ANG) * w
    xs = np.arange(w)
    ys = ys + h * .2 * np.exp(-((xs - cx) / (w * .055)) ** 4)
    fill_ridge(d, ys.astype(int), h)
    g = h - ys[int(cx)] + h * .01
    castle(d, dw, cx, g, h)
    # 通往城堡的弯曲小路上一串灯（细点）
    rnd = random.Random(4)
    for i in range(14):
        t = i / 13
        px = cx + h * (.45 - t * .35) * math.cos(t * 3) ; py = h - ys[int(min(w - 1, px))] + h * .006
        dw.ellipse([px - SS * 2.5, py - SS * 2.5, px + SS * 2.5, py + SS * 2.5], fill=255)
    # 正后方山头上一座鬼屋（另一个视觉中心，和前面的鬼堡遥遥相对）
    mx = u_of(180) * w % w
    manor(d, dw, mx, h - ys[int(mx) % w] + h * .01, h, 61)
    # 两侧山上的枯树
    for i in range(34):
        x = rnd.uniform(0, w)
        if abs(x - cx) < h * .3 or min(abs(x - mx), w - abs(x - mx)) < h * .3: continue
        gg = h - ys[int(x)] + 2
        tree(d, x, gg, h * rnd.uniform(.06, .12), 90 + rnd.uniform(-10, 10), h * .011, 3, rnd.randint(0, 999))
    return compose(m, win, C['mid'], C['mid_hi'], W, H, [C['win']], rim_px=3, rim_side=u_of(MOON_ANG), haze=.35, fade_bottom=.25)


def layer_near(W=7168, H=1024):
    m = canvas(W, H); glow = canvas(W, H)
    d = WD(m); dg = WD(glow)
    w, h = W * SS, H * SS
    ys = ridge(w, h * .3, [(h * .08, w * .26), (h * .03, w * .09), (h * .01, w * .02)], 41)
    # 正前方偏右（标题字下面）压低一点，不挡字，也把视线引向鬼堡
    xs = np.arange(w)
    ys = ys - h * .12 * np.exp(-((xs - u_of(14) * w) / (w * .09)) ** 2)
    fill_ridge(d, ys.astype(int), h)
    rnd = random.Random(8)
    def gy(x): return h - ys[int(min(w - 1, max(0, x)))] + h * .012
    # 墓碑群
    for i in range(84):
        x = rnd.uniform(0, w)
        grave(d, x, gy(x), h * rnd.uniform(.035, .07), rnd.choice(['round', 'round', 'cross', 'spire']), rnd.uniform(-14, 14), rnd.randint(0, 999))
    # 铁栅栏两段
    for a0, a1 in ((-88, -58), (40, 72), (122, 150), (-172, -140)):
        x0, x1 = u_of(a0) * w, u_of(a1) * w
        fence(d, x0, x1, gy((x0 + x1) / 2), h * .07, a0 + 200)
    # 发光南瓜灯
    for deg in (-80, -44, -30, 28, 52, 86, 108, 136, 163, -108, -128, -158):
        x = u_of(deg) * w
        pumpkin(d, dg, x, gy(x), h * rnd.uniform(.05, .075), int(deg) + 300)
    # 两棵枯树
    for deg in (-50, 60, 128, -135, 176):
        x = u_of(deg) * w
        tree(d, x, gy(x), h * .25, 90 + rnd.uniform(-6, 6), h * .028, 4, int(deg) + 500)
    for deg in (145, -150):   # 南瓜田里的稻草人
        x = u_of(deg) * w; scarecrow(d, x, gy(x), h * .3, int(deg) + 700)
    return compose(m, glow, C['near'], C['near_hi'], W, H, [C['pump']], rim_px=3, rim_side=u_of(MOON_ANG))


def layer_frame(W=7168, H=1536):
    m = canvas(W, H)
    d = WD(m)
    w, h = W * SS, H * SS
    # 左右两棵大树：树干从底部斜向上，枝杈往画面中间上方伸（像剧场两侧的幕布）
    for side, deg in ((-1, -78), (1, 82), (1, -150), (-1, 148)):
        x = u_of(deg) * w
        tx, ty = trunk(d, x, h, h * .52, h * .11, h * .035, -side * h * .06, deg)
        tree(d, tx, ty + h * .01, h * .45, 90 - side * 32, h * .035, 5, deg + 900, spread=30, droop=-side * 1.0)
        tree(d, tx + side * h * .005, ty + h * .12, h * .3, 90 + side * 40, h * .03, 4, deg + 901, spread=36)
    # 底部一道深色土坡把两棵树连起来（玩家脚下不是空的）
    ys = ridge(w, h * .07, [(h * .025, w * .2), (h * .01, w * .05)], 61)
    fill_ridge(d, ys.astype(int), h)
    return compose(m, None, C['frame'], C['frame_hi'], W, H, rim_px=4, rim_side=u_of(MOON_ANG))


def fog(W=1024, H=256, seed=3):
    """可水平无缝平铺的雾带：一团一团的云（上沿起伏、中间有疏有密），不是一整片布"""
    rnd = np.random.RandomState(seed)
    X = np.arange(W)[None, :] / W * 2 * np.pi
    Y = np.arange(H)[:, None] / H
    v = np.zeros((H, W), np.float32)
    for k in (2, 3, 5, 7, 11, 17):
        v += (1 / k ** .8) * np.sin(X * k + rnd.uniform(0, 6.28) + np.sin(X * rnd.randint(1, 4) + Y * 6) * .8) * np.cos(Y * rnd.uniform(4, 12) + rnd.uniform(0, 6.28))
    v = (v - v.min()) / (v.max() - v.min())
    # 上沿：随横向起伏的云顶
    crest = .3 + .18 * np.sin(X * 3 + 1.2) + .1 * np.sin(X * 7 + .4) + .05 * np.sin(X * 13)
    top = np.clip((Y - crest) / .25, 0, 1)
    bottom = np.clip((1 - Y) / .2, 0, 1)
    a = np.clip(v * 1.6 - .45, 0, 1) * top * bottom
    a = a ** 1.2
    rgb = np.zeros((H, W, 3)) + np.array((186, 160, 236))
    img = np.dstack([rgb, a * 255]).astype(np.uint8)
    return Image.fromarray(img).filter(ImageFilter.GaussianBlur(3))


def _fbm(N, octaves, seed, base=4):
    """可平铺的分形噪声（0..1），用正弦叠加，够柔和"""
    rnd = np.random.RandomState(seed)
    ys, xs = np.mgrid[0:N, 0:N] / N * 2 * np.pi
    v = np.zeros((N, N))
    amp = 1.0
    for o in range(octaves):
        f = base * 2 ** o
        for _ in range(3):
            ax, ay = rnd.randint(-f, f + 1), rnd.randint(-f, f + 1)
            v += amp * np.sin(xs * ax + ys * ay + rnd.uniform(0, 6.28)) / 3
        amp *= 0.55
    return (v - v.min()) / (v.max() - v.min())


def moon(N=1024):
    """10-29 用户：封面月光看着假。月面改成：边缘略暗（球面感）、受光一侧更白、暗面偏暖金；
    不规则的月海（低频噪声软阈值，不是圆斑）+ 少量小环形山（一侧亮边一侧暗边）；边缘一道很细的亮边，抗锯齿收边。"""
    ys, xs = (np.mgrid[0:N, 0:N] + 0.5) / N * 2 - 1
    r = np.sqrt(xs ** 2 + ys ** 2)
    R = 0.94
    z = np.sqrt(np.clip(1 - (r / R) ** 2, 0, 1))
    limb = 0.80 + 0.20 * z ** 0.6
    light = np.clip(0.5 + 0.5 * (xs * -0.35 + ys * -0.45) / R, 0, 1)                # 左上受光
    base = np.array((255, 244, 214), float)
    warm = np.array((236, 206, 160), float)
    col = base[None, None] * (0.86 + 0.14 * light[..., None]) * limb[..., None]
    n = _fbm(N, 4, 7, base=2)
    maria = np.clip((n - 0.52) / 0.18, 0, 1) ** 1.3 * 0.55
    col = col * (1 - maria[..., None] * 0.28) + warm[None, None] * maria[..., None] * 0.18
    # 小环形山
    rnd = random.Random(21)
    placed = []
    for i in range(60):
        if len(placed) >= 9: break
        a = rnd.uniform(0, 6.28); dd = math.sqrt(rnd.uniform(0.02, 0.62)) * R; rr = rnd.uniform(0.025, 0.07)
        cx, cy = math.cos(a) * dd, math.sin(a) * dd
        if any(math.hypot(cx - px, cy - py) < 0.22 for px, py in placed): continue
        placed.append((cx, cy))
        d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2) / rr
        bowl = np.clip(1 - d, 0, 1) ** 0.7
        rim = np.exp(-((d - 1.0) / 0.18) ** 2)
        side = ((xs - cx) * -0.6 + (ys - cy) * -0.8) / rr                               # 迎光一侧
        col = col * (1 - (bowl * 0.10 + rim * np.clip(-side, 0, 1) * 0.08)[..., None]) + 255 * (rim * np.clip(side, 0, 1) * 0.07)[..., None]
    alpha = np.clip((R - r) * N * 0.5, 0, 1)                                            # 抗锯齿边
    rimglow = np.exp(-((r - R) / 0.012) ** 2) * 0.35
    col = col + 255 * rimglow[..., None]
    img = np.dstack([np.clip(col, 0, 255), np.clip(alpha + rimglow * 0.6, 0, 1) * 255]).astype(np.uint8)
    return Image.fromarray(img)


def glow(N=512):
    """月晕：中心暖白、往外渐变成淡橙再到淡紫，一路平滑衰减到卡片边缘正好为 0（原来在边上硬切，看得见一个大圆盘）"""
    ys, xs = (np.mgrid[0:N, 0:N] + 0.5) / N * 2 - 1
    rr = np.sqrt(xs ** 2 + ys ** 2)
    a = np.exp(-(rr / 0.16) ** 2) * 0.75 + np.exp(-(rr / 0.38) ** 2) * 0.32 + np.exp(-(rr / 0.75) ** 2) * 0.12
    a *= np.clip((1 - rr) / 0.35, 0, 1) ** 2
    c0 = np.array((255, 240, 214), float); c1 = np.array((255, 180, 120), float); c2 = np.array((190, 140, 220), float)
    t = np.clip(rr / 0.6, 0, 1)[..., None]
    rgb = c0 * (1 - t) ** 2 + c1 * 2 * t * (1 - t) + c2 * t ** 2
    return Image.fromarray(np.dstack([rgb, np.clip(a, 0, 1) * 255]).astype(np.uint8))


def moon_clouds(W=2048, H=512, seed=4):
    """从月亮前飘过的薄云带（可水平平铺）：几缕被拉长的云，背光面深紫半透明，朝月亮的上沿镶一道银白亮边。"""
    rnd = np.random.RandomState(seed)
    ys, xs = np.mgrid[0:H, 0:W]
    X = xs / W * 2 * np.pi; Y = ys / H
    dens = np.zeros((H, W))
    for k, (yc, th, amp) in enumerate(((0.36, 0.13, 1.0), (0.6, 0.1, 0.85), (0.8, 0.07, 0.6))):
        wob = 0.05 * np.sin(X * 2 + rnd.uniform(0, 6)) + 0.03 * np.sin(X * 5 + rnd.uniform(0, 6))
        band = np.exp(-((Y - yc - wob) / th) ** 2)
        brk = 0.5 + 0.5 * np.sin(X * rnd.randint(2, 4) + rnd.uniform(0, 6)) * np.sin(X * rnd.randint(5, 8) + rnd.uniform(0, 6))
        fine = 0.82 + 0.18 * np.sin(X * 9 + Y * 14 + rnd.uniform(0, 6)) * np.sin(X * 5 - Y * 7)
        dens += amp * band * np.clip(brk * 1.2 + 0.25, 0, 1) * fine
    dens = np.clip(dens, 0, 1)
    up = np.roll(dens, 6, axis=0); up[:6] = 0                                            # 往下挪 6 像素：上沿 = 本身有、上方没有
    lining = np.clip(dens - up, 0, 1)
    dark = np.array((70, 48, 110), float); silver = np.array((255, 246, 228), float)
    k = np.clip(lining * 3, 0, 1)[..., None]
    rgb = dark * (1 - k) + silver * k
    alpha = np.clip(dens * 0.8 + lining * 0.5, 0, 1)
    img = Image.fromarray(np.dstack([rgb, alpha * 255]).astype(np.uint8))
    return img.filter(ImageFilter.GaussianBlur(2.2))


def crop_top(im, name, meta):
    """裁掉上方全透明的部分（头显上每层都是大面积半透明，空白也要画，白白多耗 GPU）。
    meta[name] = 保留部分占原高的比例，CoverBackdrop 按它把弧面高度同比缩短，画面位置不变。"""
    a = np.asarray(im)[..., 3]
    rows = np.where(a.max(axis=1) > 2)[0]
    top = max(0, int(rows[0]) - 8) if len(rows) else 0
    meta[name] = round(1 - top / im.height, 4)
    return im.crop((0, top, im.width, im.height))


def main():
    import json
    OUT.mkdir(parents=True, exist_ok=True)
    meta = {}
    for name, fn in (('Cover_Far', layer_far), ('Cover_Mid', layer_mid), ('Cover_Near', layer_near), ('Cover_Frame', layer_frame)):
        crop_top(fn(), name, meta).save(OUT / f'{name}.png')
    (OUT / 'cover_layers.json').write_text(json.dumps(meta, indent=1))
    fog(seed=3).save(OUT / 'Cover_Fog.png')
    floor_mist().save(OUT / 'Cover_Floor.png')
    moon().save(OUT / 'Cover_Moon.png')
    glow().save(OUT / 'Cover_MoonGlow.png')
    moon_clouds().save(OUT / 'Cover_MoonClouds.png')
    print('ok ->', OUT)


if __name__ == '__main__':
    main()

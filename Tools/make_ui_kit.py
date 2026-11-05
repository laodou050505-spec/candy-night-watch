"""整套界面美术（代码绘制，不用 AI 生图）：python3 -I Tools/make_ui_kit.py
风格：手工剪纸贴纸 + 万圣节海报的孔版印刷味（10-29 用户给的参考：夸张手写字、孔版颗粒、套色错位、爆炸星、
     高饱和南瓜橙 / 深紫 / 荧光黄绿）——平涂两色、手抖的墨色描边、硬边投影、细密网点颗粒、一侧轻微错版的撞色。
不用渐变、果冻高光、闪光星点；文字一律用真字体排版（10-28 用户要求：整体不要有明显的 AI 味，尤其是 UI）。
手势图标用游戏里真实的手套模型渲染（Unity 里按 HandPoses 摆的姿势，见 ArtSource/UI/gestures/raw_*.png），
保证图上的手势和游戏实际判定的动作一模一样，再在这里统一描边、加动作箭头。
输出到 Assets/_Game/Art/UI/（文件名与旧版相同，布局尺寸不变，代码不用改）。"""
import json, math, random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / 'Assets' / '_Game' / 'Art' / 'UI'
GEST = ROOT / 'ArtSource' / 'UI' / 'gestures'
FONT_LOGO = ROOT / 'ArtSource' / 'Fonts' / 'ZCOOLKuaiLe_logo.ttf'   # 青刻黄油体的“万”像“厉”，换成好认的快乐体
FONT_ZH = ROOT / 'Assets' / '_Game' / 'Art' / 'Fonts' / 'ZCOOLKuaiLe-Subset.ttf'
FONT_ZH_EXTRA = ROOT / 'ArtSource' / 'Fonts' / 'ZCOOLKuaiLe_logo.ttf'
FONT_CREEP = ROOT / 'Assets' / '_Game' / 'Art' / 'Fonts' / 'Creepster-Regular.ttf'
FONT_BALOO = ROOT / 'Assets' / '_Game' / 'Art' / 'Fonts' / 'Baloo2-SemiBold.ttf'

INK = (30, 16, 40)
P = {
    'pumpkin': ((250, 116, 36), (214, 74, 22)),
    'cream': ((248, 234, 204), (224, 200, 158)),
    'violet': ((132, 86, 204), (94, 56, 156)),
    'night': ((48, 30, 78), (34, 20, 58)),
    'mint': ((92, 214, 182), (52, 162, 138)),
    'lime': ((194, 232, 70), (146, 188, 36)),
    'gold': ((252, 200, 60), (226, 150, 30)),
    'pink': ((246, 112, 160), (206, 72, 124)),
    'leaf': ((110, 184, 72), (70, 132, 50)),
    'stem': ((122, 88, 52), (90, 62, 36)),
}
# 错版撞色：每个底色配一个在背后偏移出来的颜色（孔版两次套印没对齐的效果）
MIS = {'pumpkin': 'violet', 'cream': 'pumpkin', 'violet': 'lime', 'night': 'pumpkin', 'mint': 'violet', 'lime': 'violet',
       'gold': 'pink', 'pink': 'gold', 'leaf': 'gold', 'stem': 'pumpkin'}
SS = 4          # 超采样倍数：先画大再缩小，边缘干净


# ---------------- 基础工具 ----------------
def L(w, h):
    return Image.new('L', (w, h), 0)


def dilate(m, r):
    if r <= 0: return m
    b = m.filter(ImageFilter.GaussianBlur(r / 2.0))
    return b.point(lambda v: 255 if v > 12 else 0)


def erode(m, r):
    return ImageChops.invert(dilate(ImageChops.invert(m), r))


def rounded(m, r):
    """把多边形的尖角磨圆（先腐蚀再膨胀）"""
    return dilate(erode(m, r), r)


def jitter(m, amp, cell=None, seed=7):
    """手抖：用平滑的随机位移场扭一下 mask（边缘不再是机器画的完美曲线）"""
    import numpy as np
    a = np.asarray(m); h, w = a.shape
    cell = cell or max(8, int(amp * 9))
    rng = np.random.default_rng(seed)
    gh, gw = h // cell + 3, w // cell + 3
    def field():
        g = Image.fromarray((rng.normal(0, 1, (gh, gw)) * 40 + 128).clip(0, 255).astype('uint8'))
        return (np.asarray(g.resize((w, h), Image.BICUBIC)).astype('float32') - 128) / 40
    dx, dy = field() * amp, field() * amp
    yy, xx = np.mgrid[0:h, 0:w]
    sy = np.clip((yy + dy).astype(int), 0, h - 1); sx = np.clip((xx + dx).astype(int), 0, w - 1)
    return Image.fromarray(a[sy, sx])


RISO_K = 1.0     # 颗粒密度系数（图标缩得很小，颗粒会变噪点，画图标时调低）


def riso(img, mask, color, density=0.06, seed=3, light=False):
    """孔版网点颗粒：在 mask 内撒细密的深色（或浅色）小点"""
    import numpy as np
    density *= RISO_K
    w, h = img.size
    rng = np.random.default_rng(seed)
    small = (rng.random((h // 3, w // 3)) < density).astype('uint8') * 255
    dots = Image.fromarray(small).resize((w, h), Image.NEAREST).filter(ImageFilter.GaussianBlur(0.6))
    dots = ImageChops.multiply(dots, mask)
    c = tuple(min(255, int(v * 1.12) + 18) for v in color) if light else tuple(int(v * 0.72) for v in color)
    img.paste(Image.new('RGBA', (w, h), c + (150,)), (0, 0), dots)


def shift(m, dx, dy):
    out = L(*m.size); out.paste(m, (int(dx), int(dy))); return out


def grain(img, mask, amount=6, seed=1):
    """极淡的纸纹：只在 mask 内加一点低频明暗"""
    w, h = img.size
    rnd = random.Random(seed)
    small = Image.new('L', (max(2, w // 10), max(2, h // 10)))
    small.putdata([128 + rnd.randint(-amount, amount) for _ in range(small.width * small.height)])
    n = small.filter(ImageFilter.GaussianBlur(1.2)).resize((w, h), Image.BICUBIC)
    base = img.convert('RGB')
    lit = Image.merge('RGB', [ImageChops.add(c, n, 1.0, -128) for c in base.split()])
    img.paste(lit, (0, 0), mask)


def sticker(canvas, mask, color, outline=None, shadow=True, shade=True, shade_off=None, grain_amt=3, seed=1, wob=1.0, mis=True):
    """贴纸：硬边投影 → 墨色描边 → 平涂底色 → 右下暗部 → 纸纹"""
    w, h = canvas.size
    ow = outline if outline is not None else max(6, int(min(w, h) * 0.035))
    base, dark = P[color] if isinstance(color, str) else color
    if wob: mask = jitter(mask, max(1.5, min(w, h) * 0.004 * wob), seed=seed)
    rim = dilate(mask, ow)
    if shadow:
        sh = shift(rim, 0, ow * 0.7)
        canvas.paste(Image.new('RGBA', (w, h), INK + (110,)), (0, 0), sh)
    canvas.paste(Image.new('RGBA', (w, h), INK + (255,)), (0, 0), rim)
    if mis and isinstance(color, str) and color in MIS:
        # 错版：撞色那一版往左上错开一点，露在描边里侧
        mc = P[MIS[color]][0]
        canvas.paste(Image.new('RGBA', (w, h), mc + (255,)), (0, 0), ImageChops.multiply(shift(mask, -ow * .45, -ow * .35), rim))
    canvas.paste(Image.new('RGBA', (w, h), base + (255,)), (0, 0), mask)
    if shade:
        so = shade_off or (ow * 1.1, ow * 1.3)
        cres = ImageChops.subtract(mask, shift(mask, -so[0], -so[1]))
        canvas.paste(Image.new('RGBA', (w, h), dark + (255,)), (0, 0), cres)
    if grain_amt:
        grain(canvas, mask, grain_amt, seed)
        riso(canvas, mask, base, 0.05, seed)
        riso(canvas, mask, base, 0.025, seed + 9, light=True)
    return rim


def wobble(points, amp, seed=3, step=None):
    """手剪的边：沿多边形每条边细分，按低频噪声往法向抖一点"""
    rnd = random.Random(seed)
    out = []
    n = len(points)
    phase = [rnd.random() * 6.28 for _ in range(4)]
    total = 0
    for i in range(n):
        a, b = points[i], points[(i + 1) % n]
        seg = math.dist(a, b)
        k = max(2, int(seg / (step or amp * 6)))
        nx, ny = (b[1] - a[1]) / (seg + 1e-6), -(b[0] - a[0]) / (seg + 1e-6)
        for j in range(k):
            t = j / k
            s = total + seg * t
            o = amp * (0.6 * math.sin(s / (amp * 9) + phase[0]) + 0.3 * math.sin(s / (amp * 3.7) + phase[1]) + 0.1 * math.sin(s / (amp * 1.3) + phase[2]))
            out.append((a[0] + (b[0] - a[0]) * t + nx * o, a[1] + (b[1] - a[1]) * t + ny * o))
        total += seg
    return out


def rrect_pts(x0, y0, x1, y1, r, seg=10):
    pts = []
    for cx, cy, a0 in ((x1 - r, y0 + r, -90), (x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def poly_mask(size, pts):
    m = L(*size); ImageDraw.Draw(m).polygon(pts, fill=255); return m


def stroke_mask(size, pts, width):
    m = L(*size); d = ImageDraw.Draw(m)
    d.line(pts, fill=255, width=int(width), joint='curve')
    r = width / 2
    for p in (pts[0], pts[-1]): d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=255)
    return m


def arc_pts(cx, cy, r, a0, a1, n=48):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def arrow_mask(size, pts, width, head=2.4):
    """沿折线/曲线的粗箭头：线身一直连到三角箭头底部（尖角磨圆）"""
    tip = pts[-1]
    hl = width * head; hw = hl * 0.85
    # 箭头方向：取离箭尖 hl 远处的点 → 箭尖
    back = pts[0]
    for p in reversed(pts[:-1]):
        if math.dist(p, tip) >= hl: back = p; break
    ang = math.atan2(tip[1] - back[1], tip[0] - back[0])
    base = (tip[0] - math.cos(ang) * hl, tip[1] - math.sin(ang) * hl)
    body = [p for p in pts[:-1] if math.dist(p, tip) > hl] + [base]
    m = stroke_mask(size, body if len(body) > 1 else [pts[0], base], width)
    tri = [tip, (base[0] + math.cos(ang + 1.5708) * hw, base[1] + math.sin(ang + 1.5708) * hw), (base[0] + math.cos(ang - 1.5708) * hw, base[1] + math.sin(ang - 1.5708) * hw)]
    ImageDraw.Draw(m).polygon(tri, fill=255)
    return rounded(m, width * 0.12)


def burst(c, cx, cy, R, color, n=10, inner=.5, rot=0, seed=1):
    """爆炸星（海报上的那种尖角星）"""
    rnd = random.Random(seed)
    pts = []
    for i in range(n * 2):
        r = R * (1 if i % 2 == 0 else inner) * (1 + .12 * (rnd.random() - .5))
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    sticker(c, poly_mask(c.size, pts), color, outline=int(R * .14), shadow=False, shade=False, grain_amt=0, wob=0, mis=False)


def star_pts(cx, cy, R, r, n=5, rot=-90):
    return [(cx + (R if i % 2 == 0 else r) * math.cos(math.radians(rot + i * 180 / n)), cy + (R if i % 2 == 0 else r) * math.sin(math.radians(rot + i * 180 / n))) for i in range(n * 2)]


def text_mask(size, text, font_path, px, center, spacing=0):
    m = L(*size); d = ImageDraw.Draw(m)
    f = ImageFont.truetype(str(font_path), int(px))
    bb = d.textbbox((0, 0), text, font=f)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    d.text((center[0] - w / 2 - bb[0], center[1] - h / 2 - bb[1]), text, font=f, fill=255)
    return m


def finish(canvas, out, size):
    img = canvas.resize(size, Image.LANCZOS)
    img.save(out)
    print('  ', out.name, size)


# ---------------- 图标（256） ----------------
def icon(name, draw):
    global RISO_K
    RISO_K = 0.3
    N = 256 * SS
    c = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    draw(c, N)
    finish(c, UI / f'Icon_{name}.png', (256, 256))
    RISO_K = 1.0


def i_play(c, N):
    m = poly_mask(c.size, [(N * .3, N * .2), (N * .82, N * .5), (N * .3, N * .8)])
    sticker(c, rounded(m, N * .06), 'pumpkin')


def i_pause(c, N):
    m = L(N, N); d = ImageDraw.Draw(m)
    for x in (.26, .58):
        d.rounded_rectangle([N * x, N * .2, N * (x + .16), N * .8], radius=N * .06, fill=255)
    sticker(c, m, 'cream')


def i_back(c, N):
    pts = arc_pts(N * .54, N * .56, N * .24, 30, -180)
    sticker(c, arrow_mask(c.size, pts, N * .11), 'gold')


def i_retry(c, N):
    pts = arc_pts(N * .5, N * .52, N * .27, 140, 140 + 290)
    sticker(c, arrow_mask(c.size, pts, N * .11), 'mint')


def i_star(c, N):
    sticker(c, rounded(poly_mask(c.size, star_pts(N * .5, N * .53, N * .4, N * .18)), N * .03), 'gold')


def i_moon(c, N):
    m = L(N, N); d = ImageDraw.Draw(m)
    d.ellipse([N * .16, N * .14, N * .84, N * .82], fill=255)
    d.ellipse([N * .36, N * .06, N * .98, N * .68], fill=0)
    sticker(c, rounded(m, N * .02), 'gold')


def i_candy(c, N):
    cx, cy, r = N * .5, N * .5, N * .21
    wr = L(N, N); d = ImageDraw.Draw(wr)
    for s in (-1, 1):   # 两边的包装纸
        x0 = cx + s * r * .8; x1 = cx + s * r * 2.05
        d.polygon([(x0, cy - r * .15), (x1, cy - r * .95), (x1 - s * r * .15, cy), (x1, cy + r * .95), (x0, cy + r * .15)], fill=255)
    sticker(c, rounded(wr, N * .02), 'pink', shadow=True)
    body = L(N, N); ImageDraw.Draw(body).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    sticker(c, body, 'pumpkin', shadow=False)
    stripes = L(N, N); ds = ImageDraw.Draw(stripes)
    for k in (-1, 0, 1):
        ds.line([(cx - r + k * r * .7, cy + r), (cx + r * .2 + k * r * .7, cy - r)], fill=255, width=int(r * .28))
    c.paste(Image.new('RGBA', c.size, P['cream'][0] + (255,)), (0, 0), ImageChops.multiply(stripes, erode(body, N * .004)))


def i_cauldron(c, N):
    body = L(N, N); d = ImageDraw.Draw(body)
    d.ellipse([N * .16, N * .3, N * .84, N * .86], fill=255)
    d.rectangle([N * .16, N * .3, N * .84, N * .5], fill=0)
    d.ellipse([N * .16, N * .3, N * .84, N * .86], outline=0)
    d.rounded_rectangle([N * .14, N * .32, N * .86, N * .44], radius=N * .05, fill=255)    # 锅沿
    for x in (.3, .66): d.rounded_rectangle([N * x, N * .8, N * (x + .06), N * .9], radius=N * .02, fill=255)   # 小脚
    sticker(c, body, 'night')
    goo = L(N, N); dg = ImageDraw.Draw(goo)
    dg.ellipse([N * .2, N * .31, N * .8, N * .41], fill=255)
    for (x, y, r) in ((.36, .27, .06), (.52, .22, .07), (.66, .28, .045)):
        dg.ellipse([N * (x - r), N * (y - r), N * (x + r), N * (y + r)], fill=255)
    sticker(c, goo, 'mint', outline=int(N * .02), shadow=False)


def i_gear(c, N):
    m = L(N, N); d = ImageDraw.Draw(m)
    cx = cy = N * .5
    for k in range(8):
        a = math.radians(k * 45)
        px, py = cx + math.cos(a) * N * .32, cy + math.sin(a) * N * .32
        d.ellipse([px - N * .09, py - N * .09, px + N * .09, py + N * .09], fill=255)
    d.ellipse([cx - N * .3, cy - N * .3, cx + N * .3, cy + N * .3], fill=255)
    d.ellipse([cx - N * .11, cy - N * .11, cx + N * .11, cy + N * .11], fill=0)
    sticker(c, m, 'mint')


def i_lock(c, N):
    sh = stroke_mask(c.size, arc_pts(N * .5, N * .44, N * .17, 180, 360), N * .09)
    sticker(c, sh, 'violet')
    body = L(N, N); ImageDraw.Draw(body).rounded_rectangle([N * .24, N * .42, N * .76, N * .84], radius=N * .07, fill=255)
    sticker(c, body, 'gold')
    hole = L(N, N); dh = ImageDraw.Draw(hole)
    dh.ellipse([N * .45, N * .53, N * .55, N * .63], fill=255); dh.polygon([(N * .47, N * .6), (N * .53, N * .6), (N * .55, N * .74), (N * .45, N * .74)], fill=255)
    c.paste(Image.new('RGBA', c.size, INK + (255,)), (0, 0), hole)


def i_globe(c, N):
    """语言切换：地球（11-04 改为不带文字，原来的“文 / A”在两种语言下都中英混排）"""
    cx, cy, r = N * .5, N * .52, N * .34
    m = L(N, N); ImageDraw.Draw(m).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    sticker(c, m, 'mint')
    lines = L(N, N); d = ImageDraw.Draw(lines); w = int(N * .034)
    d.ellipse([cx - r * .42, cy - r, cx + r * .42, cy + r], outline=255, width=w)     # 经线
    d.line([cx, cy - r, cx, cy + r], fill=255, width=w)
    d.line([cx - r, cy, cx + r, cy], fill=255, width=w)                               # 赤道
    for k in (-.52, .52):                                                             # 纬线
        yy = cy + r * k; hw = r * (1 - k * k) ** .5
        d.line([cx - hw, yy, cx + hw, yy], fill=255, width=w)
    c.paste(Image.new('RGBA', c.size, INK + (235,)), (0, 0), ImageChops.multiply(lines, m))


# ---------------- 手势图标（游戏手套渲染 + 动作符号） ----------------
def gesture(name, raw, overlay=None, scale=0.84, offset=(0, 0)):
    N = 512 * SS
    c = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    src = Image.open(GEST / f'raw_{raw}.png').convert('RGBA')
    bb = src.getbbox()
    src = src.crop(bb)
    k = N * scale / max(src.size)
    src = src.resize((int(src.width * k), int(src.height * k)), Image.LANCZOS)
    x = int((N - src.width) / 2 + offset[0] * N); y = int((N - src.height) / 2 + offset[1] * N)
    hand = L(N, N); hand.paste(src.split()[3].point(lambda v: 255 if v > 100 else 0), (x, y))
    ow = int(N * .022)
    rim = dilate(hand, ow)
    c.paste(Image.new('RGBA', (N, N), INK + (110,)), (0, 0), shift(rim, 0, ow * .7))
    c.paste(Image.new('RGBA', (N, N), INK + (255,)), (0, 0), rim)
    c.alpha_composite(src, (x, y))
    if overlay: overlay(c, N)
    finish(c, UI / f'Gesture_{name}.png', (256, 256))


def ov_ticks(cx, cy, ang0, n=3, spread=36, r0=.05, r1=.11, color='gold'):
    def f(c, N):
        m = L(N, N)
        for i in range(n):
            a = math.radians(ang0 + (i - (n - 1) / 2) * spread)
            p0 = (N * cx + math.cos(a) * N * r0, N * cy + math.sin(a) * N * r0)
            p1 = (N * cx + math.cos(a) * N * r1, N * cy + math.sin(a) * N * r1)
            m = ImageChops.lighter(m, stroke_mask(c.size, [p0, p1], N * .028))
        sticker(c, m, color, outline=int(N * .014), shadow=False, shade=False)
    return f


def ov_arrow(pts, color='gold', width=.05):
    def f(c, N):
        sticker(c, arrow_mask(c.size, [(N * x, N * y) for x, y in pts], N * width), color, outline=int(N * .016), shadow=False, shade=False, grain_amt=0)
    return f


def ov_many(*fs):
    def f(c, N):
        for g in fs: g(c, N)
    return f


def curve(p0, p1, bend, n=24):
    """两点间的弧线（bend 为法向弯曲量，单位：画布宽）"""
    mx, my = (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    l = math.hypot(dx, dy) + 1e-6
    cx, cy = mx - dy / l * bend, my + dx / l * bend
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * cx + t * t * p1[0], (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * cy + t * t * p1[1]) for t in (i / n for i in range(n + 1))]


def ov_star(x, y, r):
    def f(c, N):
        sticker(c, rounded(poly_mask(c.size, star_pts(N * x, N * y, N * r, N * r * .45)), N * .006), 'gold', outline=int(N * .014), shadow=False)
    return f


def ov_dots(pts, r=.012, color='cream'):
    def f(c, N):
        m = L(N, N); d = ImageDraw.Draw(m)
        for x, y in pts: d.ellipse([N * (x - r), N * (y - r), N * (x + r), N * (y + r)], fill=255)
        sticker(c, m, color, outline=int(N * .01), shadow=False, shade=False, grain_amt=0)
    return f


def gestures():
    # 捏合：渲染图里指尖相碰处约在 (0.17, 0.52)
    gesture('Pinch', 'pinch', ov_ticks(.13, .5, 180, spread=40), scale=.78, offset=(.06, 0))
    gesture('Shake', 'pinch', ov_many(ov_arrow([(.36, .09), (.5, .07), (.66, .09)], 'mint', .04), ov_arrow([(.64, .25), (.5, .27), (.34, .25)], 'mint', .04)), scale=.7, offset=(0, .12))
    gesture('Throw', 'pinch', ov_arrow(curve((.62, .3), (.2, .1), -.12), 'gold', .055), scale=.68, offset=(.1, .12))
    gesture('Pull', 'pinch', ov_many(ov_dots([(.08, .2)], .05, 'violet'), ov_arrow(curve((.16, .23), (.4, .3), .05), 'mint', .045)), scale=.66, offset=(.12, .1))
    gesture('Launch', 'pinch', ov_many(ov_arrow(curve((.55, .26), (.18, .12), -.1), 'gold', .045), ov_star(.13, .12, .07), ov_ticks(.2, .55, 200, 3, 34, .1, .15, 'gold')), scale=.66, offset=(.12, .12))
    gesture('Pray', 'pray', ov_many(ov_ticks(.5, .12, -90, 3, 40, .05, .1, 'mint')), scale=.8, offset=(0, .07))
    gesture('Sweep', 'sweep', ov_many(ov_arrow([(.44, .86), (.3, .86), (.14, .86), (.06, .86)], 'gold', .045), ov_arrow([(.56, .86), (.7, .86), (.86, .86), (.94, .86)], 'gold', .045)), scale=.8, offset=(0, -.06))
    gesture('Shield', 'shield', ov_many(ov_arrow([(.12, .1), (.12, .2), (.12, .3), (.12, .36)], 'mint', .045), ov_arrow([(.88, .1), (.88, .2), (.88, .3), (.88, .36)], 'mint', .045)), scale=.74, offset=(0, .06))
    gesture('Lamp', 'lamp', None, scale=.86)   # 10-30：新手势——五指张开、手背朝自己，手背亮南瓜鬼脸（raw_lamp 由 HandView 渲染）
    # 通用“捏一下”的小手（按钮提示用）= 捏合手势
    icon_hand()


def icon_hand():
    """“玩法”按钮的小手（11-04 用户：图标没截干净）：渲染图的袖口在画面右边被硬切成一刀直边，
    这里把袖口斜着收短、末端画成圆口（袖口里面一圈深橙），再整体描边。"""
    import numpy as np
    N = 512 * SS
    src = Image.open(GEST / 'raw_pinch.png').convert('RGBA')
    a = np.asarray(src).copy(); h, w = a.shape[:2]
    bb = src.getbbox()
    cut = bb[0] + (bb[2] - bb[0]) * .86                                   # 袖口在这里收掉
    yy, xx = np.mgrid[0:h, 0:w]
    ys = np.nonzero(a[:, int(cut), 3] > 100)[0]; cy, ry = (ys.min() + ys.max()) / 2, (ys.max() - ys.min()) / 2
    rx = ry * .32
    keep = (xx < cut) | (((xx - cut) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1)
    a[~keep] = 0
    src = Image.fromarray(a)
    inner = L(w, h); ImageDraw.Draw(inner).ellipse([cut - rx * .55, cy - ry * .78, cut + rx * .75, cy + ry * .78], fill=255)
    src.paste(Image.new('RGBA', (w, h), (176, 70, 20, 255)), (0, 0), ImageChops.multiply(inner, src.split()[3]))
    src = src.crop(src.getbbox())
    k = N * .74 / max(src.size)
    src = src.resize((int(src.width * k), int(src.height * k)), Image.LANCZOS)
    x = int((N - src.width) / 2 + N * .08); y = int((N - src.height) / 2)
    c = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    hand = L(N, N); hand.paste(src.split()[3].point(lambda v: 255 if v > 100 else 0), (x, y))
    ow = int(N * .022); rim = dilate(hand, ow)
    c.paste(Image.new('RGBA', (N, N), INK + (110,)), (0, 0), shift(rim, 0, ow * .7))
    c.paste(Image.new('RGBA', (N, N), INK + (255,)), (0, 0), rim)
    c.alpha_composite(src, (x, y))
    ov_ticks(.17, .5, 180, spread=40)(c, N)
    finish(c, UI / 'Icon_Hand.png', (256, 256))


def icon_bounds():
    """每个图标看得见的部分在图里的范围（0~1），写成 C# 表：按钮里按可见部分左对齐，不受透明留白影响"""
    rows = []
    for f in sorted(UI.glob('Icon_*.png')):
        im = Image.open(f).convert('RGBA'); W, H = im.size
        bb = im.split()[3].point(lambda v: 255 if v > 40 else 0).getbbox()
        rows.append(f'            {{ "{f.stem}", new Rect({bb[0] / W:.3f}f, {1 - bb[3] / H:.3f}f, {(bb[2] - bb[0]) / W:.3f}f, {(bb[3] - bb[1]) / H:.3f}f) }},')
    out = ROOT / 'Assets' / '_Game' / 'Scripts' / 'Runtime' / 'UI' / 'IconBounds.cs'
    out.write_text('// 自动生成（Tools/make_ui_kit.py icon_bounds）：每个图标可见部分在图片里的范围，x/y 从左下角算，单位为图片宽高的比例。\n'
                   'using System.Collections.Generic;\nusing UnityEngine;\n\nnamespace TrickOrFreeze.UI\n{\n    public static class IconBounds\n    {\n'
                   '        public static readonly Dictionary<string, Rect> All = new Dictionary<string, Rect>\n        {\n' + '\n'.join(rows) +
                   '\n        };\n\n        public static Rect Of(Sprite sp) => sp && All.TryGetValue(sp.name, out var r) ? r : new Rect(0, 0, 1, 1);\n    }\n}\n', encoding='utf-8')
    print('  ', out.name, len(rows), '个图标')


def hint_arrow():
    """视野外提示：一个南瓜橙的圆头 V 形箭头（朝右，运行时旋转）"""
    N = 256 * SS
    c = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    m = stroke_mask((N, N), [(N * .34, N * .2), (N * .7, N * .5), (N * .34, N * .8)], N * .17)
    sticker(c, m, 'pumpkin', outline=int(N * .045), shadow=False)
    finish(c, UI / 'Hint_Arrow.png', (256, 256))


# ---------------- 按钮 / 面板 / 卡片 ----------------
def btn_primary():
    W, H = 1024 * SS // 2, 495 * SS // 2           # 2 倍尺寸画（再缩到 1024x495）
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    m = poly_mask((W, H), wobble(rrect_pts(W * .04, H * .16, W * .96, H * .86, H * .35), H * .008, seed=11))
    sticker(c, m, 'pumpkin', outline=int(H * .05), grain_amt=0)
    ribs = L(W, H); d = ImageDraw.Draw(ribs)   # 南瓜的竖棱（同色系暗线，不抢字）
    for x in (.2, .36, .64, .8):
        d.arc([W * (x - .07), H * .12, W * (x + .07), H * .9], 250 if x < .5 else -70, 110 if x < .5 else 70, fill=255, width=int(H * .025))
    c.paste(Image.new('RGBA', (W, H), P['pumpkin'][1] + (95,)), (0, 0), ImageChops.multiply(ribs, erode(m, H * .02)))
    stem = poly_mask((W, H), [(W * .17, H * .2), (W * .19, H * .03), (W * .24, H * .02), (W * .23, H * .2)])
    sticker(c, rounded(stem, H * .015), 'stem', outline=int(H * .035), shadow=False, grain_amt=0)
    leaf = L(W, H); ImageDraw.Draw(leaf).ellipse([W * .23, H * .02, W * .33, H * .14], fill=255)
    sticker(c, leaf, 'leaf', outline=int(H * .03), shadow=False, grain_amt=0)
    burst(c, W * .92, H * .17, H * .085, 'lime', 9, .45, 8, 31)
    finish(c, UI / 'Btn_Primary.png', (1024, 495))


def btn_secondary():
    W, H = 1024 * SS // 2, 407 * SS // 2
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    x0, y0, x1, y1 = W * .03, H * .14, W * .97, H * .84
    ch = (y1 - y0) * .32
    tag = [(x0 + ch, y0), (x1 - H * .06, y0), (x1, y0 + H * .06), (x1, y1 - H * .06), (x1 - H * .06, y1), (x0 + ch, y1), (x0, y1 - ch), (x0, y0 + ch)]
    m = poly_mask((W, H), wobble(tag, H * .007, seed=21))
    m = rounded(m, H * .02)
    hole = L(W, H); ImageDraw.Draw(hole).ellipse([W * .045, H * .43, W * .075, H * .55], fill=255)
    sticker(c, ImageChops.subtract(m, hole), 'cream', outline=int(H * .045), grain_amt=0)
    finish(c, UI / 'Btn_Secondary.png', (1024, 407))


def panel():
    W, H = 1280 * 2, 843 * 2
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    m = poly_mask((W, H), wobble(rrect_pts(W * .03, H * .04, W * .97, H * .95, H * .06), H * .004, seed=31))
    # 10-30 实机：网点颗粒和虚线在头显里被压缩、缩小成一格一格的方块（用户：面板全是网格带缝）→ 不撒网点，内圈改成一道细实线
    sticker(c, m, 'night', outline=int(H * .016), shade_off=(H * .01, H * .014), grain_amt=0)
    st = L(W, H); d = ImageDraw.Draw(st)
    pts = rrect_pts(W * .06, H * .085, W * .94, H * .91, H * .04, 14)
    d.line(pts + [pts[0]], fill=255, width=int(H * .006), joint='curve')
    st = st.filter(ImageFilter.GaussianBlur(H * .0012))
    c.paste(Image.new('RGBA', (W, H), P['cream'][1] + (150,)), (0, 0), st)
    burst(c, W * .055, H * .07, H * .05, 'lime', 9, .45, 5, 11)
    burst(c, W * .95, H * .92, H * .042, 'pumpkin', 8, .45, -10, 12)
    finish(c, UI / 'Panel.png', (1280, 843))


def card():
    W, H = 1024 * 2, 596 * 2
    lay = json.loads((UI / 'Card_layout.json').read_text())
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    m = poly_mask((W, H), wobble(rrect_pts(W * .025, H * .05, W * .975, H * .93, H * .1), H * .005, seed=41))
    sticker(c, m, 'cream', outline=int(H * .03), shade_off=(H * .016, H * .022), grain_amt=0)
    bx, by, br = lay['badge'][0] * W, (1 - lay['badge'][1]) * H, lay['badgeR'] * W
    b = L(W, H); ImageDraw.Draw(b).ellipse([bx - br, by - br, bx + br, by + br], fill=255)
    sticker(c, b, ((238, 222, 196), (220, 200, 168)), outline=int(H * .014), shadow=False, grain_amt=0)
    for sx, sy in lay['stars']:
        s = rounded(poly_mask((W, H), star_pts(sx * W, (1 - sy) * H, lay['starR'] * W, lay['starR'] * W * .45)), H * .006)
        sticker(c, s, ((236, 220, 192), (236, 220, 192)), outline=int(H * .01), shadow=False, shade=False, grain_amt=0)
    finish(c, UI / 'Card.png', (1024, 596))


def shop_card():
    """糖果铺的升级卡：和关卡卡同样的奶油色手抖贴纸，但竖版、不带编号圆圈和星槽（10-30 用户：糖果铺里糖果和星星叠在一起——
    之前直接拉伸了关卡卡，印在上面的星槽和编号圈露了出来）。只在顶部留一块浅色圆垫放图标。"""
    W, H = 660 * 2, 720 * 2
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    m = poly_mask((W, H), wobble(rrect_pts(W * .04, H * .035, W * .96, H * .95, W * .09), W * .006, seed=57))
    sticker(c, m, 'cream', outline=int(W * .028), shade_off=(W * .016, W * .02), grain_amt=0)
    b = L(W, H); ImageDraw.Draw(b).ellipse([W * .5 - W * .17, H * .23 - W * .17, W * .5 + W * .17, H * .23 + W * .17], fill=255)
    sticker(c, b, ((238, 222, 196), (220, 200, 168)), outline=int(W * .012), shadow=False, grain_amt=0)
    finish(c, UI / 'ShopCard.png', (660, 720))


# ---------------- 标题字 ----------------
def _glyph_row(mask, W, H, text, font, px, cy, tilts, lifts, sizes, track=.98):
    """一行东倒西歪的字：逐字旋转、上下蹦、大小不一，整行水平居中"""
    row = L(W, H); x = 0.0
    for i, ch in enumerate(text):
        if ch == ' ':
            x += px * .32; continue
        t, lf, sz = tilts[i % len(tilts)], lifts[i % len(lifts)], sizes[i % len(sizes)]
        g = Image.new('L', (int(px * 2), int(px * 2)), 0)
        fg = ImageFont.truetype(str(font), int(px * sz))
        dg = ImageDraw.Draw(g); bb = dg.textbbox((0, 0), ch, font=fg)
        dg.text(((g.width - (bb[2] - bb[0])) / 2 - bb[0], (g.height - (bb[3] - bb[1])) / 2 - bb[1]), ch, font=fg, fill=255)
        g = g.rotate(t, resample=Image.BICUBIC)
        adv = (bb[2] - bb[0]) * track
        row.paste(Image.new('L', g.size, 255), (int(x + adv / 2 - g.width / 2), int(cy + H * lf - g.height / 2)), g)
        x += adv
    bb = row.getbbox()
    row = shift(row, (W - (bb[2] - bb[0])) / 2 - bb[0], 0)
    return ImageChops.lighter(mask, row)


def logo_zh():
    """标题字：参考万圣节海报的夸张手写字——每个字东倒西歪、大小不一、上下蹦，底下挂糖浆滴落；
    南瓜橙字 + 深紫错版 + 孔版颗粒，外圈奶油色贴纸边；四周几颗爆炸星。英文副标用 Creepster（自带滴落）荧光绿。"""
    W, H = 1536 * 2, 651 * 2
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    title = '万圣守糖夜'
    tilts = [-9, 6, -5, 8, -7]; lifts = [.03, -.035, .02, -.05, .03]; sizes = [1.0, .9, 1.02, 1.16, .96]
    mask = L(W, H)
    x = W * .03
    for ch, t, lf, sz in zip(title, tilts, lifts, sizes):
        g = Image.new('L', (int(H * .8), int(H * .8)), 0)
        fg = ImageFont.truetype(str(FONT_LOGO), int(H * .4 * sz))
        dg = ImageDraw.Draw(g); bb = dg.textbbox((0, 0), ch, font=fg)
        dg.text(((g.width - (bb[2] - bb[0])) / 2 - bb[0], (g.height - (bb[3] - bb[1])) / 2 - bb[1]), ch, font=fg, fill=255)
        g = g.rotate(t, resample=Image.BICUBIC)
        adv = (bb[2] - bb[0]) * .98
        mask.paste(Image.new('L', g.size, 255), (int(x + adv / 2 - g.width / 2), int(H * (.35 + lf) - g.height / 2)), g)
        x += adv
    bb = mask.getbbox(); dx = (W - (bb[2] - bb[0])) / 2 - bb[0]
    mask = shift(mask, dx, 0)
    mask = dilate(mask, H * .008)                   # 稍加粗，但笔画间要留缝（“糖”“夜”的笔画多）
    # 糖浆滴落：在字的底边随机挑几处挂下圆头水滴
    import numpy as np
    a = np.asarray(mask); rnd = random.Random(5)
    bb = mask.getbbox(); d = ImageDraw.Draw(mask)
    cols = [int(bb[0] + (bb[2] - bb[0]) * k) for k in (.08, .2, .37, .55, .63, .8, .93)]
    for cx in cols:
        cx += rnd.randint(-20, 20)
        ys = np.nonzero(a[:, max(0, min(W - 1, cx))] > 0)[0]
        if len(ys) == 0: continue
        y0 = ys.max(); L_ = H * rnd.uniform(.05, .12); r = H * rnd.uniform(.014, .022)
        d.rounded_rectangle([cx - r * .7, y0 - r, cx + r * .7, y0 + L_], radius=r * .7, fill=255)
        d.ellipse([cx - r, y0 + L_ - r, cx + r, y0 + L_ + r], fill=255)
    mask = jitter(mask, H * .0022, seed=21)
    ow = int(H * .03)
    outer = dilate(mask, ow * 2.6)
    c.paste(Image.new('RGBA', (W, H), INK + (130,)), (0, 0), shift(outer, ow * .4, ow * 1.3))
    c.paste(Image.new('RGBA', (W, H), P['cream'][0] + (255,)), (0, 0), outer)
    riso(c, outer, P['cream'][0], .04, 61)
    c.paste(Image.new('RGBA', (W, H), INK + (255,)), (0, 0), dilate(mask, ow))
    c.paste(Image.new('RGBA', (W, H), P['violet'][0] + (255,)), (0, 0), ImageChops.multiply(shift(mask, -ow * .32, -ow * .26), dilate(mask, ow)))   # 错版（收着点，不糊笔画）
    c.paste(Image.new('RGBA', (W, H), P['pumpkin'][0] + (255,)), (0, 0), mask)
    bb0 = mask.getbbox()
    band = L(W, H); ImageDraw.Draw(band).rectangle([0, bb0[1] + (bb0[3] - bb0[1]) * .58, W, H], fill=255)
    c.paste(Image.new('RGBA', (W, H), P['pumpkin'][1] + (255,)), (0, 0), ImageChops.multiply(mask, band))
    riso(c, mask, P['pumpkin'][0], .045, 51); riso(c, mask, P['pumpkin'][0], .02, 52, light=True)
    # 英文副标
    en = text_mask((W, H), 'CANDY NIGHT WATCH', FONT_CREEP, H * .21, (W / 2, H * .82))
    en = jitter(en, H * .003, seed=23)
    eo = dilate(en, ow * 2.2)
    c.paste(Image.new('RGBA', (W, H), INK + (130,)), (0, 0), shift(eo, ow * .3, ow))
    c.paste(Image.new('RGBA', (W, H), P['night'][0] + (255,)), (0, 0), eo)
    c.paste(Image.new('RGBA', (W, H), INK + (255,)), (0, 0), dilate(en, ow * .7))
    c.paste(Image.new('RGBA', (W, H), P['pumpkin'][0] + (255,)), (0, 0), ImageChops.multiply(shift(en, -ow * .4, -ow * .3), dilate(en, ow * .7)))
    c.paste(Image.new('RGBA', (W, H), P['lime'][0] + (255,)), (0, 0), en)
    riso(c, en, P['lime'][0], .06, 71)
    # 爆炸星点缀 + 一弯小月亮
    bb = mask.getbbox()
    burst(c, bb[0] - H * .02, bb[1] + H * .05, H * .07, 'lime', 9, .45, 10, 1)
    burst(c, bb[2] + H * .01, bb[3] - H * .02, H * .055, 'pink', 8, .45, -8, 2)
    burst(c, bb[0] + (bb[2] - bb[0]) * .46, bb[1] - H * .03, H * .04, 'gold', 8, .5, 0, 3)
    mx, my, mr = bb[2] - H * .03, bb[1] + H * .01, H * .08
    moon = L(W, H); dm = ImageDraw.Draw(moon)
    dm.ellipse([mx - mr, my - mr, mx + mr, my + mr], fill=255); dm.ellipse([mx - mr * .45, my - mr * 1.25, mx + mr * 1.5, my + mr * .7], fill=0)
    sticker(c, rounded(moon, H * .004), 'gold', outline=ow, shadow=False)
    finish(c, UI / 'Logo_zh.png', (1536, 651))


def logo(lang='zh'):
    """标题字：参考万圣节海报的夸张手写字——每个字东倒西歪、大小不一、上下蹦，底下挂糖浆滴落；
    南瓜橙字 + 深紫错版 + 孔版颗粒，外圈奶油色贴纸边；四周几颗爆炸星和一弯小月亮。
    11-04 用户：中文模式从头到尾只能是中文、英文模式只能是英文——拆成两张：
    Logo_zh.png 只有“万圣守糖夜”，Logo_en.png 只有 CANDY NIGHT WATCH（同一套造型，Baloo 2 圆胖字体对应快乐体）。"""
    W, H = 1536 * 2, 651 * 2
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    mask = L(W, H)
    if lang == 'zh':
        mask = _glyph_row(mask, W, H, '万圣守糖夜', FONT_LOGO, H * .4, H * .42,
                          [-9, 6, -5, 8, -7], [.02, -.03, .015, -.045, .025], [1.0, .9, 1.02, 1.16, .96])
    else:
        mask = _glyph_row(mask, W, H, 'CANDY', FONT_BALOO, H * .44, H * .3,
                          [-8, 6, -4, 7, -6], [.015, -.025, .02, -.03, .01], [1.0, .94, 1.04, .96, 1.06], track=1.0)
        mask = _glyph_row(mask, W, H, 'NIGHT WATCH', FONT_BALOO, H * .3, H * .72,
                          [5, -6, 4, -5, 6, 0, -5, 6, -4, 5, -6], [-.01, .015, -.02, .01, -.015, 0, .02, -.01, .015, -.02, .01],
                          [1.0, .95, 1.04, .97, 1.02, 1, .98, 1.03, .95, 1.02, .97], track=1.06)
    mask = dilate(mask, H * .008)                   # 稍加粗，但笔画间要留缝（“糖”“夜”的笔画多）
    # 糖浆滴落：在字的底边随机挑几处挂下圆头水滴
    import numpy as np
    a = np.asarray(mask); rnd = random.Random(5)
    bb = mask.getbbox(); d = ImageDraw.Draw(mask)
    cols = [int(bb[0] + (bb[2] - bb[0]) * k) for k in (.08, .2, .37, .55, .63, .8, .93)]
    for cx in cols:
        cx += rnd.randint(-20, 20)
        ys = np.nonzero(a[:, max(0, min(W - 1, cx))] > 0)[0]
        if len(ys) == 0: continue
        y0 = ys.max(); L_ = H * rnd.uniform(.05, .1); r = H * rnd.uniform(.014, .022)
        d.rounded_rectangle([cx - r * .7, y0 - r, cx + r * .7, y0 + L_], radius=r * .7, fill=255)
        d.ellipse([cx - r, y0 + L_ - r, cx + r, y0 + L_ + r], fill=255)
    mask = jitter(mask, H * .0022, seed=21)
    ow = int(H * .03)
    outer = dilate(mask, ow * 2.6)
    c.paste(Image.new('RGBA', (W, H), INK + (130,)), (0, 0), shift(outer, ow * .4, ow * 1.3))
    c.paste(Image.new('RGBA', (W, H), P['cream'][0] + (255,)), (0, 0), outer)
    riso(c, outer, P['cream'][0], .04, 61)
    c.paste(Image.new('RGBA', (W, H), INK + (255,)), (0, 0), dilate(mask, ow))
    c.paste(Image.new('RGBA', (W, H), P['violet'][0] + (255,)), (0, 0), ImageChops.multiply(shift(mask, -ow * .32, -ow * .26), dilate(mask, ow)))   # 错版（收着点，不糊笔画）
    c.paste(Image.new('RGBA', (W, H), P['pumpkin'][0] + (255,)), (0, 0), mask)
    bb0 = mask.getbbox()
    band = L(W, H); ImageDraw.Draw(band).rectangle([0, bb0[1] + (bb0[3] - bb0[1]) * .58, W, H], fill=255)
    c.paste(Image.new('RGBA', (W, H), P['pumpkin'][1] + (255,)), (0, 0), ImageChops.multiply(mask, band))
    riso(c, mask, P['pumpkin'][0], .045, 51); riso(c, mask, P['pumpkin'][0], .02, 52, light=True)
    # 爆炸星点缀 + 一弯小月亮
    bb = mask.getbbox()
    burst(c, bb[0] - H * .02, bb[1] + H * .05, H * .07, 'lime', 9, .45, 10, 1)
    burst(c, bb[2] + H * .01, bb[3] - H * .02, H * .055, 'pink', 8, .45, -8, 2)
    burst(c, bb[0] + (bb[2] - bb[0]) * .46, bb[1] - H * .03, H * .04, 'gold', 8, .5, 0, 3)
    mx, my, mr = bb[2] - H * .03, bb[1] + H * .01, H * .08
    moon = L(W, H); dm = ImageDraw.Draw(moon)
    dm.ellipse([mx - mr, my - mr, mx + mr, my + mr], fill=255); dm.ellipse([mx - mr * .45, my - mr * 1.25, mx + mr * 1.5, my + mr * .7], fill=0)
    sticker(c, rounded(moon, H * .004), 'gold', outline=ow, shadow=False)
    finish(c, UI / f'Logo_{lang}.png', (1536, 651))


if __name__ == '__main__':
    print('图标'); [icon(n, f) for n, f in (('Play', i_play), ('Pause', i_pause), ('Back', i_back), ('Retry', i_retry), ('Star', i_star), ('Moon', i_moon),
                                            ('Candy', i_candy), ('Cauldron', i_cauldron), ('Gear', i_gear), ('Lock', i_lock), ('Globe', i_globe))]
    print('手势'); gestures(); hint_arrow()
    print('按钮 / 面板 / 卡片'); btn_primary(); btn_secondary(); panel(); card(); shop_card()
    print('标题'); logo_zh(); logo('en')
    icon_bounds()   # 11-04 用户：中文标题下的英文小字是点缀，保留

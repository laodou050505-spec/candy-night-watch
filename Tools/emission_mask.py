"""从贴图提取自发光遮罩：只保留高亮的暖色（黄/橙，雕脸里的烛光、窗户灯光、灯笼玻璃）或指定色相。
  python3 Tools/emission_mask.py <albedo.png> <out.png> [色相下限=0.06] [色相上限=0.17] [亮度阈值=0.78]
遮罩做一次 2px 模糊，边缘柔和。"""
import sys
import numpy as np
from PIL import Image, ImageFilter
src, out = sys.argv[1], sys.argv[2]
h0 = float(sys.argv[3]) if len(sys.argv) > 3 else 0.06
h1 = float(sys.argv[4]) if len(sys.argv) > 4 else 0.17
vt = float(sys.argv[5]) if len(sys.argv) > 5 else 0.78
img = Image.open(src).convert('RGB')
hsv = np.asarray(img.convert('HSV')).astype(np.float32) / 255.0
h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
hue_ok = (h >= h0) & (h <= h1)
m = np.clip((v - vt) / (1 - vt), 0, 1) * hue_ok * np.clip((s - 0.25) / 0.3, 0, 1)
rgb = np.asarray(img).astype(np.float32) / 255.0
res = (rgb * m[..., None] * 255).astype(np.uint8)
Image.fromarray(res).filter(ImageFilter.GaussianBlur(2)).save(out)
print('mask coverage', round(float((m > 0.1).mean()), 4))

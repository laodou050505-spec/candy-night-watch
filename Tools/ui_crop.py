"""界面素材裁切：按透明通道裁到内容包围盒 + 边距，去掉几乎透明的外圈辉光（阈值以下置 0），可选缩放到最长边。
用法：python3 Tools/ui_crop.py <源.png> <目标.png> [最长边像素=1024] [透明阈值=24]"""
import sys
import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
maxside = int(sys.argv[3]) if len(sys.argv) > 3 else 1024
thr = int(sys.argv[4]) if len(sys.argv) > 4 else 24
im = Image.open(src).convert('RGBA')
a = np.array(im)
alpha = a[..., 3].astype(np.int32)
a[..., 3] = np.where(alpha < thr, 0, np.clip((alpha - thr) * 255 // (255 - thr), 0, 255)).astype(np.uint8)
ys, xs = np.nonzero(a[..., 3] > 0)
pad = 12
y0, y1 = max(0, ys.min() - pad), min(a.shape[0], ys.max() + pad)
x0, x1 = max(0, xs.min() - pad), min(a.shape[1], xs.max() + pad)
out = Image.fromarray(a[y0:y1, x0:x1])
s = maxside / max(out.size)
if s < 1: out = out.resize((round(out.size[0] * s), round(out.size[1] * s)), Image.LANCZOS)
out.save(dst)
print('UI', dst, out.size)

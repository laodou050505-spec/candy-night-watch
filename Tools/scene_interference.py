"""场景精确干涉审计（Blender 里运行，纯几何，不需要材质）：
  Blender -b -P Tools/scene_interference.py -- <geo 目录> <输出 json>
输入由 Unity 菜单 Export Scene Geometry 生成（Unity 世界坐标，单位米）。
对每对包围盒相交的摆件：
  - BVHTree.overlap 求相交三角面对数（>0 说明表面互相穿过）
  - 穿插深度：A 的顶点若在 B 内部（三方向射线奇偶投票），取到 B 表面最近距离的最大值；双向取大
  - 相交区域包围盒中心（定位用）
对每个摆件：
  - 离其他所有几何的最近距离（顶点到其他 BVH 的 find_nearest 最小值）→ 判断“悬空 / 没挂在任何东西上”
"""
import bpy, sys, json, struct, math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
gdir, out = argv[0], argv[1]
PARTS = len(argv) > 2 and argv[2] == 'parts'          # 按渲染体（部件）粒度统计，便于定位
items_raw = json.load(open(gdir + '/items.json'))
meshes = []
with open(gdir + '/meshes.bin', 'rb') as f:
    n = struct.unpack('<i', f.read(4))[0]
    for _ in range(n):
        nv, nt = struct.unpack('<ii', f.read(8))
        v = np.frombuffer(f.read(nv * 12), dtype='<f4').reshape(nv, 3).astype(np.float64)
        t = np.frombuffer(f.read(nt * 12), dtype='<i4').reshape(nt, 3)
        meshes.append((v, t))

# 按摆件聚合（一个摆件可能由多个渲染体组成）
items = {}
for r in items_raw:
    v, t = meshes[r['mesh']]
    M = np.array(r['m']).reshape(4, 4)
    w = (np.c_[v, np.ones(len(v))] @ M.T)[:, :3]
    it = items.setdefault(r['path'] if PARTS else r['item'], {'verts': [], 'tris': [], 'off': 0, 'paths': [], 'item': r['item']})
    it['tris'].append(t + it['off']); it['verts'].append(w); it['off'] += len(w); it['paths'].append(r['path'])
names = list(items)
data = {}
for k in names:
    it = items[k]
    V = np.vstack(it['verts']); T = np.vstack(it['tris'])
    # 去掉退化三角形（BVH 不喜欢）
    data[k] = dict(V=V, T=T, lo=V.min(0), hi=V.max(0), bvh=BVHTree.FromPolygons([tuple(p) for p in V], [tuple(x) for x in T], epsilon=0.0),
                   paths=it['paths'], item=it['item'])
print('ITEMS', len(names), 'tris', sum(len(d['T']) for d in data.values()))

def inside(bvh, p):
    votes = 0
    for d in (Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0.37, 0.11, 0.92)).normalized()):
        o = Vector(p); hits = 0
        for _ in range(64):
            loc, nrm, idx, dist = bvh.ray_cast(o, d, 100.0)
            if loc is None: break
            hits += 1; o = loc + d * 1e-4
        if hits % 2 == 1: votes += 1
    return votes >= 2

def depth_into(A, B, region_lo, region_hi):
    """A 在 region 内的顶点里，处在 B 内部的最大深度"""
    V = A['V']
    m = np.all((V >= region_lo - 0.05) & (V <= region_hi + 0.05), axis=1)
    idx = np.nonzero(m)[0]
    if len(idx) > 3000: idx = idx[np.linspace(0, len(idx) - 1, 3000).astype(int)]
    best = 0.0
    for i in idx:
        p = Vector(V[i])
        if inside(B['bvh'], p):
            loc, nrm, fi, d = B['bvh'].find_nearest(p)
            if d is not None: best = max(best, d)
    return best

res = {'pairs': [], 'nearest': []}
for i in range(len(names)):
    A = data[names[i]]
    for j in range(i + 1, len(names)):
        B = data[names[j]]
        if PARTS and A['item'] == B['item']: continue   # 同一摆件内部的部件不查
        if np.any(A['hi'] < B['lo']) or np.any(B['hi'] < A['lo']): continue
        ov = A['bvh'].overlap(B['bvh'])
        if not ov: continue
        tri = np.array([a for a, b in ov[:4000]])
        pts = A['V'][A['T'][tri].reshape(-1)]
        lo, hi = pts.min(0), pts.max(0)
        d = max(depth_into(A, B, lo, hi), depth_into(B, A, lo, hi))
        res['pairs'].append(dict(a=names[i], b=names[j], tris=len(ov), depth=round(d, 4), lo=[round(x, 2) for x in lo], hi=[round(x, 2) for x in hi]))
print('PAIRS', len(res['pairs']))

# 每个摆件到其他几何的最近距离（取摆件自身顶点的采样）
for k in names:
    A = data[k]
    V = A['V']; idx = np.linspace(0, len(V) - 1, min(len(V), 1500)).astype(int)
    best = 1e9
    cand = [o for o in names if o != k and not (np.any(data[o]['hi'] < A['lo'] - 0.5) or np.any(A['hi'] < data[o]['lo'] - 0.5))]
    for o in cand:
        bvh = data[o]['bvh']
        for i in idx:
            loc, nrm, fi, d = bvh.find_nearest(Vector(V[i]), best)
            if d is not None and d < best: best = d
        if best < 1e-3: break
    res['nearest'].append(dict(item=k, dist=round(best if best < 1e8 else -1, 4), lo=[round(x, 2) for x in A['lo']], hi=[round(x, 2) for x in A['hi']], path=A['paths'][0]))
json.dump(res, open(out, 'w'), ensure_ascii=False, indent=1)
print('DONE')

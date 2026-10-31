"""贴图清晰度对比（Blender 里运行）：Blender -b -P Tools/texture_compare.py -- <名称> <输出目录>
原始 Tripo model.glb 与工程里 LOD0.fbx+albedo.png 用相同相机、无光照（只显示贴图颜色）渲染 3 个近景（正/侧/特写），
并计算每张图的拉普拉斯方差（清晰度）。输出 <名称>_orig_*.png / <名称>_game_*.png 和 <名称>_sharp.json。"""
import bpy, sys, os, json
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
name, out = argv[0], argv[1]
ROOT = '/Users/ld/Documents/ChatGPT/万圣节'
os.makedirs(out, exist_ok=True)


def unlit(objs, img=None):
    for o in objs:
        for slot in o.material_slots:
            m = slot.material
            if not m:
                continue
            m.use_nodes = True
            nt = m.node_tree
            tex = img
            if tex is None:
                for nd in nt.nodes:
                    if nd.type == 'TEX_IMAGE' and nd.image:
                        tex = nd.image
                        break
            for nd in list(nt.nodes):
                nt.nodes.remove(nd)
            outn = nt.nodes.new('ShaderNodeOutputMaterial')
            em = nt.nodes.new('ShaderNodeEmission')
            nt.links.new(em.outputs[0], outn.inputs[0])
            if tex:
                tn = nt.nodes.new('ShaderNodeTexImage')
                tn.image = tex
                tn.interpolation = 'Linear'
                nt.links.new(tn.outputs[0], em.inputs[0])


def norm(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    s = 1.0 / max(mx.z - mn.z, 1e-6)
    c = (mn + mx) / 2
    for o in objs:
        o.matrix_world = Matrix.Diagonal((s, s, s, 1)) @ Matrix.Translation(-c) @ o.matrix_world
    return (mx - mn) * s


def render(tag, size):
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 768
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new('w')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (0.3, 0.3, 0.32, 1)
    cd = bpy.data.cameras.new('c')
    cd.lens = 50
    cam = bpy.data.objects.new('c', cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    r = max(size) * 0.5
    shots = {'front': (Vector((0, -1, 0.15)), r * 3.2, Vector((0, 0, 0))),
             'side': (Vector((1, 0, 0.15)), r * 3.2, Vector((0, 0, 0))),
             'close': (Vector((0.3, -1, 0.35)), r * 1.25, Vector((0, 0, 0.1)))}
    sharp = {}
    for k, (d, dist, tgt) in shots.items():
        d = d.normalized()
        cam.location = tgt + d * dist
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        fp = os.path.join(out, f'{name}_{tag}_{k}.png')
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(fp)
        px = np.array(im.pixels[:]).reshape(768, 768, 4)[..., :3]
        g = px.mean(2)
        lap = 4 * g[1:-1, 1:-1] - g[:-2, 1:-1] - g[2:, 1:-1] - g[1:-1, :-2] - g[1:-1, 2:]
        mask = np.abs(g[1:-1, 1:-1] - 0.3) > 0.02
        sharp[k] = float(np.var(lap[mask]) * 1e4) if mask.sum() > 100 else 0.0
    return sharp


res = {}
bpy.ops.wm.read_factory_settings(use_empty=True)
src = f'{ROOT}/ArtSource/Models/{name}/model.glb'
if os.path.exists(src):
    bpy.ops.import_scene.gltf(filepath=src)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    unlit(ms)
    res['orig'] = render('orig', norm(ms))

bpy.ops.wm.read_factory_settings(use_empty=True)
fbx = f'{ROOT}/Assets/_Game/Art/Models/{name}/{name}_LOD0.fbx'
alb = f'{ROOT}/Assets/_Game/Art/Models/{name}/{name}_albedo.png'
bpy.ops.import_scene.fbx(filepath=fbx)
ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
img = bpy.data.images.load(alb)
for o in ms:
    if not o.material_slots:
        o.data.materials.append(bpy.data.materials.new('m'))
unlit(ms, img)
res['game'] = render('game', norm(ms))
if 'orig' in res:
    res['ratio'] = {k: round(res['game'][k] / max(1e-6, res['orig'][k]), 3) for k in res['game']}
json.dump(res, open(os.path.join(out, f'{name}_sharp.json'), 'w'), indent=1)
print('SHARP', name, json.dumps(res.get('ratio', {})))

"""装配体审查渲染（Blender 里运行）：Blender -b -P Tools/render_assembly.py -- <装配目录> <名称> <输出.png>
导入装配 FBX，按 _map.txt 给每个实例配上对应部件贴图（无贴图的用灰色），EEVEE 带光照渲染 6 个角度：
玩家视角（从 Unity +Z 方向、眼高处看过来）、正前、俯视、左、右、低角度仰视；拼成一张 3×2 图。"""
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
d, name, out = argv
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(d, name + '.fbx'))
mp = dict(l.split('\t') for l in open(os.path.join(d, name + '_map.txt')).read().splitlines() if '\t' in l)
mats = {}
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
    part = mp.get(o.name.split('.')[0])
    if part not in mats:
        m = bpy.data.materials.new(part or 'none'); m.use_nodes = True
        bsdf = m.node_tree.nodes.get('Principled BSDF')
        tp = os.path.join(d, f'{part}_albedo.png')
        if part and os.path.exists(tp):
            tn = m.node_tree.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(tp)
            m.node_tree.links.new(tn.outputs[0], bsdf.inputs['Base Color'])
        else:
            bsdf.inputs['Base Color'].default_value = (0.25, 0.22, 0.3, 1) if part and ('Handle' in part or 'Ring' in part or 'Frame' in part or 'Body' in part) else (0.8, 0.75, 0.7, 1)
        if part == 'Candle_Flame':
            bsdf.inputs['Emission Color'].default_value = (1, 0.6, 0.2, 1); bsdf.inputs['Emission Strength'].default_value = 6
        if part == 'Potion_Surface':
            bsdf.inputs['Base Color'].default_value = (0.2, 0.9, 0.3, 1); bsdf.inputs['Emission Color'].default_value = (0.2, 0.9, 0.3, 1); bsdf.inputs['Emission Strength'].default_value = 1.5
        if part == 'Gem_Hex':
            bsdf.inputs['Base Color'].default_value = (0.9, 0.3, 0.7, 1)
        mats[part] = m
    o.data.materials.clear(); o.data.materials.append(mats[part])
ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
pts = [o.matrix_world @ Vector(c) for o in ms for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
c = (mn + mx) / 2; r = (mx - mn).length / 2
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = sc.render.resolution_y = 600
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.32, 0.28, 0.42, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (0.8, 0.2, 0.6); sun.data.energy = 3
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
# Unity +Z（玩家方向）= Blender -Y
views = [(Vector((0, -1, 0.55)), 'player'), (Vector((0, -1, 0.12)), 'front'), (Vector((0.01, -0.05, 1)), 'top'),
         (Vector((1, 0, 0.2)), 'left'), (Vector((-1, 0.3, 0.2)), 'right'), (Vector((0.5, -1, -0.35)), 'low')]
tiles = []
for dv, tag in views:
    cam.location = c + dv.normalized() * r * 2.6
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    fp = f'/tmp/_asm_{name}_{tag}.png'; sc.render.filepath = fp; bpy.ops.render.render(write_still=True); tiles.append(fp)
imgs = [bpy.data.images.load(p) for p in tiles]
W = 600; grid = bpy.data.images.new('grid', W * 3, W * 2)
import numpy as np
px = np.zeros((W * 2, W * 3, 4), dtype=np.float32)
for i, im in enumerate(imgs):
    a = np.array(im.pixels[:], dtype=np.float32).reshape(W, W, 4)
    gx, gy = i % 3, 1 - i // 3
    px[gy * W:(gy + 1) * W, gx * W:(gx + 1) * W] = a
grid.pixels = px.ravel(); grid.filepath_raw = out; grid.file_format = 'PNG'; grid.save()
print('RENDER', out)

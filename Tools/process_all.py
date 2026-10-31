"""批量调用 Blender 清理导出所有已贴图的模型 → Assets/_Game/Art/Models/<名称>/。
用法：python3 -I Tools/process_all.py [名称...]
目标高度（米）按游戏内实际尺寸：敌人与原型规则层一致（KINDS.height），场景件按浮空圣坛设计。"""
import json, pathlib, subprocess, sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / 'ArtSource' / 'Models'
DST = ROOT / 'Assets' / '_Game' / 'Art' / 'Models'
BLENDER = '/Applications/Blender.app/Contents/MacOS/Blender'

# 名称 → (目标高度米, LOD0 面数上限)
SIZES = {
    'E1_Skelly': (0.20, 16000), 'E2_Batsy': (0.10, 14000), 'E3_Dangle': (0.09, 12000), 'E4_Jack': (0.22, 18000),
    'E5_Mitten': (0.13, 14000), 'E6_Boo': (0.20, 10000), 'E7_Frankenbear': (0.42, 24000),
    # 桌面高 0.72，桌半径 1.15：模型整体高度（含藤腿）就是桌面高
    'S1_PumpkinTable': (0.74, 40000), 'S2_CandyBucket': (0.24, 20000), 'S3_Cauldron': (0.17, 20000),
    'S4_BookTower': (2.6, 30000), 'S5_WitchBed': (1.9, 36000), 'S6_FangWardrobe': (2.1, 26000), 'S7_Coffin': (1.7, 16000),
    'S8_PumpkinPile': (0.55, 24000), 'S9_Broom': (1.4, 12000), 'S10_CandyWrapped': (0.03, 6000), 'S11_LollipopPumpkin': (0.09, 6000),
    'S12_Gravestone': (0.9, 10000),
    'L1_ArchColumn': (7.5, 30000), 'L2_RoseWindow': (14.0, 30000), 'L3_HauntedManor': (9.0, 45000), 'L4_WitchCottage': (6.0, 40000),
    'L5_Scarecrow': (2.2, 20000), 'L6_BellTower': (8.0, 30000), 'L7_FloatIsland': (6.0, 40000), 'L8_RopeBridgePost': (1.6, 12000),
    'L9_MushroomLamp': (2.4, 14000), 'L10_CandleCluster': (0.35, 10000),
    # 万圣节符号（米）
    'H1_JackLanternBig': (0.75, 18000), 'H2_SpookyTree': (3.6, 30000), 'H3_GraveyardSet': (1.1, 26000), 'H4_WitchOnBroom': (0.9, 20000),
    'H5_SkullCandelabra': (0.38, 14000), 'H6_BatLantern': (0.5, 10000), 'H7_CandyCornPile': (0.32, 12000), 'H8_PotionShelf': (1.5, 22000),
}

names = sys.argv[1:] or list(SIZES)
for n in names:
    g = SRC / n / 'clean.glb'          # 参考原模型去过尖刺的修复版（despike_ref.py）
    if not g.exists():
        g = SRC / n / 'repaired.glb'
    if not g.exists():
        g = SRC / n / 'model.glb'
    if not g.exists():
        print(f'{n:22s} 跳过：没有 model.glb'); continue
    h, cap = SIZES[n]
    out = DST / n
    r = subprocess.run([BLENDER, '-b', '-P', str(ROOT / 'Tools' / 'blender_process.py'), '--', str(g), str(out), n, str(h), str(cap)],
                       capture_output=True, text=True)
    # 导出后对每个 LOD 做一次 FBX 往返检查与修复（导入端丢掉的退化面、减面后的小洞）
    for li in range(3):
        fp = out / f'{n}_LOD{li}.fbx'
        if fp.exists():
            subprocess.run([BLENDER, '-b', '-P', str(ROOT / 'Tools' / 'crack_fix.py'), '--', str(fp)], capture_output=True, text=True)
    rep = out / f'{n}_process.json'
    if rep.exists():
        d = json.loads(rep.read_text())
        t = d['textures'][0] if d['textures'] else {}
        print(f"{n:22s} faces {d['faces_in']}→{d['lod_faces']} 碎块 -{d['debris_removed_islands']}块/{d['debris_removed_faces']}面 "
              f"size={d['size_m']} tex={t.get('size')} 锐度={t.get('laplacian_var')}")
    else:
        print(f'{n:22s} 失败'); print(r.stdout[-1500:]); print(r.stderr[-1500:])

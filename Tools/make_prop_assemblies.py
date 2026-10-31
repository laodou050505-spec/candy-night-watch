"""Tripo 布景道具 → 单件装配（统一尺寸、正面朝 Unity +Z），只处理已经走完流程（有 model.glb 且审计通过）的。
python3 -I Tools/make_prop_assemblies.py  → 打印生成了哪些，再逐个跑 assemble.py"""
import json, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[1]
PROPS = {   # 名称: (fit, 额外 yaw)
    'P_HayBale': ({'w': 1.2}, 0), 'P_CornShock': ({'h': 2.0}, 0), 'P_StoneUrn': ({'h': 0.55}, 0), 'P_Crow': ({'h': 0.32}, 0),
    'P_BlackCat': ({'h': 0.45}, 0), 'P_Owl': ({'h': 0.42}, 0), 'P_SheetGhost': ({'h': 0.95}, 0), 'P_GiantSpider': ({'w': 0.9}, 0),
    'P_Lantern': ({'h': 0.45}, 0), 'P_Barrel': ({'h': 0.9}, 0), 'P_AppleCrate': ({'w': 0.7}, 0), 'P_GardenBench': ({'w': 1.6}, 0),
    'P_PumpkinStack': ({'h': 0.95}, 0), 'P_WitchHat': ({'h': 0.42}, 0), 'P_SkullPile': ({'h': 0.36}, 0), 'P_Crow2': ({'h': 0.34}, 0), 'P_BlackCat2': ({'h': 0.45}, 0),
}
TRIM_CROW = 0.115
done = []
for n, (fit, yaw) in PROPS.items():
    d = ROOT / 'ArtSource/Models' / n
    a = d / 'audit.json'
    if not (d / 'model.glb').exists() or not a.exists() or not json.loads(a.read_text()).get('pass'): continue
    name = 'Deco_' + n[2:].rstrip('0123456789')      # P_Crow2 → Deco_Crow
    spec = {"name": name, "allow_overlap": 0.01, "allow_pairs": [], "parts": [
        {"id": "Body", "src": n, "fit": fit, "decimate": 4000, "inst": [{"name": "Body", "pos": [0, 0, 0], "yaw": -90 + yaw}]}]}
    if n == 'P_Crow2': spec['parts'][0]['trim_bottom'] = TRIM_CROW      # 去掉摆件底座，只留鸟
    (ROOT / 'Tools/assemblies' / f'{name}.json').write_text(json.dumps(spec, indent=1))
    done.append(name)
print(' '.join(done))

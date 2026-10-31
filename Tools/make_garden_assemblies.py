"""根据 blender_garden3.py 输出的布局数据生成墓园、南瓜田、温室、喷泉的装配 JSON。
实例名一律用“部件id_序号”，装配脚本按前缀匹配穿插白名单。"""
import json, random, math, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[1]
L = json.load(open(ROOT / 'ArtSource/Procedural/garden3_layout.json'))
R = random.Random(9)
hy = lambda x, z: L['hill_y'].get(f'{round(x * 2) / 2:.1f},{round(z * 2) / 2:.1f}', 0.0)
A = ROOT / 'Tools/assemblies'
def keep(i, src): return {"id": i, "src": "proc:" + src, "fit": {}, "keep_origin": True, "inst": [{"name": i, "pos": [0, 0, 0]}]}
# ---- 墓园 ----
kinds = {}; cand = []; flames = []; skulls = []; pumps = []
for k, g in enumerate(L['graves']):
    kid = 'G' + g['kind']
    kinds.setdefault(kid, []).append({"name": f"{kid}_{k}", "pos": [g['x'], g['y'], g['z']], "yaw": 90 + g['yaw'], "rot": [g['tilt'], 0, 0], "scale": g['s']})
    if R.random() < 0.5:
        for j in range(R.choice([1, 2, 3])):
            x = g['x'] + 0.42 + R.uniform(0, 0.12); z = g['z'] + (-0.22, 0.02, 0.24)[j] + R.uniform(-0.04, 0.04); y = hy(x, z) - 0.01
            s = R.uniform(1.5, 2.2); sh = R.uniform(0.55, 1.0)
            cand.append({"name": f"Candle_{len(cand)}", "pos": [x, y, z], "scale": [s, s, s * sh]})
            flames.append({"name": f"Flame_{len(flames)}", "pos": [x, y + 0.163 * s * sh - 0.004 * s, z], "scale": s})
    r = R.random()
    if r < 0.2: skulls.append({"name": f"Skull_{len(skulls)}", "pos": [g['x'] + 0.3, hy(g['x'] + 0.3, g['z'] + 0.62) - 0.02, g['z'] + 0.62], "yaw": R.uniform(60, 120)})
    elif r < 0.45: pumps.append({"name": f"Pk_{len(pumps)}", "pos": [g['x'] + 0.25, hy(g['x'] + 0.25, g['z'] - 0.62) - 0.045, g['z'] - 0.62], "yaw": R.uniform(0, 360), "scale": R.uniform(0.5, 0.8)})
parts = [keep("Hill", "Gv_Hill"), keep("Fence", "Gv_Fence"), keep("MzStone", "Mz_Stone"), keep("MzRoof", "Mz_Roof"), keep("MzPed", "Mz_Pediment"), keep("MzDoor", "Mz_Door")]
for kd, inst in kinds.items(): parts.append({"id": kd, "src": "proc:Gv_" + kd[1:], "fit": {}, "inst": inst})
parts.append({"id": "Candle", "src": "proc:Candle_Float", "fit": {}, "decimate": 420, "inst": cand})
parts.append({"id": "Flame", "src": "proc:Candle_Flame", "fit": {}, "inst": flames})
if skulls: parts.append({"id": "Skull", "src": "P_Skull", "fit": {"h": 0.2}, "decimate": 1600, "inst": skulls})
if pumps: parts.append({"id": "Pk", "src": "proc:Pp_Pumpkin", "fit": {}, "inst": pumps})
ap = [["Hill", x] for x in ["Fence", "MzStone", "GRound", "GCross", "GSlab", "GObelisk", "Candle", "Skull", "Pk", "MzDoor", "Flame"]] + [["MzStone", "MzRoof"], ["MzStone", "MzDoor"], ["Candle", "Flame"], ["MzPed", "MzRoof"], ["MzPed", "MzStone"]]
json.dump({"name": "Deco_Cemetery", "allow_overlap": 0.02, "allow_pairs": ap, "parts": parts}, open(A / 'Deco_Cemetery.json', 'w'), indent=1)
# ---- 南瓜田 ----
pk = []; st = []
for k, p in enumerate(L['pumpkins']):
    pk.append({"name": f"Pumpkin_{k}", "pos": [p['x'], 0.08, p['z']], "yaw": p['yaw'], "scale": p['s']})
    st.append({"name": f"Stem_{k}", "pos": [p['x'], 0.08, p['z']], "yaw": p['yaw'], "scale": p['s']})
for j, (dx, dz) in enumerate([(-0.33, 0.12), (0.05, -0.12), (0.38, 0.12)]):
    c, s_ = math.cos(math.radians(-30)), math.sin(math.radians(-30))
    x = 10.6 + dx * c - dz * s_; z = -12.4 + dx * s_ + dz * c
    pk.append({"name": f"Pumpkin_c{j}", "pos": [x, 0.42, z], "yaw": j * 70, "scale": 0.6})
    st.append({"name": f"Stem_c{j}", "pos": [x, 0.42, z], "yaw": j * 70, "scale": 0.6})
hay = [{"pos": [8.0, 0, 1.6], "yaw": 90}, {"pos": [8.0, 0, 2.85], "yaw": 92}, {"pos": [8.0, 0.52, 2.2], "yaw": 88}, {"pos": [19.9, 0, -9.5], "yaw": 20}]
corn = [{"pos": [19.6, 0, -2.5], "yaw": 0, "scale": 1.1}, {"pos": [8.0, 0, -10.6], "yaw": 40}, {"pos": [14.0, 0, 4.0], "yaw": 80, "scale": 1.2}, {"pos": [19.6, 0, 2.4], "yaw": 10}]
def multi(parts_ids, inst):
    return [{"id": pid, "src": "proc:" + src, "fit": {}, "inst": [dict(it, name=f"{pid}_{k}") for k, it in enumerate(inst)]} for pid, src in parts_ids]
cart = [{"name": "Cart_0", "pos": [10.6, 0, -12.4], "yaw": 30}]
parts = [keep("Soil", "Pp_Soil"), keep("Vine", "Pp_Vine"), keep("Leaf", "Pp_Leaf"),
         {"id": "Pumpkin", "src": "proc:Pp_Pumpkin", "fit": {}, "inst": pk}, {"id": "Stem", "src": "proc:Pp_Stem", "fit": {}, "inst": st},
         {"id": "Cart", "src": "proc:Pp_Cart", "fit": {}, "inst": cart}] + multi([("Bale", "Hb_Bale"), ("Twine", "Hb_Twine")], hay) + multi([("Stalk", "Cs_Stalk"), ("CLeaf", "Cs_Leaf"), ("Tie", "Cs_Tie")], corn)
for _p in parts:      # 南瓜田在墙外 8 米开外：玉米秆束、草垛减面
    if _p["id"] == "Stalk": _p["decimate"] = 2600
    if _p["id"] == "Bale": _p["decimate"] = 1800
ap = [["Pumpkin", "Soil"], ["Stem", "Pumpkin"], ["Stem", "Soil"], ["Leaf", "Soil"], ["Vine", "Soil"], ["Leaf", "Vine"], ["Leaf", "Pumpkin"], ["Vine", "Pumpkin"], ["Leaf", "Stem"], ["Pumpkin", "Cart"], ["Leaf", "Leaf"], ["Vine", "Stem"], ["Stem", "Cart"], ["Bale", "Twine"], ["Bale", "Bale"], ["Twine", "Twine"], ["Stalk", "CLeaf"], ["Stalk", "Tie"], ["CLeaf", "Tie"], ["CLeaf", "CLeaf"], ["Stalk", "Stalk"], ["CLeaf", "Soil"], ["Stalk", "Soil"]]
json.dump({"name": "Deco_PumpkinPatch", "allow_overlap": 0.02, "allow_pairs": ap, "parts": parts}, open(A / 'Deco_PumpkinPatch.json', 'w'), indent=1)
print(len(L['graves']), 'graves', len(cand), 'candles', len(pk), 'pumpkins')

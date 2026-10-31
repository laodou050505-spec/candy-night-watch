"""中文字体按用字子集化：python3 -I Tools/font_subset.py
收集 Assets/_Game/Localization 下 zh/en 全部文本 + ASCII + 常用标点，向 Google Fonts 请求只含这些字的
站酷快乐体（ZCOOL KuaiLe，OFL 许可），写到 Assets/_Game/Art/Fonts/ZCOOLKuaiLe-Subset.ttf。改了中文文案后要重跑。"""
import json, pathlib, re, string, subprocess, urllib.parse
ROOT = pathlib.Path(__file__).resolve().parents[1]
loc = ROOT / 'Assets' / '_Game' / 'Localization'
chars = set(string.printable.strip()) | set("·…—，。！？：、（）《》“”★☆●○◆◇♥ ")
for f in ('zh.json', 'en.json'):
    for v in json.loads((loc / f).read_text()).values(): chars |= set(v)
text = ''.join(sorted(c for c in chars if ord(c) >= 32))
url = 'https://fonts.googleapis.com/css?family=ZCOOL+KuaiLe&text=' + urllib.parse.quote(text)
css = subprocess.run(['curl', '-s', '-m', '60', '-A', 'Mozilla/4.0', url], capture_output=True, text=True).stdout
u = re.findall(r'url\((https://[^)]+)\)', css)
out = ROOT / 'Assets' / '_Game' / 'Art' / 'Fonts' / 'ZCOOLKuaiLe-Subset.ttf'
subprocess.run(['curl', '-s', '-m', '120', '-o', str(out), u[0]], check=True)
print('字符数', len(text), '→', out, out.stat().st_size, 'bytes')

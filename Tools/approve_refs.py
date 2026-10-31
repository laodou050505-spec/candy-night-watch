"""把审图通过的资产标记为 ref.ok（人工看过 ArtSource/Models/<名>/ref.png 之后运行）。
用法：python3 -I Tools/approve_refs.py 名称... """
import pathlib, sys
root = pathlib.Path(__file__).resolve().parents[1] / 'ArtSource' / 'Models'
for n in sys.argv[1:]:
    p = root / n
    if (p / 'ref.png').exists():
        (p / 'ref.ok').write_text('审图通过\n', encoding='utf-8')
        print('ok', n)
    else:
        print('缺 ref.png', n)

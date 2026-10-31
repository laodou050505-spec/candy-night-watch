"""《一二三·木头鬼》美术管线（纯 Python，调用 Tripo v3 接口；密钥由 tripo_image.py 从钥匙串读取，本脚本不接触明文）。

流程（每个资产一个目录 ArtSource/Models/<名称>/，state.json 记录进度，可随时中断、反复续跑）：
  1. ref       banana_pro 生成单件参考图（纯色背景、正面略俯视、完整不裁切）
  2. mv        /generation/image-to-multiview 生成四视图
  3. white     /generation/multiview-to-model（P2，texture=false）生成白模
  4. audit     Blender 审计白模（tools/mesh_audit.py）：非流形边、开放边界、碎片、法线、薄片；不合格 → 删 white 重来（换种子）
  5. tex       审计通过后 /models/texture 以四视图为参考上贴图（texture_quality=standard：不过高也不糊）
  6. 下载 model.glb，交给 Unity 导入

用法：
  python3 -I Tools/art_pipeline.py step [名称...]    推进一轮（不等待），打印每个资产当前阶段
  python3 -I Tools/art_pipeline.py run  [名称...]    循环推进直到全部完成或卡在需要人工判断的地方
  python3 -I Tools/art_pipeline.py list               列出资产与阶段
"""
import json, pathlib, subprocess, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'ArtSource' / 'Models'
BLENDER = '/Applications/Blender.app/Contents/MacOS/Blender'
AUDIT = ROOT / 'Tools' / 'mesh_audit.py'

STYLE_EYE = ('Stylized 3D cartoon game asset, cute whimsical Halloween storybook style, soft rounded chunky shapes, '
             'clean simple forms, hand-painted look with smooth gradients, single object, complete and fully visible, '
             'centered, camera at eye level looking straight at the side of the object (horizontal side elevation, no top-down tilt, the object stands upright and level), '
             'plain flat light grey background, no shadow on ground, no text, no other objects.')

STYLE = ('Stylized 3D cartoon game asset, cute whimsical Halloween storybook style, soft rounded chunky shapes, '
         'clean simple forms, hand-painted look with smooth gradients, single object, complete and fully visible, '
         'centered, three-quarter front view slightly from above, plain flat light grey background, no shadow on ground, '
         'no text, no other objects.')

# 名称 → (提示词, 面数上限, 分组)
SPECS = {
    # ---------------- 敌人（分部件动画，造型要可爱、轮廓清晰） ----------------
    'E1_Skelly': ('A chibi cute skeleton kid toy standing in a T-pose-like relaxed A-pose, oversized round skull with big black eye sockets and a tiny smile, '
                  'wearing half of a little orange pumpkin as a hat, a small tattered purple cape, chunky bones, big feet', 16000, 'enemy'),
    'E2_Batsy': ('A cute round fluffy bat ball creature with big pointy ears, two tiny fangs, big shiny red-pink eyes, '
                 'wings fully spread open flat and symmetrical, wing membranes purple, viewed from the front', 14000, 'enemy'),
    'E3_Dangle': ('A cute little round spider with a candy-striped orange and black round belly, eight thin bent legs spread evenly, '
                  'four big glossy green eyes, chubby and adorable', 12000, 'enemy'),
    'E4_Jack': ('A cute chibi pumpkin-head jester creature standing in A-pose, round carved jack-o-lantern head with triangle eyes and a jagged grin, '
                'a white ruffled clown collar, purple and green striped body suit, a two-pointed jester hat with gold bells, curly pointed shoes', 18000, 'enemy'),
    'E5_Mitten': ('A cute chubby black cat standing on four legs, big glowing yellow eyes, tiny pink nose, wearing a small purple witch hat with an orange band, '
                  'long curled tail up, side three-quarter view', 14000, 'enemy'),
    'E6_Boo': ('A cute chubby bedsheet ghost, round dome head, wavy hem at the bottom, two big black oval eyes and a small open mouth, '
               'two little stubby arms raised, soft white with slight lavender tint', 10000, 'enemy'),
    'E7_Frankenbear': ('A cute chunky patchwork teddy bear monster in A-pose, mint green fur with pink and purple fabric patches and visible stitches, '
                       'a metal bolt on each side of the neck, one red button eye and one X-stitched eye, stitched smile, round belly, big paws', 24000, 'enemy'),
    # ---------------- 场景主件 ----------------
    'S1_PumpkinTable': ('A round low table whose tabletop is a giant flat pumpkin slice: wide flat wooden-grain top, the rim made of a ring of plump orange pumpkin lobes, '
                        'four legs made of thick curly green pumpkin vines with spiral tendrils', 40000, 'scene'),
    'S2_CandyBucket': ('A grinning jack-o-lantern candy bucket with a black metal handle, open top filled to the brim with colorful wrapped candies', 20000, 'scene'),
    'S3_Cauldron': ('A small round black iron witch cauldron on three curly legs, two side ring handles, three large faceted gems (green, pink, purple) set on its front belly, '
                    'little bones hanging from the rim, open top with a flat potion surface', 20000, 'scene'),
    'S4_BookTower': ('A whimsical leaning spiral tower of stacked old spell books in purple, teal and orange covers, twisting upward like a crooked staircase, '
                     'a candle stub and a tiny skull on top', 30000, 'scene'),
    'S5_WitchBed': ('A cute small four-poster witch bed with a dark purple canopy and curtains tied back, pointed spire posts, an orange quilt with bat patterns, plump pillow', 36000, 'scene'),
    'S6_FangWardrobe': ('A cute cartoon monster wardrobe cabinet with two doors that look like a mouth with two big ivory fangs, two glowing yellow eyes carved on top, '
                        'small horns on the crown, curvy wooden legs, purple and dark wood', 26000, 'scene'),
    'S7_Coffin': ('A cute stylized wooden coffin standing upright, slightly cracked open lid with a gold cross, dark reddish wood, rounded edges', 16000, 'scene'),
    'S8_PumpkinPile': ('A cozy pile of three cute pumpkins of different sizes, one carved jack-o-lantern with a happy face, one tall pale pumpkin, one small orange pumpkin with a curly stem', 24000, 'scene'),
    'S9_Broom': ('A cute witch broomstick leaning, crooked wooden handle with a purple ribbon, fluffy straw bristles tied with twine', 12000, 'scene'),
    'S10_CandyWrapped': ('A single big shiny wrapped candy, round sweet in the middle with twisted wrapper ends on both sides, pink and white swirl wrapper', 6000, 'prop'),
    'S11_LollipopPumpkin': ('A cute pumpkin-shaped lollipop on a white stick with a little green leaf, glossy orange candy', 6000, 'prop'),
    'S12_Gravestone': ('A cute rounded cartoon gravestone with RIP carved and a small jack-o-lantern and moss at the base, soft grey stone', 10000, 'scene'),
    # ---------------- v3 定稿场景：月下浮空南瓜圣坛 ----------------
    'L1_ArchColumn': ('A single tall gothic stone pointed-arch pillar segment: a slender stone column wrapped by a giant twisting green pumpkin vine with curly tendrils and leaves, '
                      'a small pumpkin growing on top, half of a pointed arch springing from the top to one side, slightly ruined chipped stone, purple-grey stone', 30000, 'scene'),
    'L2_RoseWindow': ('A huge round gothic rose window frame made of dark purple stone tracery, twelve petal-shaped panes radiating from a central crescent moon emblem, '
                      'small bat and jack-o-lantern motifs in the tracery, front view, flat disc shape, panes left as solid flat glass', 30000, 'scene'),
    'L3_HauntedManor': ('A cute crooked haunted manor house on a small grassy rock base: leaning towers with pointy slate roofs, many arched windows, a chimney, a little porch with steps, '
                        'two carved pumpkins by the door, purple-grey walls and dark blue roofs, whimsical storybook', 45000, 'scene'),
    'L4_WitchCottage': ('A cute small witch cottage: round crooked walls, a tall droopy pointed thatched roof like a witch hat, a round door, a stone chimney, a little garden with mushrooms, '
                        'purple and orange colors, whimsical storybook', 40000, 'scene'),
    'L5_Scarecrow': ('A cute friendly scarecrow with a carved pumpkin head wearing a floppy brown hat, patched blue shirt, straw sticking out of sleeves, arms spread on a wooden cross post', 20000, 'scene'),
    'L6_BellTower': ('A small cute crooked gothic bell tower with a big brass bell visible in an open belfry, a pointy roof with a weather vane shaped like a bat, purple stone, small round clock face', 30000, 'scene'),
    'L7_FloatIsland': ('A large round floating island chunk seen from slightly above: flat top covered with purple-grey cobblestone paving in a circular pattern, '
                       'the underside a tapering rocky cone with hanging roots, little grass tufts on the rim, clean chunky shapes', 40000, 'scene'),
    'L8_RopeBridgePost': ('A pair of cute wooden rope-bridge posts with thick rope coils and a small hanging jack-o-lantern lantern on each post', 12000, 'scene'),
    'L9_MushroomLamp': ('A cute giant glowing mushroom lamp: tall curvy stem, wide spotted purple cap with glowing turquoise underside, tiny mushrooms at the base', 14000, 'scene'),
    'L10_CandleCluster': ('A cluster of five cute melting white candles of different heights with dripping wax on a little round stone dish', 10000, 'prop'),
    # ---------------- 万圣节符号强化（玩家视野里必须一眼认出的元素） ----------------
    'H1_JackLanternBig': ('A big classic carved jack-o-lantern pumpkin with a wide toothy grin and triangle eyes and nose, the carved holes glowing bright warm yellow-orange from a candle inside, '
                          'thick curly green stem with a leaf and a curly vine tendril, plump deep orange ribs', 18000, 'halloween'),
    'H2_SpookyTree': ('A twisted spooky bare Halloween tree with a thick gnarled trunk that has a cute carved face hollow, long curling crooked branches like fingers, '
                      'small hanging jack-o-lantern lanterns on strings from three branches, dark purple-brown bark, standing on a small mossy root base', 30000, 'halloween'),
    'H3_GraveyardSet': ('A cute small Halloween graveyard patch on a round grassy base: three rounded cartoon tombstones of different shapes with RIP and a cross, '
                        'a tiny crooked wrought-iron fence, a small carved pumpkin, a skeleton hand poking out of the ground waving', 26000, 'halloween'),
    'H4_WitchOnBroom': ('A cute chibi witch girl flying on a broomstick seen from the side, big droopy purple pointed witch hat with an orange band and buckle, '
                        'flowing dark purple dress and cape, striped orange and black stockings, a black cat sitting behind her on the broom, side profile view', 20000, 'halloween'),
    'H5_SkullCandelabra': ('A cute gothic candelabra with three arms holding melting white candles, a cartoon smiling skull in the center of the base, dark iron with curly ornaments', 14000, 'halloween'),
    'H6_BatLantern': ('A cute hanging iron lantern shaped like a little bat with spread wings, glowing warm yellow glass panes in its round body, a hook on top', 10000, 'halloween'),
    'H7_CandyCornPile': ('A small heap of giant candy corn pieces and wrapped candies and a striped lollipop, bright yellow orange white candy corn, glossy', 12000, 'halloween'),
    'H8_PotionShelf': ('A crooked little wooden witch shelf with glowing potion bottles in green, purple and pink, a jar with an eyeball, a small skull, a spider web in the corner', 22000, 'halloween'),
    # ---------------- v5：玩法桌（直径 2.3 米、高 0.72 米，宽而矮）、深坩埚 ----------------
    'S1W_PumpkinTableWide': ('A very wide and low round tabletop shaped like a giant flat pumpkin slice, the top is a broad perfectly flat smooth wooden disc with subtle wood grain, '
                             'about three times wider than its height, the rim is a ring of many small plump orange pumpkin lobes, '
                             'held up by six short thick curly green pumpkin-vine legs with spiral tendrils, low coffee-table proportions', 40000, 'scene'),
    'S3D_CauldronDeep': ('A cute round black iron witch cauldron on three short curly legs, wide open top with a thick rolled rim, the inside is a deep empty hollow bowl with no liquid, '
                         'two side ring handles, three large faceted gems (green, pink, purple) set on its front belly, little bones hanging from the rim', 20000, 'scene'),
    # ================= v6 分部件：桌面一套（每个部件单独生成、严格审计）=================
    'P_TableLeg': ('One single thick curly green pumpkin vine shaped like a furniture leg: a sturdy vertical stalk that curls into a spiral at the bottom foot, '
                   'with one small leaf and one thin spiral tendril attached, smooth chunky solid shape, isolated single part', 12000, 'part'),
    'P_TableRim': ('A closed donut ring made of many small plump orange pumpkin lobes in a circle, like the rim of a pumpkin, solid ring, hollow round center hole, '
                   'top view slightly tilted, isolated single part', 20000, 'part'),
    'P_BucketShell': ('A round plump cartoon jack-o-lantern pumpkin with a carved face (triangle eyes, triangle nose and a toothy grin carved as shallow relief), '
                      'the top is cut flat and closed like a lid, no stem, no handle, solid closed shape, isolated single part', 16000, 'part'),
    'P_CauldronBody': ('An empty round black iron witch cauldron pot body only, very deep hollow interior visible through the wide open top, thick rolled lip around the rim, '
                       'smooth round belly, no legs, no handles, no decorations, isolated single part', 12000, 'part'),
    'P_CauldronLeg': ('One single short curly black iron cauldron foot, a stubby leg that curls outward into a small scroll at the bottom, isolated single part', 4000, 'part'),
    'P_Bone': ('One single small cute cartoon dog bone, white ivory, rounded knobs at both ends, isolated single part', 3000, 'part'),
    'P_Skull': ('One single cute cartoon human skull, round cranium, big round dark eye sockets, small nose hole, a few rounded teeth, ivory white, front view, isolated single part', 8000, 'part'),
    'P_CandelabraFrame': ('A gothic wrought-iron three-arm candelabra frame only, round heavy base, central column, two curved side arms with small cup holders at the tips and one cup on top, '
                          'curly scroll ornaments, dark iron, no candles, no skull, isolated single part', 12000, 'part'),
    'P_CandyBall': ('One single round wrapped hard candy with twisted wrapper ends on both sides, pink and white swirl, glossy, isolated single part', 3000, 'part'),
    'P_CandyCorn': ('One single cartoon candy corn piece, plump rounded triangular shape with yellow base, orange middle and white tip bands, isolated single part', 2000, 'part'),
    'P_Lollipop': ('One single swirl lollipop, flat round spiral candy disc in orange and purple on a short white stick, isolated single part', 3000, 'part'),
    'P_CandyBar': ('One single small cartoon chocolate candy bar in an orange wrapper with crimped ends, isolated single part', 2500, 'part'),
    # ================= v7 花园布景道具（单件、严格审计；比程序化件细节丰富）=================
    'P_HayBale': ('One single rectangular straw hay bale, chunky cartoon shape with slightly bulging sides and rounded edges, two dark twine bands wrapped tightly around it and pressed into the straw, visible straw texture, golden yellow, isolated single part', 6000, 'part'),
    'P_CornShock': ('One single bundle of dried corn stalks tied together in the middle with a rope, teepee shape, tall dried stalks fanning out at the top with long curling dried leaves drooping down, spreading base, autumn tan and beige, chunky stylized, isolated single part', 14000, 'part'),
    'P_StoneUrn': ('One single classical stone garden urn on a square pedestal, wide fluted bowl with a rolled rim and two small scroll handles, weathered grey stone with green moss patches, filled with soil, isolated single part', 9000, 'part'),
    'P_Crow': ('One single cute cartoon black crow perched and standing, wings folded, big round glossy eyes, yellow beak, small feet, slightly tilted head, isolated single part', 6000, 'part'),
    'P_BlackCat': ('One single cute cartoon black cat sitting upright with its tail curled around its feet, big glowing yellow eyes, pointy ears, isolated single part', 7000, 'part'),
    'P_Owl': ('One single cute cartoon brown owl perched upright, big round orange eyes, ear tufts, folded wings, small feet, isolated single part', 6000, 'part'),
    'P_SheetGhost': ('One single cute cartoon bedsheet ghost floating, white cloth with wavy hem flowing down, two little arms raised, big black oval eyes and small open mouth, isolated single part', 6000, 'part'),
    'P_GiantSpider': ('One single big cute cartoon black spider with a round fuzzy body, eight thick bent legs, purple glowing eyes, isolated single part', 9000, 'part'),
    'P_Lantern': ('One single old wrought-iron hanging lantern with glass panes, ring handle on top, small candle inside, cartoon chunky, isolated single part', 6000, 'part'),
    'P_Barrel': ('One single old wooden barrel with dark iron hoops, slightly bulging, cartoon chunky, isolated single part', 5000, 'part'),
    'P_AppleCrate': ('One single wooden crate full of red apples, rough planks, cartoon chunky, isolated single part', 9000, 'part'),
    'P_GardenBench': ('One single Victorian garden bench, wooden seat slats and backrest with ornate dark cast-iron curly side frames, chunky cartoon proportions, isolated single part', 12000, 'part'),
    'P_PumpkinStack': ('One single stack of three pumpkins of decreasing size piled on top of each other, the top one a carved jack-o-lantern with a happy face, orange and cream colors, isolated single part', 9000, 'part'),
    'P_WitchHat': ('One single cartoon witch hat, wide floppy brim, tall bent pointed crown, purple with an orange band and a gold buckle, isolated single part', 4000, 'part'),
    'P_SkullPile': ('One single small pile of four cute cartoon skulls stacked together, ivory white, isolated single part', 8000, 'part'),
    'P_Crow2': ('One single cute cartoon black crow figurine standing on a small round flat base, wings folded tightly against a smooth plump body, big round eyes, short thick yellow beak, chunky simplified legs merged with the base, single solid closed watertight body, isolated single part', 6000, 'part'),
    'P_BlackCat2': ('One single cute cartoon black cat figurine sitting upright, smooth plump body, tail wrapped tightly around its front paws and fused to the body, big yellow eyes, small pointed ears, single solid closed watertight body with a flat bottom, isolated single part', 7000, 'part'),
}

EYE_LEVEL = {'S1W_PumpkinTableWide', 'S3D_CauldronDeep', 'P_BucketShell'}

DONE = ('success',)
BAD = ('failed', 'cancelled', 'banned', 'expired', 'unknown')


class Busy(Exception):
    pass


def post(path, body):
    try:
        return api.request_json('POST', path, body)['data']['task_id']
    except Exception as e:
        msg = str(e)
        if '429' in msg or 'limit' in msg.lower():
            raise Busy()
        raise


def fetch(url, dest):
    # 先下到临时文件，校验 Content-Length（GLB 再校验头部声明的总长度），通过后再改名；失败重试 3 次
    import struct
    dest = pathlib.Path(dest); tmp = dest.with_suffix(dest.suffix + '.part')
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url, timeout=120) as r:
                want = int(r.headers.get('Content-Length') or 0)
                data = r.read()
            if want and len(data) != want:
                raise IOError(f'short read {len(data)}/{want}')
            if dest.suffix == '.glb' and (data[:4] != b'glTF' or struct.unpack('<I', data[8:12])[0] != len(data)):
                raise IOError('bad glb length')
            tmp.write_bytes(data); tmp.replace(dest); return
        except Exception as e:
            print('fetch retry', attempt, e)
            time.sleep(3)
    raise IOError('download failed: ' + str(dest))


def advance(name):
    prompt, faces, group = SPECS[name]
    folder = OUT / name
    folder.mkdir(parents=True, exist_ok=True)
    sf = folder / 'state.json'
    st = json.loads(sf.read_text()) if sf.exists() else {}
    save = lambda: sf.write_text(json.dumps(st, indent=2, ensure_ascii=False))

    def poll(key):
        d = api.task(st[key])
        s = d.get('status')
        if s in DONE:
            st[key + '_result'] = d; save()
        elif s in BAD:
            st[key + '_error'] = d; del st[key]; save()
        return s

    if (folder / 'model.glb').exists():
        return 'done'
    # 1 参考图
    if not (folder / 'ref.png').exists():
        if 'ref' not in st:
            style = STYLE_EYE if name in EYE_LEVEL else STYLE
            st['ref'] = post('/generation/text-to-image', {'prompt': f'{prompt}. {style}', 'model': 'banana_pro', 'size': '2K',
                                                           'aspect_ratio': '1:1', 'output_format': 'png', 'watermark': False})
            save(); return 'ref-submitted'
        s = poll('ref')
        if s not in DONE:
            return 'ref-' + str(s)
        out = st['ref_result']['output']
        fetch(out.get('generated_image_url') or out.get('image_url'), folder / 'ref.png')
        return 'ref-done（待人工看图：确认后创建 ref.ok）'
    if not (folder / 'ref.ok').exists():
        return 'ref-review'
    # 2 四视图
    if 'mv_result' not in st:
        if 'mv' not in st:
            if 'ref_token' not in st:
                st['ref_token'] = api.upload_file(folder / 'ref.png'); save()
            st['mv'] = post('/generation/image-to-multiview', {'input': st['ref_token']}); save()
            return 'mv-submitted'
        s = poll('mv')
        if s not in DONE:
            return 'mv-' + str(s)
        out = st['mv_result']['output']
        # Tripo v3 把四视图嵌在 output.generate_multiview_image 里
        mv_out = out.get('generate_multiview_image') or out
        for k in ('front', 'left', 'back', 'right'):
            u = mv_out.get(k + '_view_url')
            if u:
                fetch(u, folder / f'view_{k}.png')
        return 'mv-done（待人工看四视图：确认后创建 mv.ok）'
    if not (folder / 'mv.ok').exists():
        return 'mv-review'
    # 3 白模
    if 'white_result' not in st:
        if 'white' not in st:
            seed = 2026102400 + list(SPECS).index(name) * 10 + st.get('white_retry', 0)
            body = {'inputs': [{'task_id': st['mv']}], 'model': 'P2-20260801', 'face_limit': faces, 'texture': False, 'pbr': False, 'model_seed': seed}
            st['white_request'] = body
            st['white'] = post('/generation/multiview-to-model', body); save()
            return 'white-submitted'
        s = poll('white')
        if s not in DONE:
            return 'white-' + str(s)
        out = st['white_result']['output']
        fetch(out.get('model_url') or out.get('model'), folder / 'white.glb')
        return 'white-done'
    # 4 审计
    audit = folder / 'audit.json'
    if not audit.exists():
        subprocess.run([BLENDER, '-b', '-P', str(AUDIT), '--', str(folder / 'white.glb'), str(audit), str(folder / 'white_views.png')] + (['strict'] if group == 'part' else []),
                       check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if not audit.exists():
            return 'audit-error'
    a = json.loads(audit.read_text())
    if not a.get('pass'):
        if (folder / 'white.ok').exists():
            pass  # 人工看过素模六视图后放行（例如有意的薄片：翅膀、斗篷）
        else:
            if st.get('white_retry', 0) < (4 if group == 'part' else 2) and not (folder / 'audit.hold').exists():
                st['white_retry'] = st.get('white_retry', 0) + 1
                for k in ('white', 'white_result'):
                    st.pop(k, None)
                save(); audit.unlink(); (folder / 'white.glb').unlink(missing_ok=True)
                return f'audit-fail→重做白模（第{st["white_retry"]}次）: {a.get("reason")}'
            return f'audit-fail（需人工）: {a.get("reason")}'
    # 5 贴图
    if 'tex_result' not in st:
        if 'tex' not in st:
            mv_out = st['mv_result']['output']
            mv_out = mv_out.get('generate_multiview_image') or mv_out
            images = [{k: mv_out[k + '_view_url']} for k in ('front', 'left', 'back', 'right') if mv_out.get(k + '_view_url')]
            body = {'input': st['white'], 'model': 'v3.0-20250812', 'texture_prompt': {'images': images},
                    'texture_quality': 'standard', 'pbr': False, 'texture_alignment': 'original_image', 'texture_seed': 31}
            st['tex'] = post('/models/texture', body); save()
            return 'tex-submitted'
        s = poll('tex')
        if s not in DONE:
            return 'tex-' + str(s)
    out = st['tex_result']['output']
    fetch(out.get('model_url') or out.get('pbr_model_url') or out.get('model'), folder / 'model.glb')
    if isinstance(out.get('rendered_image_url'), str):
        try:
            fetch(out['rendered_image_url'], folder / 'rendered.webp')
        except Exception:
            pass
    return 'done'


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'step'
    names = sys.argv[2:] or list(SPECS)
    if cmd == 'list':
        for n in SPECS:
            f = OUT / n
            print(n, 'done' if (f / 'model.glb').exists() else ('state' if (f / 'state.json').exists() else '-'))
        return
    while True:
        busy = False
        stages = {}
        for n in names:
            try:
                stages[n] = advance(n)
            except Busy:
                stages[n] = 'busy'; busy = True
            except Exception as e:  # 单个失败不影响其他资产
                stages[n] = f'error: {e}'[:160]
        for n, s in stages.items():
            print(f'{n:22s} {s}', flush=True)
        if cmd != 'run':
            return
        if all(s == 'done' or s.endswith('review') or 'need' in s or '人工' in s or s.startswith('error') for s in stages.values()):
            return
        print('-' * 40, flush=True)
        time.sleep(25 if busy else 15)


if __name__ == '__main__':
    main()

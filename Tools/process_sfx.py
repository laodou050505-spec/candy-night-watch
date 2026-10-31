"""音效整理 + 补缺：python3 Tools/process_sfx.py
1) ArtSource/Audio/sfx/ 里从 ange-embed 挑来的素材（manifest.json）：解码 → 切掉开头静音 → 截到合适长度并淡出 →
   按响度统一（RMS 目标 -18 dBFS，峰值不超过 -1 dB）→ 写成 44.1k 单声道 WAV 到 Assets/_Game/Audio/Sfx/<id>_<n>.wav。
2) 素材库里没有的（卡通角色叫声、夜晚虫鸣循环、坩埚冒泡循环、合十）离线合成，风格是卡通、可爱，不吓人。
文件名约定 <id>_<序号>：GameplayBuilder.BuildAudio 按 id 分组成多变体音效组。"""
import json, math, subprocess, tempfile, wave
from pathlib import Path
import numpy as np
import importlib.util

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'ArtSource' / 'Audio' / 'sfx'
OUT = ROOT / 'Assets' / '_Game' / 'Audio' / 'Sfx'
SR = 44100
spec = importlib.util.spec_from_file_location('st', ROOT / 'Tools' / 'make_stingers.py'); st = importlib.util.module_from_spec(spec); spec.loader.exec_module(st)

# 每个 id 的最长时长（秒）；没列的不截
MAXLEN = {'freeze': 1.3, 'warn': .8, 'slip': 1.0, 'grab': .6, 'boxed': .9, 'gulp': .6, 'pull': 1.2, 'whoosh': 1.1, 'thud': .5, 'step': .35,
          'bone': .4, 'steal': .7, 'candy_drop': .6, 'candy_back': 1.2, 'lost': 1.3, 'charged': 1.5, 'star': 1.8, 'dizzy': 1.6, 'pat': .5,
          'wave': 2.0, 'sweep': 1.6, 'lamp': 1.6, 'shield': 2.0, 'ui_hover': .2, 'ui_click': .25, 'ui_back': .6, 'voice_cat': 1.4,
          'amb_owl': 2.0, 'amb_wolf': 4.0, 'amb_gate': 2.4}
SKIP = {'pray', 'bubble_loop'}      # 素材不合适（太短 / 不能循环），改用下面合成的


def load(path):
    with tempfile.TemporaryDirectory() as td:
        w = Path(td) / 'x.wav'
        subprocess.run(['afconvert', '-f', 'WAVE', '-d', f'LEI16@{SR}', '-c', '1', str(path), str(w)], check=True, capture_output=True)
        with wave.open(str(w)) as f:
            return np.frombuffer(f.readframes(f.getnframes()), np.int16).astype(np.float32) / 32768


def save(name, x):
    OUT.mkdir(parents=True, exist_ok=True)
    x = np.clip(x, -1, 1)
    with wave.open(str(OUT / name), 'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(SR)
        f.writeframes((x * 32767).astype(np.int16).tobytes())


def tidy(x, maxlen=None, target_db=-18, fade=0.06):
    # 切开头静音（-45 dB 以下）
    thr = 10 ** (-45 / 20)
    nz = np.nonzero(np.abs(x) > thr)[0]
    if len(nz): x = x[max(0, nz[0] - int(SR * .005)):]
    # 切尾巴静音
    nz = np.nonzero(np.abs(x) > thr)[0]
    if len(nz): x = x[:nz[-1] + int(SR * .02)]
    if maxlen and len(x) > maxlen * SR:
        x = x[:int(maxlen * SR)].copy()
        f = int(min(fade * 2.5, maxlen * .3) * SR); x[-f:] *= np.linspace(1, 0, f) ** 2
    else:
        f = int(.01 * SR); x[-f:] *= np.linspace(1, 0, f)
    # 响度统一
    rms = math.sqrt(float(np.mean(x ** 2))) + 1e-9
    g = 10 ** (target_db / 20) / rms
    g = min(g, 0.89 / (np.max(np.abs(x)) + 1e-9))
    return x * g


def import_library():
    m = json.loads((SRC / 'manifest.json').read_text())
    n = 0
    for e in m:
        f = e.get('file')
        if not f: continue
        stem = Path(f).stem
        bid = stem.rsplit('_', 1)[0]
        if bid in SKIP: continue
        x = tidy(load(SRC / f), MAXLEN.get(bid))
        save(stem + '.wav', x); n += 1
    print('  素材音效', n, '个')


# ---------------- 合成 ----------------
def formant_voice(f0_curve, formants, dur, breath=0.05):
    """声带脉冲 + 共振峰滤波：卡通发声。f0_curve(t) → 基频；formants = [(中心, 带宽, 增益)]"""
    n = int(SR * dur); t = np.arange(n) / SR
    f0 = f0_curve(t)
    ph = np.cumsum(2 * np.pi * f0 / SR)
    src = np.zeros(n, np.float32)
    for k in range(1, 30):   # 带限脉冲（锯齿谐波叠加）
        src += np.where(f0 * k < SR / 2.2, np.sin(k * ph) / k, 0)
    src += np.random.default_rng(3).normal(0, breath, n)
    out = np.zeros(n, np.float32)
    for fc, bw, gain in formants:   # 二阶带通（双二次）
        w0 = 2 * np.pi * fc / SR; alpha = math.sin(w0) * math.sinh(math.log(2) / 2 * bw / fc * w0 / math.sin(w0))
        b0, b2, a0, a1, a2 = alpha, -alpha, 1 + alpha, -2 * math.cos(w0), 1 - alpha
        y = np.zeros(n, np.float32); x1 = x2 = y1 = y2 = 0.0
        for i in range(n):
            xi = src[i]
            yi = (b0 * xi + b2 * x2 - a1 * y1 - a2 * y2) / a0
            x2, x1 = x1, xi; y2, y1 = y1, yi; y[i] = yi
        out += y * gain
    return out


def syllables(starts, length, dur):
    """一串“音节”的音量包络"""
    n = int(SR * dur); t = np.arange(n) / SR; e = np.zeros(n, np.float32)
    for s in starts:
        k = (t - s) / length
        e += np.where((k >= 0) & (k <= 1), np.sin(np.pi * np.clip(k, 0, 1)) ** 0.7, 0)
    return np.clip(e, 0, 1)


def giggle(base=520, n=5, step=0.105, vowel='i', seed=1):
    dur = n * step + .25
    rng = np.random.default_rng(seed)
    pitches = base * (1 + 0.08 * rng.standard_normal(n)) * np.linspace(1.1, .92, n)
    def f0(t):
        i = np.clip((t / step).astype(int), 0, n - 1)
        return pitches[i] * (1 + .04 * np.sin(2 * np.pi * 9 * t))
    F = {'i': [(380, 90, 1), (2300, 180, .6), (3000, 250, .3)], 'e': [(500, 100, 1), (1900, 160, .55), (2600, 220, .3)], 'u': [(330, 80, 1), (800, 120, .5), (2300, 200, .15)]}[vowel]
    v = formant_voice(f0, F, dur, .08)
    return v * syllables([i * step for i in range(n)], step * .8, dur)


def ghost_woo(seed=1):
    dur = 1.5; t = np.arange(int(SR * dur)) / SR
    f0 = lambda t: 300 * (1 + .35 * np.sin(np.pi * np.clip(t / dur, 0, 1))) * (1 + .03 * np.sin(2 * np.pi * 6 * t))
    v = formant_voice(f0, [(330, 70, 1), (780, 110, .45)], dur, .03)
    e = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.2
    return st.reverb(v * e, .35)[:int(SR * 1.9)]


def bat_squeak(seed=1):
    rng = np.random.default_rng(seed); dur = .45; x = np.zeros(int(SR * dur), np.float32)
    for k in range(3 + seed % 2):
        s = .03 + k * .11 + rng.uniform(-.01, .01); L = .045
        t = np.arange(int(SR * L)) / SR
        f = 5200 - 2400 * t / L + rng.uniform(-300, 300)
        sig = np.sin(np.cumsum(2 * np.pi * f / SR)) * np.sin(np.pi * t / L) ** 2
        st.add(x, s, sig.astype(np.float32), .8)
    return x


def spider_skitter(seed=1):
    rng = np.random.default_rng(seed); dur = .7; x = np.zeros(int(SR * dur), np.float32)
    t = .02
    while t < dur - .03:
        n = int(SR * .006); click = rng.normal(0, 1, n) * np.exp(-np.arange(n) / (SR * .0015))
        # 高通一下：细碎的“哒哒”
        click = np.diff(np.concatenate([[0], click])).astype(np.float32)
        st.add(x, t, click, rng.uniform(.3, .8))
        t += rng.uniform(.018, .045)
    return x


def frank_hmm(seed=1):
    dur = 1.0
    f0 = lambda t: 105 * (1 - .18 * np.clip(t / dur, 0, 1)) * (1 + .02 * np.sin(2 * np.pi * 5 * t))
    v = formant_voice(f0, [(400, 90, 1), (900, 120, .5), (2400, 220, .12)], dur, .06)
    t = np.arange(len(v)) / SR
    return v * np.minimum(1, t / .08) * np.clip((dur - t) / .35, 0, 1)


def boss_growl():
    dur = 1.8; n = int(SR * dur); t = np.arange(n) / SR
    f0 = 70 * (1 + .25 * np.sin(np.pi * np.clip(t / dur, 0, 1)))
    ph = np.cumsum(2 * np.pi * f0 / SR); saw = ((ph / np.pi) % 2 - 1)
    rum = np.random.default_rng(7).normal(0, 1, n); y = np.zeros(n, np.float32); a = 0.0
    for i in range(n): a += .02 * (rum[i] - a); y[i] = a
    g = saw * (1 + .5 * np.sin(2 * np.pi * 23 * t)) * .6 + y * 3
    out = np.zeros(n, np.float32); a = 0.0
    for i in range(n): a += .06 * (g[i] - a); out[i] = a
    e = np.minimum(1, t / .15) * np.clip((dur - t) / .6, 0, 1)
    thump = st.pluck(55, .8)
    x = out * e; st.add(x, 0, thump, 1.5)
    return st.reverb(x, .25)[:int(SR * 2.2)]


def crickets_loop(dur=24.0):
    """夜虫 + 微风，首尾无缝：所有成分都用周期能整除 dur 的调制"""
    n = int(SR * dur); t = np.arange(n) / SR
    x = np.zeros(n, np.float32)
    rng = np.random.default_rng(11)
    for k in range(5):     # 5 只蟋蟀：不同音高、不同节奏，左右远近不同（单声道里只体现响度）
        fc = rng.uniform(4200, 5600)
        rate = round(rng.uniform(1.6, 3.2) * dur) / dur            # 每秒几阵，整除总长
        chirp = round(rng.uniform(14, 22) * dur) / dur              # 一阵里的颤音
        gate = (np.sin(2 * np.pi * rate * t + rng.uniform(0, 6)) > .35).astype(np.float32)
        gate = np.convolve(gate, np.ones(200) / 200, 'same')
        trem = .5 + .5 * np.sin(2 * np.pi * chirp * t)
        x += np.sin(2 * np.pi * fc * t) * gate * trem * rng.uniform(.04, .1)
    # 微风：循环噪声 → 低通 → 慢起伏
    w = rng.normal(0, 1, n).astype(np.float32)
    W = np.fft.rfft(w); f = np.fft.rfftfreq(n, 1 / SR); W *= 1 / (1 + (f / 500) ** 2); w = np.fft.irfft(W, n).astype(np.float32)
    w *= .6 + .4 * np.sin(2 * np.pi * (2 / dur) * t)
    x += w / (np.std(w) + 1e-9) * .05
    return x


def bubble_loop(dur=6.0):
    n = int(SR * dur); x = np.zeros(n + SR, np.float32); rng = np.random.default_rng(5)
    t = 0.0
    while t < dur:
        L = rng.uniform(.04, .09); m = int(SR * L); tt = np.arange(m) / SR
        f = rng.uniform(250, 600) * (1 + 2.5 * tt / L)
        b = np.sin(np.cumsum(2 * np.pi * f / SR)) * np.exp(-tt / (L * .4))
        i = int(t * SR); x[i:i + m] += b * rng.uniform(.3, .8)
        t += rng.uniform(.05, .22)
    x[:SR] += x[n:n + SR]          # 尾巴折回开头：首尾无缝
    x = x[:n]
    lp = np.zeros(n, np.float32); a = 0.0
    for i in range(n): a += .35 * (x[i] - a); lp[i] = a
    return lp


def pray_chime():
    x = st.buf(2.6)
    for f, g in ((st.note('G4'), .5), (st.note('D5'), .4), (st.note('G5'), .35), (st.note('B5'), .3)):
        st.add(x, 0, st.celesta(f, 2.4, .8), g)
    st.add(x, 0, st.pad([st.note('G3'), st.note('D4'), st.note('B4')], 2.5, .25, 1.2), .4)
    for i in range(6): st.add(x, .08 + i * .06, st.musicbox(st.note('D6') * 2 ** (i / 12 * 2), .5), .12)
    return st.reverb(x, .35)


def synth_all():
    jobs = {
        'giggle_1': lambda: giggle(560, 5, .1, 'i', 1), 'giggle_2': lambda: giggle(620, 4, .095, 'e', 2), 'giggle_3': lambda: giggle(500, 6, .09, 'i', 3),
        'voice_pumpkin_1': lambda: giggle(230, 4, .14, 'e', 4), 'voice_pumpkin_2': lambda: giggle(200, 3, .16, 'u', 5),
        'voice_ghost_1': lambda: ghost_woo(1), 'voice_ghost_2': lambda: ghost_woo(2) * .9,
        'voice_bat_1': lambda: bat_squeak(1), 'voice_bat_2': lambda: bat_squeak(2), 'voice_bat_3': lambda: bat_squeak(3),
        'voice_spider_1': lambda: spider_skitter(1), 'voice_spider_2': lambda: spider_skitter(2),
        'voice_frank_1': frank_hmm, 'boss_1': boss_growl,
        'pray_1': pray_chime,
    }
    for name, fn in jobs.items():
        save(name + '.wav', tidy(fn(), None, -18)); print('  合成', name)
    # 循环：不切静音、不加淡出，只统一响度
    for name, fn, db in (('amb_night_loop_1', crickets_loop, -30), ('bubble_loop_1', bubble_loop, -24)):
        x = fn(); x = x * (10 ** (db / 20) / (math.sqrt(float(np.mean(x ** 2))) + 1e-9)); save(name + '.wav', x); print('  合成循环', name)


if __name__ == '__main__':
    for f in OUT.glob('*.wav'): f.unlink()
    import_library()
    synth_all()

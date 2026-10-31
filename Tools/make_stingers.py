"""短乐句（离线渲染成 WAV，进 Unity 当普通音频用）：python3 Tools/make_stingers.py
素材库里找不到贴切的万圣节短乐句，就自己合成：钢片琴 / 八音盒（加非整数泛音的铃声）+ 拨弦 + 暖色长音垫，最后过一层小房间混响。
  stinger_dawn.wav   天亮了：C 大调钢片琴琶音上行 → 和弦闪光收尾（约 3.6 秒）
  stinger_fail.wav   糖被偷光：小调下行 + 低音管式“哇哇”滑音（约 2.8 秒）
  stinger_night.wav  一夜开始：八音盒奏的俏皮小调动机 + 拨弦低音（约 2.6 秒）
  stinger_skill.wav  新本事介绍：两声亮晶晶的上行铃（约 1.2 秒）"""
import math, wave
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets' / '_Game' / 'Audio' / 'Music'
SR = 44100


def note(n):
    """音名 → 频率，如 'C5'、'F#4'、'Eb4'"""
    names = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
    k = names[n[0]]; i = 1
    if n[i] == '#': k += 1; i += 1
    elif n[i] == 'b': k -= 1; i += 1
    octv = int(n[i:])
    return 440.0 * 2 ** ((k + 12 * (octv + 1) - 69) / 12)


def buf(sec):
    return np.zeros(int(SR * sec), np.float32)


def add(dst, t, sig, gain=1.0):
    i = int(t * SR); j = min(len(dst), i + len(sig))
    if j > i: dst[i:j] += sig[:j - i] * gain


def env_ad(n, a, d):
    t = np.arange(n) / SR
    e = np.minimum(1, t / max(a, 1e-4)) * np.exp(-t / d)
    return e.astype(np.float32)


def celesta(f, dur=1.6, bright=1.0):
    """钢片琴：基音 + 4 倍、略失谐的高泛音（敲击感），快起慢落"""
    n = int(SR * dur); t = np.arange(n) / SR
    s = np.sin(2 * np.pi * f * t) + 0.35 * bright * np.sin(2 * np.pi * f * 4.01 * t) * np.exp(-t / 0.18) + 0.12 * np.sin(2 * np.pi * f * 2 * t)
    return (s * env_ad(n, 0.003, dur * 0.35)).astype(np.float32)


def musicbox(f, dur=1.2):
    """八音盒：更尖更短，带 5.4 倍非整数泛音（金属簧片）"""
    n = int(SR * dur); t = np.arange(n) / SR
    s = np.sin(2 * np.pi * f * t) + 0.5 * np.sin(2 * np.pi * f * 5.4 * t) * np.exp(-t / 0.08) + 0.2 * np.sin(2 * np.pi * f * 3 * t) * np.exp(-t / 0.3)
    return (s * env_ad(n, 0.002, dur * 0.28)).astype(np.float32)


def pluck(f, dur=0.6):
    """拨弦（Karplus-Strong）"""
    n = int(SR * dur); p = max(2, int(SR / f))
    rng = np.random.default_rng(int(f))
    b = rng.uniform(-1, 1, p).astype(np.float32); out = np.zeros(n, np.float32)
    for i in range(n):
        out[i] = b[i % p]
        b[i % p] = 0.5 * (b[i % p] + b[(i + 1) % p]) * 0.994
    return out * env_ad(n, 0.002, dur * 0.5)


def pad(freqs, dur, a=0.4, r=0.8):
    """暖色长音垫：几个略失谐的三角波叠加 + 起落包络"""
    n = int(SR * dur); t = np.arange(n) / SR
    s = np.zeros(n, np.float32)
    for f in freqs:
        for det in (-0.004, 0.004):
            ph = 2 * np.pi * f * (1 + det) * t
            s += (2 / np.pi) * np.arcsin(np.sin(ph)).astype(np.float32)
    e = np.minimum(1, t / a) * np.minimum(1, (dur - t) / r).clip(0, 1)
    return (s / (2 * len(freqs)) * e).astype(np.float32)


def wah(f0, f1, dur):
    """卡通“哇~”：锯齿波滑音 + 低通，模拟低音管/长号"""
    n = int(SR * dur); t = np.arange(n) / SR
    f = f0 * (f1 / f0) ** (t / dur)
    ph = np.cumsum(2 * np.pi * f / SR)
    saw = (ph / np.pi) % 2 - 1
    # 单极点低通 + 颤音
    out = np.zeros(n, np.float32); y = 0.0; k = 0.08
    for i in range(n):
        y += k * (saw[i] - y); out[i] = y
    vib = 1 + 0.06 * np.sin(2 * np.pi * 5.5 * t)
    return (out * vib * np.minimum(1, t / 0.04) * np.minimum(1, (dur - t) / 0.15).clip(0, 1)).astype(np.float32)


def reverb(x, mix=0.28, room=0.82):
    """小型 Schroeder 混响：4 个并联梳状 + 2 个串联全通"""
    out = np.zeros(len(x) + SR, np.float32); xin = np.concatenate([x, np.zeros(SR, np.float32)])
    for d, g in ((1557, room), (1617, room - .01), (1491, room - .02), (1422, room - .03)):
        y = np.zeros_like(xin)
        for i in range(len(xin)):
            y[i] = xin[i] + (g * y[i - d] if i >= d else 0)
        out += y * 0.25
    for d, g in ((225, 0.5), (556, 0.5)):
        y = np.zeros_like(out)
        for i in range(len(out)):
            xd = out[i - d] if i >= d else 0; yd = y[i - d] if i >= d else 0
            y[i] = -g * out[i] + xd + g * yd
        out = y
    return (np.concatenate([x, np.zeros(SR, np.float32)]) * (1 - mix) + out * mix).astype(np.float32)


def save(name, mono, pan_spread=0.25):
    mono = reverb(mono)
    # 结尾 0.3 秒淡出，去掉混响尾巴的突然截断
    fade = int(SR * 0.3); mono[-fade:] *= np.linspace(1, 0, fade)
    peak = np.max(np.abs(mono)) + 1e-6; mono = mono / peak * 0.8
    # 轻微立体声：右声道延迟 7ms
    d = int(SR * 0.007); r = np.concatenate([np.zeros(d, np.float32), mono[:-d]])
    st = np.stack([mono, mono * (1 - pan_spread) + r * pan_spread], 1)
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / name), 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((st * 32767).astype(np.int16).tobytes())
    print('  ', name, f'{len(st) / SR:.1f}s')


def dawn():
    x = buf(3.6)
    for i, n in enumerate(['C5', 'E5', 'G5', 'C6', 'E6']):
        add(x, i * 0.13, celesta(note(n), 1.8), 0.55)
    for n in ['C6', 'E6', 'G6']:
        add(x, 0.75, celesta(note(n), 2.4, 1.3), 0.32)
    add(x, 0.75, musicbox(note('C7'), 1.6), 0.18)
    add(x, 0.0, pad([note('C4'), note('G4'), note('E5')], 3.4, 0.5, 1.4), 0.35)
    add(x, 0.75, pluck(note('C3'), 1.2), 0.5)
    save('stinger_dawn.wav', x)


def fail():
    x = buf(2.8)
    for i, n in enumerate(['G4', 'F#4', 'F4']):
        add(x, i * 0.28, celesta(note(n), 1.0, 0.6), 0.5)
    add(x, 0.84, wah(note('E3'), note('C3') * 0.94, 1.4), 0.55)
    add(x, 0.0, pad([note('C3'), note('Eb4')], 2.6, 0.3, 1.0), 0.25)
    save('stinger_fail.wav', x)


def night():
    x = buf(2.6)
    motif = [('C5', 0), ('Eb5', .16), ('G5', .32), ('F#5', .56), ('G5', .7), ('C6', .9), ('Bb5', 1.2), ('G5', 1.36)]
    for n, t in motif:
        add(x, t, musicbox(note(n), 1.0), 0.5)
    for n, t in (('C3', 0), ('G2', .56), ('Ab2', .9), ('G2', 1.2)):
        add(x, t, pluck(note(n), 0.7), 0.55)
    add(x, 0.0, pad([note('C4'), note('Eb4'), note('G4')], 2.4, 0.6, 1.0), 0.18)
    save('stinger_night.wav', x)


def skill():
    x = buf(1.3)
    add(x, 0.0, celesta(note('G5'), 1.0), 0.6)
    add(x, 0.12, celesta(note('D6'), 1.1, 1.3), 0.6)
    add(x, 0.12, musicbox(note('G6'), 0.9), 0.2)
    save('stinger_skill.wav', x)


if __name__ == '__main__':
    dawn(); fail(); night(); skill()

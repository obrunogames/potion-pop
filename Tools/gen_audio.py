#!/usr/bin/env python3
"""Procedural audio for Potion Pop! — every sound is synthesized here (no samples).

    uv run --with pillow --with numpy --with scipy python Tools/gen_audio.py [--only sfx|music|<name>]

SFX   -> Assets/_Game/Resources/Audio/sfx_<name>.wav   mono 16-bit 44.1 kHz, peak -1 dBFS, DC removed,
         smooth attack/release (no clicks), short reverb. One file per value of PotionPop.Sfx (snake_case).
MUSIC -> Assets/_Game/Resources/Audio/music_<name>.ogg stereo Vorbis q5, seamless loops (~38-44 s), about -16 LUFS.
         Songs are written as chord progressions + melodies (note lists below), humanized, mixed with stereo width
         and a convolution reverb. Seamless looping: the loop is rendered with an extra tail and the tail is folded
         back onto the start, so the file is exactly periodic (what plays after the end is what the start expects).

Vorbis encoding uses ffmpeg with libvorbis when available (/opt/homebrew/bin/ffmpeg or PATH); otherwise libsndfile's
libvorbis through the `soundfile` package at the same quality (q5), installed on the fly with `uv run --with soundfile`
if needed (ffmpeg's built-in "vorbis" encoder is experimental and is never used).

Style: cute, glossy, candy-like — soft bubbly pops, marimba / glockenspiel / kalimba-like modal plucks, sparkly
chimes, warm whooshes from filtered noise.
"""
import argparse
import math
import os
import shutil
import subprocess
import sys
import tempfile

import numpy as np
from scipy import signal
from scipy.io import wavfile
from scipy.ndimage import maximum_filter1d, minimum_filter1d, uniform_filter1d

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Game", "Resources", "Audio")
SR = 44100
SFX_PEAK_DB = -1.0
MUSIC_LUFS = -16.0
MUSIC_CEILING_DB = -1.5      # sample-peak ceiling before encoding (leaves room for codec overshoot)

# Must match PotionPop.Sfx (Assets/_Game/Scripts/Core) in snake_case.
SFX_NAMES = [
    "click", "popup_open", "popup_close", "pick", "drop", "invalid", "match", "combo", "star", "coin",
    "layer_reveal", "unlock", "win", "lose", "timer_tick", "freeze", "hammer", "wand", "shuffle", "bomb",
    "chest_open", "spin_tick", "spin_win", "reward", "heart", "whoosh", "card_flip", "toggle", "error",
    "purchase", "pop", "sparkle", "countdown", "fanfare", "swoosh",
]


# ============================================================================================ basics

NOTE_INDEX = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi(name):
    """'C4' -> 60, 'F#5' -> 78, 'Bb3' -> 58."""
    letter = name[0].upper()
    i = 1
    acc = 0
    while i < len(name) and name[i] in "#b":
        acc += 1 if name[i] == "#" else -1
        i += 1
    octave = int(name[i:])
    return 12 * (octave + 1) + NOTE_INDEX[letter] + acc


def hz(note):
    m = midi(note) if isinstance(note, str) else note
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def db(x):
    return 20 * math.log10(max(x, 1e-12))


def samples(sec):
    return max(1, int(round(sec * SR)))


def taxis(n):
    return np.arange(n) / SR


def fade(x, fin=0.002, fout=0.01):
    """Raised-cosine fades (in place) — guarantees no clicks at both ends."""
    n = len(x)
    a = min(samples(fin), n // 2)
    b = min(samples(fout), n // 2)
    shape = (-1, 1) if x.ndim == 2 else (-1,)
    if a > 0:
        x[:a] *= (0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))).reshape(shape)
    if b > 0:
        x[-b:] *= (0.5 + 0.5 * np.cos(np.linspace(0, np.pi, b))).reshape(shape)
    return x


def place(buf, sig, t, gain=1.0):
    """Mixes sig into buf starting at t seconds (mono or stereo, clipped to the buffer)."""
    i = int(round(t * SR))
    if i >= len(buf):
        return
    if i < 0:
        sig = sig[-i:]
        i = 0
    n = min(len(sig), len(buf) - i)
    buf[i:i + n] += sig[:n] * gain


def sos_filter(x, kind, freq, order=2):
    sos = signal.butter(order, freq, btype=kind, fs=SR, output="sos")
    return signal.sosfilt(sos, x, axis=0)


def exp_env(n, tau, attack=0.001):
    t = taxis(n)
    env = np.exp(-t / max(tau, 1e-4))
    a = min(samples(attack), n)
    if a > 1:
        env[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    return env


def adsr(n, a, d, s, r, gate):
    """Linear-segment ADSR (seconds) over n samples, note-off at gate seconds."""
    t = taxis(n)
    env = np.where(t < a, t / max(a, 1e-4), s + (1 - s) * np.exp(-(t - a) / max(d, 1e-4)))
    rel = t >= gate
    if rel.any():
        level = np.interp(gate, t, env)
        env[rel] = level * np.exp(-(t[rel] - gate) / max(r, 1e-4))
    return env


# ============================================================================================ instruments

def modal(f, ratios, amps, taus, length, attack=0.0015, rng=None, detune=0.0, phase_rand=True):
    """Sum of exponentially decaying sine modes (mallet instruments, bells, tines)."""
    n = samples(length)
    t = taxis(n)
    out = np.zeros(n)
    for r, a, tau in zip(ratios, amps, taus):
        fk = f * r
        if fk >= SR * 0.45 or a <= 0:
            continue
        m = min(n, samples(tau * 10))           # e^-10: -87 dB, then a short fade (no truncation click)
        ph = rng.uniform(0, 2 * np.pi) if (rng is not None and phase_rand) else 0.0
        part = a * np.exp(-t[:m] / tau) * np.sin(2 * np.pi * fk * (1 + detune) * t[:m] + ph)
        out[:m] += fade(part, 0, min(0.005, tau))
    a_n = min(samples(attack), n)
    if a_n > 1:
        out[:a_n] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a_n))
    # If the requested length cuts the ring short, damp it smoothly (like a hand on the bar) instead of clicking.
    return fade(out, 0, min(0.15, length * 0.3))


def mallet_noise(length, cutoff, rng, tau=0.003):
    n = samples(length)
    x = rng.standard_normal(n) * exp_env(n, tau, 0.0003)
    return sos_filter(x, "low", min(cutoff, SR * 0.45))


def marimba(f, vel=1.0, rng=None, length=None):
    tau1 = float(np.clip(0.55 * (400.0 / f) ** 0.6, 0.10, 1.1))
    length = length or tau1 * 5
    bright = 0.6 + 0.4 * vel
    x = modal(f, [1.0, 3.93, 9.24], [1.0, 0.30 * bright, 0.08 * bright], [tau1, tau1 / 4.5, tau1 / 10], length,
              rng=rng)
    x[:samples(0.01)] += mallet_noise(0.01, 2500 + 2500 * vel, rng) * 0.12 * vel
    return x * vel


def glock(f, vel=1.0, rng=None, length=None):
    tau1 = float(np.clip(1.3 * (1000.0 / f) ** 0.35, 0.25, 2.0))
    length = length or min(tau1 * 4, 3.0)
    x = modal(f, [1.0, 2.756, 5.404, 8.933], [1.0, 0.42, 0.20, 0.08], [tau1, tau1 * 0.35, tau1 * 0.16, tau1 * 0.08],
              length, attack=0.0008, rng=rng)
    x[:samples(0.006)] += mallet_noise(0.006, 9000, rng, tau=0.0012) * 0.08 * vel
    return x * vel


def kalimba(f, vel=1.0, rng=None, length=None):
    tau1 = float(np.clip(0.9 * (500.0 / f) ** 0.4, 0.2, 1.4))
    length = length or tau1 * 4
    x = modal(f, [1.0, 2.0, 5.93, 9.1], [1.0, 0.05, 0.22, 0.05], [tau1, tau1 / 3, tau1 / 7, tau1 / 12], length,
              attack=0.002, rng=rng)
    x[:samples(0.008)] += mallet_noise(0.008, 1800, rng, tau=0.002) * 0.15 * vel
    return x * vel


def bell(f, vel=1.0, rng=None, length=1.2, ratio=3.5, index=2.2):
    """FM bell: carrier + inharmonic modulator with decaying index (bright attack, pure tail)."""
    n = samples(length)
    t = taxis(n)
    idx = index * np.exp(-t / (length * 0.18))
    x = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * ratio * t)) * exp_env(n, length * 0.3, 0.001)
    return fade(x, 0, min(0.15, length * 0.3)) * vel


def uke(f, dur, vel=1.0, rng=None):
    """Nylon-string pluck: additive harmonics with a pluck-position spectrum and frequency-dependent decay."""
    ring = dur + 0.25
    n = samples(ring)
    t = taxis(n)
    K = int(min(24, 9000 // f))
    out = np.zeros(n)
    for k in range(1, K + 1):
        a = abs(math.sin(math.pi * k * 0.19)) / k ** 1.15
        tau = 0.85 * (262.0 / f) ** 0.4 / (1 + 0.45 * (k - 1) ** 1.25)
        m = min(n, samples(tau * 6))
        out[:m] += a * np.exp(-t[:m] / tau) * np.sin(2 * np.pi * f * k * (1 + 0.0004 * k * k) * t[:m] + rng.uniform(0, 6.28))
    # finger/pick noise and a damped release at the end of the note (next strum)
    out[:samples(0.006)] += sos_filter(rng.standard_normal(samples(0.006)), "band", [1500, 6000]) * 0.03
    g = samples(dur)
    if g < n:
        out[g:] *= np.exp(-np.arange(n - g) / (0.06 * SR))
    return fade(out, 0.002, 0.02) * vel


def pluck(f, dur, vel=1.0, rng=None, bright=1.0):
    """Synth pluck (stereo): two detuned band-limited saws whose upper harmonics decay faster."""
    ring = dur + 0.12
    n = samples(ring)
    t = taxis(n)
    K = int(min(30, 11000 // f))
    out = np.zeros((n, 2))
    for ch, det in enumerate((1.0035, 0.9965)):
        for k in range(1, K + 1):
            tau = 0.42 / (1 + 0.55 * (k - 1) / bright)
            m = min(n, samples(tau * 6))
            out[:m, ch] += (1.0 / k) * np.exp(-t[:m] / tau) * np.sin(2 * np.pi * f * det * k * t[:m] + k * 0.3)
    g = samples(dur)
    if g < n:
        out[g:] *= np.exp(-np.arange(n - g) / (0.03 * SR))[:, None]
    out[:samples(0.002)] *= np.linspace(0, 1, samples(0.002))[:, None]
    return fade(out, 0.001, 0.01) * vel * 0.6


def bass(f, dur, vel=1.0, rng=None, bright=0.5):
    """Round soft bass: few harmonics, brief brightness decay, gentle saturation."""
    n = samples(dur + 0.12)
    t = taxis(n)
    env = adsr(n, 0.006, 0.18, 0.72, 0.06, dur)
    x = np.sin(2 * np.pi * f * t)
    for k, a in ((2, 0.45), (3, 0.2 * bright + 0.05), (4, 0.08 * bright)):
        x += a * np.exp(-t / 0.25) * np.sin(2 * np.pi * f * k * t)
    x = np.tanh(1.4 * x * env) / np.tanh(1.4)
    return fade(x, 0.003, 0.02) * vel


def pad(f, dur, vel=1.0, rng=None):
    """Warm detuned saw pad (stereo), slow attack and release."""
    n = samples(dur + 0.7)
    t = taxis(n)
    K = int(min(16, 4000 // f))
    env = adsr(n, 0.35, 0.8, 0.8, 0.45, dur)
    out = np.zeros((n, 2))
    for ch, dets in enumerate(((1.0, 1.0047), (0.9953, 1.0021))):
        for det in dets:
            ph0 = rng.uniform(0, 6.28)
            for k in range(1, K + 1):
                out[:, ch] += (np.exp(-k / 5.0) / k) * np.sin(2 * np.pi * f * det * k * t + ph0 * k)
    return fade(out * env[:, None], 0.01, 0.05) * vel * 0.35


def brass(f, dur, vel=1.0, rng=None, vibrato=True):
    """Soft synth brass: brightness follows the envelope, small pitch scoop and delayed vibrato."""
    n = samples(dur + 0.25)
    t = taxis(n)
    env = adsr(n, 0.035, 0.25, 0.8, 0.12, dur)
    cents = -35 * np.exp(-t / 0.03)
    if vibrato:
        cents = cents + 9 * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.18) / 0.25, 0, 1)
    phase = 2 * np.pi * np.cumsum(f * 2 ** (cents / 1200.0)) / SR
    K = int(min(14, 7000 // f))
    x = np.zeros(n)
    for k in range(1, K + 1):
        x += (1.0 / k) * env ** (1 + 0.22 * k) * np.sin(k * phase)
    return fade(x, 0.004, 0.03) * vel


def flute(f, dur, vel=1.0, rng=None, droop_cents=0.0):
    """Soft ocarina-like tone (sine + a little odd harmonic + breath), optional pitch droop and vibrato."""
    n = samples(dur + 0.15)
    t = taxis(n)
    env = adsr(n, 0.04, 0.3, 0.85, 0.09, dur)
    cents = 7 * np.sin(2 * np.pi * 5.0 * t) * np.clip((t - 0.12) / 0.2, 0, 1) - droop_cents * np.clip(t / max(dur, 1e-3), 0, 1) ** 2
    phase = 2 * np.pi * np.cumsum(f * 2 ** (cents / 1200.0)) / SR
    x = np.sin(phase) + 0.12 * np.sin(3 * phase) + 0.05 * np.sin(2 * phase)
    breath = sos_filter(rng.standard_normal(n), "band", [min(f * 2, 8000), min(f * 6, 16000)]) * 0.025
    return fade((x + breath) * env, 0.005, 0.03) * vel


# ----------------------------------------------------------------------------------------- drums

def kick(vel=1.0, rng=None, soft=False):
    n = samples(0.45)
    t = taxis(n)
    f = 48 + (130 if soft else 150) * np.exp(-t / 0.035)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * exp_env(n, 0.16 if soft else 0.2, 0.001)
    x[:samples(0.004)] += sos_filter(rng.standard_normal(samples(0.004)), "low", 3000) * (0.08 if soft else 0.15)
    return fade(x, 0.0005, 0.03) * vel


def snare(vel=1.0, rng=None):
    n = samples(0.3)
    noise = sos_filter(rng.standard_normal(n), "band", [1200, 7000]) * exp_env(n, 0.07, 0.0005)
    tone = np.sin(2 * np.pi * 190 * taxis(n)) * exp_env(n, 0.05, 0.0005) * 0.6
    return fade(noise * 0.8 + tone, 0.0005, 0.03) * vel


def clap(vel=1.0, rng=None):
    n = samples(0.32)
    x = np.zeros(n)
    for i, d in enumerate((0.0, 0.009, 0.019, 0.027)):
        m = samples(0.06)
        burst = rng.standard_normal(m) * exp_env(m, 0.006 if i < 3 else 0.06, 0.0003)
        place(x, burst, d, 1.0 if i < 3 else 0.9)
    x = sos_filter(x, "band", [900, 3500])
    return fade(x, 0.0005, 0.03) * vel


def hat(vel=1.0, rng=None, open_=False):
    n = samples(0.25 if open_ else 0.08)
    x = sos_filter(rng.standard_normal(n), "high", 7500) * exp_env(n, 0.09 if open_ else 0.022, 0.0005)
    return fade(x, 0.0005, 0.01) * vel


def shaker(vel=1.0, rng=None):
    n = samples(0.12)
    t = taxis(n)
    env = np.where(t < 0.012, t / 0.012, np.exp(-(t - 0.012) / 0.035))
    x = sos_filter(rng.standard_normal(n), "band", [4500, 12000]) * env
    return fade(x, 0.001, 0.01) * vel


def woodblock(f=1700, vel=1.0, rng=None, tau=0.035):
    x = modal(f, [1.0, 2.71], [1.0, 0.35], [tau, tau / 3], tau * 6, attack=0.0004, rng=rng)
    x[:samples(0.002)] += mallet_noise(0.002, 6000, rng, 0.0006) * 0.2
    return fade(x, 0.0003, 0.01) * vel


def snap(vel=1.0, rng=None):
    n = samples(0.1)
    x = sos_filter(rng.standard_normal(n), "band", [1800, 4500]) * exp_env(n, 0.012, 0.0003)
    x += np.sin(2 * np.pi * 1250 * taxis(n)) * exp_env(n, 0.01, 0.0003) * 0.3
    return fade(x, 0.0003, 0.01) * vel


def tom(f=160, vel=1.0, rng=None):
    n = samples(0.4)
    t = taxis(n)
    fr = f * (1 + 0.5 * np.exp(-t / 0.03))
    x = np.sin(2 * np.pi * np.cumsum(fr) / SR) * exp_env(n, 0.18, 0.001)
    x += sos_filter(rng.standard_normal(n), "band", [300, 2000]) * exp_env(n, 0.02, 0.0005) * 0.2
    return fade(x, 0.0005, 0.03) * vel


def swell(dur, rng, lo=3000, hi=14000):
    """Soft cymbal-like noise swell (reverse-ish), for section changes."""
    n = samples(dur)
    t = taxis(n)
    env = (t / dur) ** 2
    x = sos_filter(rng.standard_normal(n), "band", [lo, hi]) * env
    return fade(x, 0.01, 0.03)


# ============================================================================================ noise & fx helpers

def filtered_noise(dur, fc, bw_oct, rng, nfft=1024, hop=128):
    """White noise through a time-varying log-Gaussian band-pass (STFT domain). fc: callable(t[s]) -> Hz."""
    n = samples(dur)
    x = rng.standard_normal(n + nfft)
    f, t, Z = signal.stft(x, SR, nperseg=nfft, noverlap=nfft - hop)
    centers = np.maximum(np.asarray(fc(np.clip(t, 0, dur)), dtype=float), 20.0)
    lf = np.log2(np.maximum(f, 1.0)[:, None] / centers[None, :])
    G = np.exp(-0.5 * (lf / bw_oct) ** 2)
    _, y = signal.istft(Z * G, SR, nperseg=nfft, noverlap=nfft - hop)
    y = y[:n]
    return y / (np.sqrt(np.mean(y ** 2)) + 1e-9)


def bubble(f0, f1, dur, rng=None, tau=None, harm=0.15):
    """Bubble 'pop': sine with an exponential pitch rise and fast decay (the classic water-drop sound)."""
    n = samples(dur)
    t = taxis(n)
    k = math.log(f1 / f0) / dur
    f = f0 * np.exp(k * np.minimum(t, dur))
    ph = 2 * np.pi * np.cumsum(f) / SR
    env = exp_env(n, tau or dur * 0.45, 0.0015)
    return fade((np.sin(ph) + harm * np.sin(2 * ph)) * env, 0.0015, 0.008)


def sweep_tone(f0, f1, dur, tau=None, harm=(0.0,), curve="exp"):
    n = samples(dur)
    t = taxis(n)
    if curve == "exp":
        f = f0 * (f1 / f0) ** (t / dur)
    else:
        f = f0 + (f1 - f0) * t / dur
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(ph)
    for i, a in enumerate(harm, start=2):
        x += a * np.sin(i * ph)
    env = exp_env(n, tau, 0.002) if tau else np.ones(n)
    return fade(x * env, 0.002, 0.01)


def sparkle(dur, count, rng, lo="C7", hi="E8", vel=0.5, tau=(0.04, 0.12), start=0.0):
    """Shower of tiny high pentatonic pings."""
    pent = [m for m in range(midi(lo), midi(hi) + 1) if (m % 12) in (0, 2, 4, 7, 9)]
    buf = np.zeros(samples(dur + 0.4))
    for i in range(count):
        tt = start + (dur - start) * (i / max(count, 1)) + rng.uniform(-0.02, 0.02)
        f = hz(int(rng.choice(pent)))
        ta = rng.uniform(*tau)
        ping = modal(f, [1.0, 2.76], [1.0, 0.25], [ta, ta * 0.3], ta * 6, attack=0.0015, rng=rng)
        place(buf, ping, max(tt, 0), vel * rng.uniform(0.5, 1.0))
    return buf


def shimmer(dur, rng, lo=6000, hi=12000, rise=0.15):
    n = samples(dur)
    t = taxis(n)
    env = np.minimum(t / rise, 1.0) * np.exp(-np.maximum(t - rise, 0) / (dur * 0.35))
    x = sos_filter(rng.standard_normal(n), "band", [lo, min(hi, SR * 0.45)]) * env
    return fade(x, 0.005, 0.02)


def make_ir(rt60, length, rng, stereo=True, predelay=0.012, hf_damp=2.2, early=True):
    """Synthetic room impulse response: decorrelated noise per channel, exponential decay (highs faster)."""
    n = samples(length)
    t = taxis(n)
    env = 10 ** (-3 * t / rt60)
    chans = []
    for _ in range(2 if stereo else 1):
        noise = rng.standard_normal(n)
        lo = sos_filter(noise, "low", 2500)
        hi_ = noise - lo
        ir = lo * env + hi_ * env ** hf_damp
        ir = sos_filter(ir, "high", 180)
        ir[:samples(0.004)] *= np.linspace(0, 1, samples(0.004))
        if early:
            for d, g in ((0.007, 0.5), (0.013, 0.35), (0.021, 0.25), (0.034, 0.18)):
                j = samples(d + rng.uniform(-0.002, 0.002))
                if j < n:
                    ir[j] += g * rng.choice([-1, 1]) * 3
        ir = np.concatenate([np.zeros(samples(predelay)), ir])
        chans.append(ir / np.sqrt(np.sum(ir ** 2)))
    return np.stack(chans, axis=1) if stereo else chans[0]


def reverb_mono(x, rng, rt60=0.5, wet=0.15, length=None):
    ir = make_ir(rt60, length or rt60 * 1.2, rng, stereo=False, predelay=0.008)
    y = signal.fftconvolve(x, ir)
    out = np.zeros(len(y))
    out[:len(x)] = x
    return out + y * wet


# ============================================================================================ loudness & checks

def k_weight(x):
    """ITU-R BS.1770 K-weighting at SR (formulas as in pyloudnorm)."""
    G, Q, fc = 3.999843853973347, 0.7071752369554196, 1681.974450955533
    A = 10 ** (G / 40)
    w0 = 2 * math.pi * fc / SR
    alpha = math.sin(w0) / (2 * Q)
    c = math.cos(w0)
    b = [A * ((A + 1) + (A - 1) * c + 2 * math.sqrt(A) * alpha), -2 * A * ((A - 1) + (A + 1) * c),
         A * ((A + 1) + (A - 1) * c - 2 * math.sqrt(A) * alpha)]
    a = [(A + 1) - (A - 1) * c + 2 * math.sqrt(A) * alpha, 2 * ((A - 1) - (A + 1) * c),
         (A + 1) - (A - 1) * c - 2 * math.sqrt(A) * alpha]
    y = signal.lfilter(np.array(b) / a[0], np.array(a) / a[0], x, axis=0)
    Q2, fc2 = 0.5003270373238773, 38.13547087602444
    w0 = 2 * math.pi * fc2 / SR
    alpha = math.sin(w0) / (2 * Q2)
    c = math.cos(w0)
    b = [(1 + c) / 2, -(1 + c), (1 + c) / 2]
    a = [1 + alpha, -2 * c, 1 - alpha]
    return signal.lfilter(np.array(b) / a[0], np.array(a) / a[0], y, axis=0)


def lufs(x):
    """Integrated loudness (BS.1770-4 gating). x: (n,) or (n, ch)."""
    if x.ndim == 1:
        x = x[:, None]
    y = k_weight(x)
    blk, hop = samples(0.4), samples(0.1)
    if len(y) < blk:
        z = np.mean(y ** 2, axis=0).sum()
        return -0.691 + 10 * math.log10(max(z, 1e-12))
    zs = np.array([np.mean(y[i:i + blk] ** 2, axis=0).sum() for i in range(0, len(y) - blk + 1, hop)])
    ls = -0.691 + 10 * np.log10(np.maximum(zs, 1e-12))
    zs = zs[ls > -70]
    if len(zs) == 0:
        return -70.0
    rel = -0.691 + 10 * math.log10(zs.mean()) - 10
    ls = -0.691 + 10 * np.log10(zs)
    return -0.691 + 10 * math.log10(zs[ls > rel].mean())


def limiter(x, ceiling_db, look=0.006, wrap=False):
    """Look-ahead peak limiter (no overshoot): gain = moving average of a min-filtered gain curve."""
    thr = 10 ** (ceiling_db / 20)
    peak = np.abs(x).max(axis=1) if x.ndim == 2 else np.abs(x)
    w = 2 * samples(look) + 1
    mode = "wrap" if wrap else "nearest"
    g = np.minimum(1.0, thr / np.maximum(maximum_filter1d(peak, w, mode=mode), 1e-9))
    g = minimum_filter1d(g, w, mode=mode)
    g = uniform_filter1d(g, w, mode=mode)
    g = np.minimum(g, uniform_filter1d(minimum_filter1d(g, 4 * w, mode=mode), 4 * w, mode=mode) ** 0.5 * g ** 0.5)
    return x * (g[:, None] if x.ndim == 2 else g)


def remove_dc(x):
    x = x - np.mean(x, axis=0)
    sos = signal.butter(2, 25, btype="high", fs=SR, output="sos")
    return signal.sosfiltfilt(sos, x, axis=0)


def trim_tail(x, thresh_db=-62):
    thr = 10 ** (thresh_db / 20) * np.max(np.abs(x))
    idx = np.nonzero(np.abs(x) > thr)[0]
    if len(idx) == 0:
        return x
    end = min(len(x), idx[-1] + samples(0.01))
    return x[:end]


def write_wav16(path, x, rng):
    """16-bit PCM with TPDF dither."""
    d = (rng.uniform(-0.5, 0.5, x.shape) + rng.uniform(-0.5, 0.5, x.shape)) / 32767.0
    y = np.clip(np.rint((x + d) * 32767.0), -32768, 32767).astype(np.int16)
    wavfile.write(path, SR, y)


# ============================================================================================ SFX designs

def sfx_click(rng):
    b = np.zeros(samples(0.2))
    place(b, marimba(hz("G6"), 0.9, rng, length=0.12), 0)
    place(b, bubble(1100, 1700, 0.03, tau=0.012), 0.002, 0.35)
    return b


def sfx_popup_open(rng):
    b = np.zeros(samples(0.8))
    place(b, sweep_tone(330, 980, 0.16, tau=0.12, harm=(0.2,)), 0, 0.7)
    place(b, glock(hz("C6"), 0.55, rng, length=0.6), 0.05)
    place(b, glock(hz("G6"), 0.5, rng, length=0.6), 0.12)
    place(b, sparkle(0.35, 4, rng, vel=0.25, start=0.12), 0.0)
    return reverb_mono(b, rng, 0.5, 0.18)


def sfx_popup_close(rng):
    b = np.zeros(samples(0.6))
    place(b, sweep_tone(900, 360, 0.13, tau=0.09, harm=(0.2,)), 0, 0.7)
    place(b, marimba(hz("G5"), 0.6, rng, length=0.3), 0.0)
    place(b, marimba(hz("C5"), 0.7, rng, length=0.4), 0.065)
    return reverb_mono(b, rng, 0.4, 0.12)


def sfx_pick(rng):
    b = np.zeros(samples(0.16))
    place(b, marimba(hz("E6"), 0.7, rng, length=0.1) * exp_env(samples(0.1), 0.03), 0)
    place(b, bubble(600, 1150, 0.035, tau=0.015), 0.0, 0.45)
    return b


def sfx_drop(rng):
    b = np.zeros(samples(0.22))
    place(b, marimba(hz("A5"), 0.8, rng, length=0.16) * exp_env(samples(0.16), 0.05), 0.002)
    place(b, sweep_tone(230, 140, 0.06, tau=0.025), 0, 0.6)
    place(b, mallet_noise(0.02, 1800, rng, 0.003), 0, 0.15)
    return b


def sfx_invalid(rng):
    b = np.zeros(samples(0.42))
    for i, t0 in enumerate((0.0, 0.12)):
        place(b, sweep_tone(330, 235, 0.11, tau=0.05, harm=(0.35, 0.1)), t0, 0.8 if i == 0 else 0.7)
    return reverb_mono(sos_filter(b, "low", 2500), rng, 0.3, 0.08)


def sfx_match(rng):
    b = np.zeros(samples(1.2))
    for i, (note, t0) in enumerate((("C6", 0.0), ("E6", 0.055), ("G6", 0.11))):
        place(b, glock(hz(note), 0.75, rng, length=0.9), t0)
        place(b, kalimba(hz(note), 0.35, rng, length=0.5), t0)
    place(b, bell(hz("C7"), 0.35, rng, length=0.8), 0.17)
    place(b, sparkle(0.6, 9, rng, vel=0.3, start=0.15), 0.0)
    place(b, shimmer(0.6, rng), 0.12, 0.05)
    return reverb_mono(b, rng, 0.7, 0.22)


def sfx_combo(rng):
    b = np.zeros(samples(1.2))
    notes = ["C6", "D6", "E6", "G6", "A6", "C7"]
    for i, note in enumerate(notes):
        t0 = i * 0.045
        place(b, kalimba(hz(note), 0.6 + 0.06 * i, rng, length=0.5), t0)
        place(b, glock(hz(note), 0.35 + 0.05 * i, rng, length=0.6), t0)
    place(b, sparkle(0.7, 10, rng, vel=0.28, start=0.25), 0.0)
    return reverb_mono(b, rng, 0.7, 0.22)


def sfx_star(rng):
    b = np.zeros(samples(0.9))
    place(b, glock(hz("G6"), 0.8, rng, length=0.7), 0)
    place(b, glock(hz("D7"), 0.7, rng, length=0.7), 0.07)
    place(b, sparkle(0.45, 5, rng, vel=0.22, start=0.1), 0.0)
    return reverb_mono(b, rng, 0.6, 0.2)


def sfx_coin(rng):
    b = np.zeros(samples(0.7))
    for note, t0, v in (("B5", 0.0, 0.7), ("E6", 0.075, 0.9)):
        f = hz(note)
        tone = modal(f, [1, 2, 3, 4.2], [1.0, 0.25, 0.3, 0.12], [0.35, 0.12, 0.08, 0.05], 0.6, attack=0.0008, rng=rng)
        place(b, tone, t0, v)
        place(b, bell(f * 2, 0.15, rng, length=0.4), t0)
    return reverb_mono(b, rng, 0.45, 0.14)


def sfx_layer_reveal(rng):
    b = np.zeros(samples(0.6))
    place(b, filtered_noise(0.24, lambda t: 500 * (3200 / 500) ** (t / 0.24), 0.7, rng)
          * np.sin(np.pi * np.linspace(0, 1, samples(0.24))) ** 2, 0, 0.18)
    place(b, bubble(420, 820, 0.06, tau=0.03), 0.17, 0.6)
    place(b, marimba(hz("C6"), 0.45, rng, length=0.3), 0.19)
    return reverb_mono(b, rng, 0.45, 0.14)


def sfx_unlock(rng):
    b = np.zeros(samples(1.3))
    place(b, mallet_noise(0.05, 2500, rng, 0.01), 0, 0.5)
    place(b, sweep_tone(180, 90, 0.08, tau=0.04), 0, 0.5)
    for t0 in (0.0, 0.06, 0.13):
        f0 = rng.uniform(1900, 2700)
        place(b, modal(f0, [1, 2.32, 4.25, 6.63], [1, 0.6, 0.35, 0.2], [0.11, 0.07, 0.05, 0.03], 0.4, attack=0.0005,
                       rng=rng), t0, 0.45)
    for i, note in enumerate(("C6", "G6", "C7")):
        place(b, glock(hz(note), 0.6 + 0.1 * i, rng, length=0.8), 0.2 + 0.07 * i)
    place(b, sparkle(0.6, 7, rng, vel=0.25, start=0.3), 0.0)
    return reverb_mono(b, rng, 0.7, 0.2)


def sfx_win(rng):
    """~3.5 s joyful fanfare: brass 'da-da-da-daaa, da-daaaa', glockenspiel run, timpani-ish kicks, sparkle shower."""
    b = np.zeros(samples(4.2))
    mel = [("G4", 0.0, 0.13), ("C5", 0.15, 0.13), ("E5", 0.30, 0.13), ("G5", 0.45, 0.38), ("E5", 0.90, 0.13),
           ("G5", 1.05, 1.55)]
    for note, t0, d in mel:
        place(b, brass(hz(note), d, 0.55, rng), t0)
        place(b, glock(hz(note) * 2, 0.25, rng, length=0.8), t0)
    for chord, t0, d in ((("C4", "E4", "G4"), 0.45, 0.38), (("C4", "E4", "G4", "C5"), 1.05, 1.6)):
        for note in chord:
            place(b, brass(hz(note), d, 0.22, rng, vibrato=False), t0 + rng.uniform(0, 0.01))
    place(b, brass(hz("F4"), 0.13, 0.2, rng), 0.9)
    place(b, brass(hz("A4"), 0.13, 0.2, rng), 0.9)
    for t0, v in ((0.45, 0.6), (1.05, 0.8)):
        place(b, kick(v, rng, soft=True), t0)
        place(b, tom(110, 0.3, rng), t0)
    for i, note in enumerate(("C6", "E6", "G6", "C7", "E7", "G7")):
        place(b, glock(hz(note), 0.5, rng, length=1.0), 1.08 + i * 0.045)
    place(b, sparkle(2.6, 26, rng, vel=0.22, start=1.15), 0.0)
    place(b, swell(0.6, rng), 0.48, 0.05)
    place(b, shimmer(2.2, rng, rise=0.4), 1.05, 0.04)
    for i, note in enumerate(("C5", "E5", "G5", "C6")):
        place(b, bell(hz(note), 0.12, rng, length=2.0), 1.05 + 0.02 * i)
    b = sos_filter(b, "low", 9000)
    return reverb_mono(b, rng, 1.2, 0.22, length=1.6)


def sfx_lose(rng):
    """Gentle descending 'aww' (wah-wah-wah-waaah) on a soft ocarina, with a warm low marimba under the last note."""
    b = np.zeros(samples(2.6))
    for note, t0, d, droop in (("G4", 0.0, 0.26, 0), ("F#4", 0.32, 0.26, 0), ("F4", 0.64, 0.26, 0),
                               ("E4", 0.96, 1.05, 40)):
        place(b, flute(hz(note), d, 0.7, rng, droop_cents=droop), t0)
    place(b, marimba(hz("C3"), 0.5, rng, length=1.2), 0.96)
    place(b, marimba(hz("G3"), 0.35, rng, length=1.0), 0.98)
    place(b, bubble(500, 260, 0.25, tau=0.12), 2.0, 0.15)
    return reverb_mono(sos_filter(b, "low", 5000), rng, 0.9, 0.18)


def sfx_timer_tick(rng):
    b = np.zeros(samples(0.08))
    place(b, woodblock(1850, 1.0, rng, tau=0.012), 0)
    return sos_filter(b, "low", 6000)


def sfx_freeze(rng):
    b = np.zeros(samples(1.5))
    for i, note in enumerate(("E7", "C7", "A6", "E6", "C6")):
        place(b, bell(hz(note), 0.4, rng, length=0.9, ratio=3.01, index=1.6), i * 0.065)
    n = samples(0.9)
    crackle = np.zeros(n)
    for _ in range(70):
        i = int(rng.beta(1.0, 2.5) * n)
        crackle[i:i + 1] += rng.choice([-1, 1]) * rng.uniform(0.3, 1.0)
    crackle = sos_filter(crackle, "high", 3500)
    place(b, crackle, 0.02, 0.5)
    place(b, filtered_noise(0.8, lambda t: 7000 * (1800 / 7000) ** (t / 0.8), 0.6, rng)
          * exp_env(samples(0.8), 0.3, 0.05), 0.0, 0.08)
    place(b, sweep_tone(320, 160, 0.4, tau=0.2), 0.0, 0.25)
    return reverb_mono(b, rng, 1.0, 0.3)


def sfx_hammer(rng):
    """Cartoon toy-hammer smash: a chunky plastic 'bonk' (modal knock + pitch-dropping body), a bright crack, a
    short low thump for weight and tinkling debris. The thump stays moderate: phone speakers cannot reproduce it,
    and with peak normalization a dominant sub-bass would make the audible part ~6 dB quieter than the other SFX."""
    b = np.zeros(samples(0.8))
    place(b, sweep_tone(150, 60, 0.2, tau=0.07), 0, 0.5)                                   # weight
    place(b, sweep_tone(700, 380, 0.14, tau=0.06, harm=(0.45, 0.2)), 0, 0.6)               # "bonk" body
    place(b, modal(420, [1.0, 2.43, 4.1], [1.0, 0.55, 0.25], [0.07, 0.035, 0.018], 0.3, attack=0.0004, rng=rng), 0, 0.7)
    crack = sos_filter(rng.standard_normal(samples(0.06)), "band", [900, 4500]) * exp_env(samples(0.06), 0.012, 0.0004)
    place(b, crack, 0.0, 0.8)
    for t0 in (0.07, 0.12, 0.18, 0.25, 0.31):                                              # debris
        place(b, marimba(rng.uniform(1500, 2600), 0.3, rng, length=0.08) * exp_env(samples(0.08), 0.02), t0)
    b = np.tanh(1.6 * b) / np.tanh(1.6)
    return reverb_mono(b, rng, 0.4, 0.12)


def sfx_wand(rng):
    b = np.zeros(samples(1.6))
    notes = ["C6", "D6", "E6", "G6", "A6", "C7", "D7", "E7"]
    for i, note in enumerate(notes):
        place(b, glock(hz(note), 0.45 + 0.04 * i, rng, length=0.7), i * 0.035)
    place(b, filtered_noise(0.4, lambda t: 2000 * (9000 / 2000) ** (t / 0.4), 0.5, rng)
          * np.sin(np.pi * np.linspace(0, 1, samples(0.4))), 0, 0.07)
    place(b, bell(hz("E7"), 0.45, rng, length=1.0), 0.3)
    place(b, sparkle(1.0, 12, rng, vel=0.25, start=0.3), 0.0)
    return reverb_mono(b, rng, 0.9, 0.28)


def sfx_shuffle(rng):
    b = np.zeros(samples(0.85))
    for i in range(7):
        n = samples(0.03)
        flick = sos_filter(rng.standard_normal(n), "band", [1500 + 150 * (i % 3), 5200]) * exp_env(n, 0.008, 0.0008)
        place(b, flick, 0.02 + i * 0.065 + rng.uniform(-0.006, 0.006), 0.5)
    n = samples(0.5)
    sw = filtered_noise(0.5, lambda t: 700 + 1300 * np.sin(np.pi * t / 0.5), 0.8, rng)
    sw *= np.sin(np.pi * np.linspace(0, 1, n)) ** 2 * (0.7 + 0.3 * np.sin(2 * np.pi * 12 * taxis(n)))
    place(b, sw, 0.0, 0.12)
    place(b, bubble(500, 950, 0.05, tau=0.025), 0.5, 0.5)
    return reverb_mono(b, rng, 0.4, 0.12)


def sfx_bomb(rng):
    """Cartoon 'ka-boom': punchy mid-range blast (saturated noise burst + a 'boom' body with harmonics), a sub
    sweep for weight (moderate, see sfx_hammer), crackling bubbles and a few sparkles as the smoke clears."""
    b = np.zeros(samples(1.7))
    place(b, sweep_tone(110, 38, 0.8, tau=0.3), 0, 0.55)                                   # sub weight
    place(b, sweep_tone(240, 90, 0.35, tau=0.12, harm=(0.6, 0.35, 0.2)), 0, 0.75)          # audible boom body
    n = samples(1.2)
    noise = filtered_noise(1.2, lambda tt: 3000 * (300 / 3000) ** (tt / 1.2) ** 0.7, 1.3, rng) * exp_env(n, 0.22, 0.002)
    place(b, noise, 0, 0.55)
    burst = sos_filter(rng.standard_normal(samples(0.08)), "band", [500, 5000]) * exp_env(samples(0.08), 0.02, 0.0005)
    place(b, burst, 0, 0.7)
    for _ in range(16):
        place(b, bubble(rng.uniform(350, 900), rng.uniform(1000, 2000), 0.04, tau=0.015), rng.uniform(0.12, 0.9), 0.16)
    place(b, sparkle(1.0, 6, rng, vel=0.14, start=0.3), 0.0)
    b = sos_filter(b, "low", 6000)
    b = np.tanh(1.8 * b) / np.tanh(1.8)
    return reverb_mono(b, rng, 0.9, 0.18)


def sfx_chest_open(rng):
    b = np.zeros(samples(2.0))
    place(b, sweep_tone(150, 90, 0.12, tau=0.06), 0, 0.7)
    place(b, marimba(hz("C4"), 0.6, rng, length=0.3), 0)
    for i, note in enumerate(("C6", "E6", "G6", "C7", "E7")):
        place(b, glock(hz(note), 0.5 + 0.05 * i, rng, length=0.9), 0.12 + i * 0.06)
    for note in ("C5", "E5", "G5", "C6"):
        place(b, pad(hz(note), 0.7, 0.35, rng)[:, 0], 0.1)
    place(b, sparkle(1.3, 16, rng, vel=0.25, start=0.4), 0.0)
    place(b, shimmer(1.0, rng, rise=0.3), 0.15, 0.04)
    return reverb_mono(b, rng, 1.0, 0.25)


def sfx_spin_tick(rng):
    b = np.zeros(samples(0.05))
    place(b, woodblock(2900, 1.0, rng, tau=0.006), 0)
    place(b, mallet_noise(0.006, 7000, rng, 0.0012), 0, 0.2)
    return b


def sfx_spin_win(rng):
    b = np.zeros(samples(1.6))
    for i, note in enumerate(("G5", "C6", "E6", "G6")):
        place(b, kalimba(hz(note), 0.7, rng, length=0.6), i * 0.065)
        place(b, glock(hz(note), 0.35, rng, length=0.6), i * 0.065)
    place(b, bell(hz("C7"), 0.6, rng, length=1.1), 0.28)
    place(b, glock(hz("C7"), 0.5, rng, length=1.0), 0.28)
    place(b, sparkle(1.0, 12, rng, vel=0.25, start=0.3), 0.0)
    return reverb_mono(b, rng, 0.9, 0.25)


def sfx_reward(rng):
    b = np.zeros(samples(2.0))
    n = samples(0.32)
    place(b, filtered_noise(0.32, lambda t: 1500 * (6000 / 1500) ** (t / 0.32), 0.7, rng) * np.linspace(0, 1, n) ** 2,
          0, 0.08)
    for note in ("C6", "E6", "G6"):
        place(b, glock(hz(note), 0.55, rng, length=1.3), 0.3)
        place(b, bell(hz(note), 0.25, rng, length=1.2), 0.3)
    for note in ("C4", "G4", "E5"):
        place(b, pad(hz(note), 0.8, 0.4, rng)[:, 1], 0.28)
    place(b, sparkle(1.3, 14, rng, vel=0.25, start=0.35), 0.0)
    return reverb_mono(b, rng, 1.0, 0.25)


def sfx_heart(rng):
    b = np.zeros(samples(0.9))
    for t0, f0 in ((0.0, 520), (0.16, 440)):
        place(b, sweep_tone(f0, f0 * 0.72, 0.12, tau=0.06, harm=(0.25,)), t0, 0.8)
    place(b, glock(hz("E6"), 0.45, rng, length=0.6), 0.3)
    place(b, sparkle(0.4, 4, rng, vel=0.2, start=0.32), 0.0)
    return reverb_mono(b, rng, 0.6, 0.18)


def sfx_whoosh(rng):
    dur = 0.48
    n = samples(dur)
    x = filtered_noise(dur, lambda t: 300 * (1800 / 300) ** np.sin(np.pi * 0.85 * t / dur), 1.1, rng)
    env = np.sin(np.pi * np.linspace(0, 1, n) ** 0.8) ** 2
    return reverb_mono(sos_filter(x * env, "low", 6000), rng, 0.4, 0.12)


def sfx_card_flip(rng):
    b = np.zeros(samples(0.25))
    for t0, v in ((0.0, 1.0), (0.045, 0.6)):
        n = samples(0.05)
        place(b, sos_filter(rng.standard_normal(n), "high", 2000) * exp_env(n, 0.012, 0.0008), t0, v * 0.6)
    place(b, sweep_tone(200, 150, 0.05, tau=0.02), 0.04, 0.4)
    return b


def sfx_toggle(rng):
    b = np.zeros(samples(0.2))
    place(b, sweep_tone(640, 680, 0.05, tau=0.03, harm=(0.2,)), 0, 0.7)
    place(b, sweep_tone(960, 1000, 0.07, tau=0.04, harm=(0.2,)), 0.055, 0.8)
    return b


def sfx_error(rng):
    """Gentle 'uh-oh': two decaying mallet notes a minor third down, with a quiet ocarina layer."""
    b = np.zeros(samples(0.7))
    place(b, marimba(hz("C5"), 0.8, rng, length=0.3), 0)
    place(b, flute(hz("C5"), 0.09, 0.3, rng), 0)
    place(b, marimba(hz("A4"), 0.8, rng, length=0.5), 0.15)
    place(b, flute(hz("A4"), 0.2, 0.3, rng, droop_cents=25), 0.15)
    return reverb_mono(sos_filter(b, "low", 4000), rng, 0.35, 0.1)


def sfx_purchase(rng):
    b = np.zeros(samples(1.2))
    for t0, f0 in ((0.0, 2400), (0.05, 3100)):
        place(b, modal(f0, [1, 2.32, 4.25], [1, 0.5, 0.25], [0.12, 0.08, 0.05], 0.4, attack=0.0005, rng=rng), t0, 0.4)
    place(b, bell(hz("E6"), 0.6, rng, length=0.9), 0.1)
    place(b, bell(hz("B6"), 0.45, rng, length=0.9), 0.1)
    place(b, glock(hz("E7"), 0.4, rng, length=0.7), 0.12)
    place(b, sparkle(0.6, 7, rng, vel=0.22, start=0.15), 0.0)
    return reverb_mono(b, rng, 0.7, 0.2)


def sfx_pop(rng):
    b = np.zeros(samples(0.14))
    place(b, bubble(280, 1200, 0.05, tau=0.022), 0)
    place(b, mallet_noise(0.003, 5000, rng, 0.0006), 0.0, 0.08)
    return b


def sfx_sparkle(rng):
    b = np.zeros(samples(1.0))
    place(b, sparkle(0.55, 11, rng, vel=0.6), 0)
    place(b, shimmer(0.6, rng, 7000, 13000), 0, 0.06)
    return reverb_mono(b, rng, 0.7, 0.25)


def sfx_countdown(rng):
    b = np.zeros(samples(0.5))
    place(b, marimba(hz("A5"), 0.9, rng, length=0.45), 0)
    place(b, glock(hz("A6"), 0.25, rng, length=0.45), 0)
    return reverb_mono(b, rng, 0.4, 0.12)


def sfx_fanfare(rng):
    """Short 'ta-da-daa!' (~1.7 s) for level start / area unlocked."""
    b = np.zeros(samples(2.4))
    place(b, brass(hz("G4"), 0.1, 0.5, rng), 0.0)
    place(b, brass(hz("C5"), 0.1, 0.5, rng), 0.12)
    for note in ("E5", "G5", "C6"):
        place(b, brass(hz(note), 0.95, 0.35, rng), 0.26)
    for note in ("C4", "G4"):
        place(b, brass(hz(note), 0.95, 0.2, rng, vibrato=False), 0.26)
    place(b, kick(0.5, rng, soft=True), 0.26)
    for i, note in enumerate(("G6", "C7", "E7")):
        place(b, glock(hz(note), 0.4, rng, length=0.9), 0.28 + i * 0.05)
    place(b, sparkle(1.2, 10, rng, vel=0.2, start=0.35), 0.0)
    return reverb_mono(sos_filter(b, "low", 9000), rng, 0.9, 0.2)


def sfx_swoosh(rng):
    dur = 0.26
    n = samples(dur)
    x = filtered_noise(dur, lambda t: 1000 * (4500 / 1000) ** (t / dur), 0.9, rng)
    t = np.linspace(0, 1, n)
    env = np.minimum(t / 0.25, 1) * (1 - t) ** 1.5
    return reverb_mono(x * env, rng, 0.3, 0.1)


def finalize_sfx(x):
    x = remove_dc(np.asarray(x, dtype=np.float64))
    x = trim_tail(x)
    x = fade(x, 0.0015, min(0.03, len(x) / SR * 0.2))
    # Very short transients (ticks) keep a residual mean the high-pass cannot remove: subtract it with a Hann
    # window so the result has zero mean and the ends stay at exactly zero.
    w = np.hanning(len(x))
    x = x - np.mean(x) / max(np.mean(w), 1e-9) * w
    peak = np.max(np.abs(x))
    return x * (10 ** (SFX_PEAK_DB / 20) / peak) if peak > 0 else x


# ============================================================================================ music engine

class Song:
    def __init__(self, name, bpm, bars, seed, mix):
        self.name, self.bpm, self.bars, self.seed = name, bpm, bars, seed
        self.events = []   # (beat, dur_beats, midi or None, vel, instrument)
        # Stem loudness targets in LU relative to the lead (0). Each instrument stem is rendered, measured
        # (BS.1770 integrated, gated: i.e. its level while playing) and scaled to its target before mixing.
        self.mix = mix

    @property
    def beat_s(self):
        return 60.0 / self.bpm

    @property
    def loop_s(self):
        return self.bars * 4 * self.beat_s

    def add(self, beat, dur, note, vel, inst):
        self.events.append((beat, dur, midi(note) if isinstance(note, str) else note, vel, inst))


def fmidi(freq):
    """Frequency in Hz -> (fractional) MIDI note, for unpitched-but-tuned percussion (woodblocks, toms)."""
    return 12 * math.log2(freq / 440.0) + 69


# instrument -> (pan -1..1, reverb send, timing humanization sigma in ms)
INSTRUMENTS = {
    "uke": (-0.25, 0.22, 4),
    "marimba": (0.2, 0.25, 5),
    "glock": (0.35, 0.35, 5),
    "kalimba": (-0.3, 0.3, 4),
    "pluck": (0.0, 0.25, 3),
    "bass": (0.0, 0.03, 2),
    "pad": (0.0, 0.4, 0),
    "kick": (0.0, 0.03, 1.5),
    "kick_soft": (0.0, 0.03, 1.5),
    "snare": (0.05, 0.18, 2.5),
    "clap": (0.0, 0.25, 3),
    "hat": (0.3, 0.06, 3),
    "hat_open": (0.3, 0.1, 3),
    "shaker": (-0.35, 0.08, 4),
    "wood": (0.45, 0.15, 4),
    "snap": (-0.15, 0.2, 4),
    "tom": (-0.1, 0.15, 3),
    "swell": (0.0, 0.3, 0),
}


def render_event(inst, f, dur_s, vel, rng):
    if inst == "uke":
        return uke(f, dur_s, vel, rng)
    if inst == "marimba":
        return marimba(f, vel, rng)
    if inst == "glock":
        return glock(f, vel, rng, length=1.5)
    if inst == "kalimba":
        return kalimba(f, vel, rng)
    if inst == "pluck":
        return pluck(f, dur_s, vel, rng)
    if inst == "bass":
        return bass(f, dur_s, vel, rng)
    if inst == "pad":
        return pad(f, dur_s, vel, rng)
    if inst == "kick":
        return kick(vel, rng)
    if inst == "kick_soft":
        return kick(vel, rng, soft=True)
    if inst == "snare":
        return snare(vel, rng)
    if inst == "clap":
        return clap(vel, rng)
    if inst == "hat":
        return hat(vel, rng)
    if inst == "hat_open":
        return hat(vel, rng, open_=True)
    if inst == "shaker":
        return shaker(vel, rng)
    if inst == "wood":
        return woodblock(f or 1700, vel, rng, tau=0.03)
    if inst == "snap":
        return snap(vel, rng)
    if inst == "tom":
        return tom(f or 160, vel, rng)
    if inst == "swell":
        return swell(dur_s, rng)
    raise ValueError(inst)


def pan_gains(p):
    a = (p + 1) * math.pi / 4
    return math.cos(a), math.sin(a)


def render_song(song):
    """Renders the loop + tail stem by stem (each scaled to its mix target), adds a stereo convolution reverb and
    folds the tail onto the start so the loop is exactly periodic. Returns (stereo float (n, 2), stem report)."""
    rng = np.random.default_rng(song.seed)
    L = samples(song.loop_s)
    tail = samples(max(3.5, 2 * 4 * song.beat_s))
    n = L + tail
    dry = np.zeros((n, 2))
    send = np.zeros((n, 2))
    report = {}
    by_inst = {}
    for e in sorted(song.events, key=lambda e: e[0]):
        by_inst.setdefault(e[4], []).append(e)
    lead_ref = -20.0                                  # absolute LUFS the lead stem is scaled to (pre-master)
    for inst in sorted(by_inst):
        pan, rv, sigma = INSTRUMENTS[inst]
        gl, gr = pan_gains(pan)
        stem = np.zeros((n, 2))
        for beat, dur, note, vel, _ in by_inst[inst]:
            t = beat * song.beat_s
            if sigma > 0:
                t += float(np.clip(rng.normal(0, sigma / 1000.0), -2.5 * sigma / 1000, 2.5 * sigma / 1000))
            t = max(t, 0.0)
            v = vel * float(np.clip(rng.normal(1.0, 0.06), 0.8, 1.15))
            f = hz(note) if note is not None else None
            sig = render_event(inst, f, dur * song.beat_s, v, rng)
            if sig.ndim == 1:
                st = np.stack([sig * gl, sig * gr], axis=1)
            else:
                st = sig * np.array([gl, gr]) * math.sqrt(2) * 0.75
            place(stem, st, t)
        level = lufs(fold_loop(stem, L))
        target = lead_ref + song.mix.get(inst, -10.0)
        g = 10 ** ((target - level) / 20) if level > -69 else 0.0
        report[inst] = (song.mix.get(inst, -10.0), len(by_inst[inst]))
        dry += stem * g
        send += stem * (g * rv)
    ir = make_ir(1.6, 2.2, np.random.default_rng(song.seed + 1), stereo=True, predelay=0.02)
    wet = np.stack([signal.fftconvolve(send[:, c], ir[:, c])[:n] for c in range(2)], axis=1)
    mix = dry + wet * 0.9
    mix = sos_filter(mix, "high", 32)
    return fold_loop(mix, L), report


def fold_loop(x, L):
    """Seamless loop: everything rendered after the loop end is folded back onto the start."""
    out = x[:L].copy()
    pos = L
    while pos < len(x):
        m = min(L, len(x) - pos)
        out[:m] += x[pos:pos + m]
        pos += m
    return out


def master(x):
    """Loudness to MUSIC_LUFS with a circular look-ahead limiter at MUSIC_CEILING_DB (keeps the loop seamless)."""
    x = x - x.mean(axis=0)
    for _ in range(3):
        g = 10 ** ((MUSIC_LUFS - lufs(x)) / 20)
        x = limiter(x * g, MUSIC_CEILING_DB, wrap=True)
    return x


# ----------------------------------------------------------------------------------------- composition helpers

UKE_SHAPES = {   # re-entrant G4 C4 E4 A4 tuning, strings in physical order (down strum order)
    "F": ["A4", "C4", "F4", "A4"], "Am": ["A4", "C4", "E4", "A4"], "Bb": ["Bb4", "D4", "F4", "Bb4"],
    "C": ["G4", "C4", "E4", "C5"], "C7": ["G4", "C4", "E4", "Bb4"], "Dm": ["A4", "D4", "F4", "A4"],
    "Gm7": ["G4", "D4", "F4", "Bb4"], "Gm": ["G4", "D4", "G4", "Bb4"],
}

CHORD_TONES = {
    "F": ["F", "A", "C"], "Am": ["A", "C", "E"], "Bb": ["Bb", "D", "F"], "C": ["C", "E", "G"],
    "C7": ["C", "E", "G", "Bb"], "Dm": ["D", "F", "A"], "Gm7": ["G", "Bb", "D", "F"], "Gm": ["G", "Bb", "D"],
    "G": ["G", "B", "D"], "D": ["D", "F#", "A"], "Em": ["E", "G", "B"], "E": ["E", "G#", "B"],
    "E7": ["E", "G#", "B", "D"],
}


def tone(name, octave):
    return f"{name}{octave}"


def add_melody(song, bar, notes, inst, vel=0.8, octave_shift=0):
    for beat, dur, note in notes:
        m = midi(note) + 12 * octave_shift
        accent = 1.0 if beat % 1 == 0 else 0.88
        song.add(bar * 4 + beat, dur, m, vel * accent, inst)


def strum(song, bar, chord, pattern, vel=0.7):
    """Ukulele strum: pattern of (beat, 'D'|'U', accent). Strings spread 10-14 ms (down = low->high order)."""
    shape = UKE_SHAPES[chord]
    beats = [p[0] for p in pattern] + [4.0]
    for i, (beat, direction, acc) in enumerate(pattern):
        dur = beats[i + 1] - beat + 0.05
        order = shape if direction == "D" else list(reversed(shape))
        spread = 0.012 / (60.0 / song.bpm)       # seconds -> beats
        for k, note in enumerate(order):
            if direction == "U" and k == 3:
                continue                          # up strums usually miss the lowest string
            song.add(bar * 4 + beat + k * spread, dur, note, vel * acc * (1.0 if direction == "D" else 0.8), "uke")


def arp16(song, bar, chord, octave, inst, vel, pattern=(0, 1, 2, 3, 2, 1, 0, 1), beats=(0, 4), step=0.25):
    """Arpeggio over the chord (root position from `octave`, plus the octave), one note per `step` beats."""
    tones = CHORD_TONES[chord]
    root = midi(tone(tones[0], octave))
    pitches = []
    for tname in tones[:3]:
        m = midi(tone(tname, octave))
        while m < root:
            m += 12
        pitches.append(m)
    pitches = sorted(pitches) + [root + 12]
    b = beats[0]
    i = 0
    while b < beats[1] - 1e-6:
        accent = 1.0 if i % 4 == 0 else 0.8
        song.add(bar * 4 + b, step * 0.9, pitches[pattern[i % len(pattern)] % len(pitches)], vel * accent, inst)
        b += step
        i += 1


def root_of(chord, octave):
    name = CHORD_TONES[chord][0]
    return midi(tone(name, octave))


# ----------------------------------------------------------------------------------------- songs

def song_home():
    """Cheerful, relaxed, ~100 BPM, F major: ukulele strum, marimba melody, soft bass, shaker/snaps."""
    s = Song("music_home", 100, 16, seed=101, mix={
        "marimba": 0, "uke": -3, "bass": -4.5, "glock": -8, "kick_soft": -8, "snap": -11, "shaker": -13,
        "wood": -11})
    chords = ["F", "Am", "Bb", "C", "F", "Dm", "Gm7", "C7",
              "Bb", "C", "Am", "Dm", "Bb", "C", "F", "C7"]
    melody = [
        [(0, .5, "C5"), (.5, .5, "A4"), (1, .5, "C5"), (1.5, 1, "F5"), (2.5, .5, "E5"), (3, 1, "C5")],
        [(0, .5, "E5"), (.5, .5, "C5"), (1, .5, "E5"), (1.5, 1, "A5"), (2.5, .5, "G5"), (3, 1, "E5")],
        [(0, .5, "D5"), (.5, .5, "F5"), (1, 1, "Bb5"), (2, .5, "A5"), (2.5, .5, "G5"), (3, 1, "F5")],
        [(0, 1.5, "E5"), (1.5, .5, "D5"), (2, .5, "C5"), (2.5, .5, "D5"), (3, 1, "E5")],
        [(0, .5, "F5"), (.5, .5, "C5"), (1, .5, "A4"), (1.5, .5, "C5"), (2, 1, "F5"), (3, .5, "G5"), (3.5, .5, "A5")],
        [(0, 1, "A5"), (1, .5, "F5"), (1.5, .5, "D5"), (2, 1, "F5"), (3, 1, "A5")],
        [(0, .5, "Bb5"), (.5, .5, "A5"), (1, .5, "G5"), (1.5, .5, "F5"), (2, 1, "D5"), (3, 1, "F5")],
        [(0, 1.5, "E5"), (1.5, .5, "G5"), (2, 2, "C5")],
        [(0, .5, "F5"), (.5, .5, "Bb5"), (1, 1, "D6"), (2, .5, "C6"), (2.5, .5, "Bb5"), (3, 1, "F5")],
        [(0, .5, "E5"), (.5, .5, "G5"), (1, 1, "C6"), (2, .5, "Bb5"), (2.5, .5, "A5"), (3, 1, "G5")],
        [(0, 1, "A5"), (1, .5, "E5"), (1.5, .5, "A5"), (2, 1, "C6"), (3, 1, "G5")],
        [(0, 1.5, "F5"), (1.5, .5, "E5"), (2, .5, "D5"), (2.5, .5, "E5"), (3, 1, "F5")],
        [(0, .5, "D5"), (.5, .5, "F5"), (1, .5, "Bb5"), (1.5, .5, "A5"), (2, 1, "Bb5"), (3, .5, "C6"), (3.5, .5, "D6")],
        [(0, 1, "C6"), (1, .5, "G5"), (1.5, .5, "E5"), (2, 1, "G5"), (3, 1, "Bb5")],
        [(0, 1.5, "A5"), (1.5, .5, "G5"), (2, .5, "F5"), (2.5, .5, "G5"), (3, 1, "A5")],
        [(0, .5, "G5"), (.5, .5, "E5"), (1, .5, "C5"), (1.5, .5, "E5"), (2, 1, "G5"), (3, 1, "E5")],
    ]
    island = [(0, "D", 1.0), (1, "D", 0.8), (1.5, "U", 0.7), (2.5, "U", 0.75), (3, "D", 0.85), (3.5, "U", 0.7)]
    for bar, chord in enumerate(chords):
        strum(s, bar, chord, island, vel=0.62)
        add_melody(s, bar, melody[bar], "marimba", vel=0.78)
        if bar >= 8:   # second half: glockenspiel doubles the melody an octave up, softly
            add_melody(s, bar, melody[bar], "glock", vel=0.32, octave_shift=1)
        # bass: root, root, fifth, approach to the next chord
        r = root_of(chord, 2)
        if r < midi("E2"):
            r += 12
        nxt = root_of(chords[(bar + 1) % len(chords)], 2)
        if nxt < midi("E2"):
            nxt += 12
        fifth = r + 7
        approach = nxt - 1 if nxt > r else nxt + 2
        s.add(bar * 4 + 0, 1.4, r, 0.85, "bass")
        s.add(bar * 4 + 1.5, 0.45, r, 0.6, "bass")
        s.add(bar * 4 + 2, 1.4, fifth, 0.72, "bass")
        s.add(bar * 4 + 3.5, 0.45, approach, 0.6, "bass")
        # light percussion
        for e in range(8):
            s.add(bar * 4 + e * 0.5, 0.1, None, 0.75 if e % 2 else 0.45, "shaker")
        s.add(bar * 4 + 0, 0.2, None, 0.7, "kick_soft")
        s.add(bar * 4 + 2.5 if bar % 2 else bar * 4 + 2, 0.2, None, 0.5, "kick_soft")
        s.add(bar * 4 + 1, 0.1, None, 0.6, "snap")
        s.add(bar * 4 + 3, 0.1, None, 0.65, "snap")
        if bar % 4 == 3:
            s.add(bar * 4 + 3.5, 0.1, fmidi(2100), 0.5, "wood")
            s.add(bar * 4 + 3.75, 0.1, fmidi(1700), 0.45, "wood")
    return s


def song_game():
    """Upbeat, focused, ~118 BPM, G major: plucky melody, pulsing bass, kalimba arps, claps and shakers."""
    s = Song("music_game", 118, 20, seed=202, mix={
        "pluck": 0, "kalimba": -5, "bass": -4, "kick": -5, "clap": -7.5, "glock": -9, "pad": -10, "tom": -6,
        "shaker": -13, "hat": -14, "hat_open": -12, "swell": -14})
    chords = ["G", "D", "Em", "C", "G", "D", "C", "D",
              "Em", "C", "G", "D", "Em", "C", "Am", "D",
              "C", "D", "Em", "D"]
    CHORD_TONES.setdefault("Am", ["A", "C", "E"])
    bar1 = [(0, .5, "D5"), (.5, .5, "G5"), (1, .5, "F#5"), (1.5, .5, "G5"), (2, 1, "B5"), (3, .5, "A5"), (3.5, .5, "G5")]
    b9 = [(0, .75, "B5"), (.75, .75, "G5"), (1.5, .5, "E5"), (2, .5, "G5"), (2.5, .5, "B5"), (3, 1, "A5")]
    melody = [
        bar1,
        [(0, 1, "F#5"), (1, .5, "D5"), (1.5, .5, "E5"), (2, 1, "F#5"), (3, 1, "A5")],
        [(0, .5, "G5"), (.5, .5, "E5"), (1, .5, "B4"), (1.5, .5, "E5"), (2, 1, "G5"), (3, .5, "F#5"), (3.5, .5, "E5")],
        [(0, 1.5, "E5"), (1.5, .5, "D5"), (2, 1, "C5"), (3, 1, "D5")],
        bar1,
        [(0, 1, "F#5"), (1, .5, "A5"), (1.5, .5, "B5"), (2, 1, "A5"), (3, 1, "F#5")],
        [(0, .5, "E5"), (.5, .5, "G5"), (1, .5, "C6"), (1.5, .5, "B5"), (2, .5, "A5"), (2.5, .5, "G5"), (3, 1, "E5")],
        [(0, 1.5, "F#5"), (1.5, .5, "G5"), (2, 2, "A5")],
        b9,
        [(0, .75, "G5"), (.75, .75, "E5"), (1.5, .5, "C5"), (2, .5, "E5"), (2.5, .5, "G5"), (3, 1, "G5")],
        [(0, .75, "D5"), (.75, .75, "G5"), (1.5, .5, "B5"), (2, 1, "D6"), (3, .5, "B5"), (3.5, .5, "A5")],
        [(0, 2, "A5"), (2, .5, "F#5"), (2.5, .5, "G5"), (3, 1, "A5")],
        b9,
        [(0, .75, "G5"), (.75, .75, "E5"), (1.5, .5, "G5"), (2, 1, "C6"), (3, 1, "B5")],
        [(0, .5, "A5"), (.5, .5, "C6"), (1, .5, "B5"), (1.5, .5, "A5"), (2, 1, "E5"), (3, 1, "G5")],
        [(0, 1.5, "F#5"), (1.5, .5, "E5"), (2, 1, "D5"), (3, .5, "E5"), (3.5, .5, "F#5")],
        [(0, 1, "G5"), (1, 1, "E5"), (2, 1, "C5"), (3, 1, "E5")],
        [(0, 1, "F#5"), (1, 1, "D5"), (2, 1, "A4"), (3, 1, "D5")],
        [(0, 1, "G5"), (1, 1, "B5"), (2, 1, "E5"), (3, 1, "G5")],
        [(0, 1, "A5"), (1, .5, "F#5"), (1.5, .5, "E5"), (2, 1, "D5"), (3, .5, "E5"), (3.5, .5, "F#5")],
    ]
    for bar, chord in enumerate(chords):
        section = 0 if bar < 8 else (1 if bar < 16 else 2)
        add_melody(s, bar, melody[bar], "pluck", vel=0.8 if section != 2 else 0.65)
        if section == 1:
            add_melody(s, bar, melody[bar], "glock", vel=0.22, octave_shift=1)
        # harmony: kalimba 16th arps in B, offbeat stabs in A and the turnaround
        if section == 1:
            arp16(s, bar, chord, 5, "kalimba", 0.42)
        else:
            tones = CHORD_TONES[chord]
            for off in (0.5, 1.5, 2.5, 3.5):
                for tname in tones[:3]:
                    m = midi(tone(tname, 4))
                    if m < midi("G4"):
                        m += 12
                    s.add(bar * 4 + off, 0.3, m, 0.32, "kalimba")
        if section == 2:
            for tname in CHORD_TONES[chord][:3]:
                m = midi(tone(tname, 4))
                s.add(bar * 4, 3.9, m, 0.5, "pad")
        # bass: eighth-note pulse with octave pops
        r = root_of(chord, 2)
        if r < midi("E2"):
            r += 12
        if section < 2:
            pat = [0, 0, 12, 0, 0, 0, 12, 7] if bar % 2 else [0, 0, 12, 0, 0, 12, 0, 12]
            for e, iv in enumerate(pat):
                s.add(bar * 4 + e * 0.5, 0.38, r + iv, 0.8 if e % 2 == 0 else 0.62, "bass")
        else:
            for q in range(4):
                s.add(bar * 4 + q, 0.85, r + (12 if q == 3 else 0), 0.75, "bass")
        # drums
        for q in range(4):
            if section < 2 or q in (0, 2):
                s.add(bar * 4 + q, 0.2, None, 0.85 if q in (0, 2) else 0.55, "kick")
        for q in (1, 3):
            s.add(bar * 4 + q, 0.2, None, 0.8, "clap")
        for e in range(16):
            s.add(bar * 4 + e * 0.25, 0.1, None, 0.75 if e % 4 == 2 else (0.5 if e % 2 else 0.35), "shaker")
        for e in range(4):
            s.add(bar * 4 + e + 0.5, 0.1, None, 0.6, "hat")
        if bar in (7, 15):
            s.add(bar * 4 + 3.5, 0.5, None, 0.5, "hat_open")
        if bar == 19:     # tom fill into the loop start
            for k, f in enumerate((220, 196, 165, 147)):
                s.add(bar * 4 + 3 + k * 0.25, 0.25, fmidi(f), 0.6, "tom")
        if bar in (7, 15):
            s.add(bar * 4 + 2, 2, None, 1.0, "swell")
    return s


def song_hard():
    """Driving, tense but cute, ~132 BPM, A minor: staccato pluck ostinato, octave bass, four-on-the-floor,
    tick-tock woodblocks in the breakdown."""
    s = Song("music_hard", 132, 24, seed=303, mix={
        "pluck": 0, "marimba": -6.5, "kalimba": -6, "bass": -3.5, "kick": -4.5, "snare": -6.5, "clap": -11,
        "hat": -12.5, "wood": -10, "pad": -9, "tom": -5, "swell": -13})
    chords = ["Am", "F", "C", "G", "Am", "F", "E", "E",
              "Dm", "Am", "Dm", "E", "F", "G", "Am", "E",
              "F", "G", "Em", "Am", "F", "G", "E", "E"]
    CHORD_TONES.setdefault("Am", ["A", "C", "E"])
    b1 = [(0, .5, "E5"), (.5, .5, "A5"), (1, .5, "C6"), (1.5, .5, "B5"), (2, .5, "A5"), (2.5, .5, "E5"), (3, 1, "A5")]
    melody = [
        b1,
        [(0, .5, "F5"), (.5, .5, "A5"), (1, .5, "C6"), (1.5, .5, "A5"), (2, 1, "F5"), (3, .5, "G5"), (3.5, .5, "A5")],
        [(0, .5, "G5"), (.5, .5, "E5"), (1, .5, "C5"), (1.5, .5, "E5"), (2, 1, "G5"), (3, 1, "C6")],
        [(0, 1.5, "B5"), (1.5, .5, "A5"), (2, 1, "G5"), (3, 1, "D5")],
        b1,
        [(0, .5, "A5"), (.5, .5, "C6"), (1, 1, "C6"), (2, .5, "A5"), (2.5, .5, "G5"), (3, 1, "F5")],
        [(0, .5, "E5"), (.5, .5, "G#5"), (1, .5, "B5"), (1.5, .5, "G#5"), (2, .5, "E5"), (2.5, .5, "B4"), (3, 1, "E5")],
        [(0, 2, "G#5"), (2, 1, "B5"), (3, 1, "D6")],
        [(0, 1, "D6"), (1, .5, "A5"), (1.5, .5, "F5"), (2, 1, "D5"), (3, .5, "E5"), (3.5, .5, "F5")],
        [(0, 1, "E5"), (1, .5, "A5"), (1.5, .5, "C6"), (2, 1, "C6"), (3, 1, "A5")],
        [(0, .5, "F5"), (.5, .5, "A5"), (1, .5, "D6"), (1.5, .5, "C6"), (2, .5, "A5"), (2.5, .5, "F5"), (3, 1, "D5")],
        [(0, 1, "B4"), (1, 1, "E5"), (2, 1, "G#5"), (3, 1, "B5")],
        [(0, .5, "C6"), (.5, .5, "A5"), (1, .5, "F5"), (1.5, .5, "A5"), (2, 1, "C6"), (3, 1, "A5")],
        [(0, .5, "D6"), (.5, .5, "B5"), (1, .5, "G5"), (1.5, .5, "B5"), (2, 1, "D6"), (3, 1, "B5")],
        [(0, 1.5, "C6"), (1.5, .5, "B5"), (2, 1, "A5"), (3, 1, "E5")],
        [(0, 1, "G#5"), (1, 1, "B5"), (2, 2, "E5")],
        [(e * .5, .4, "A5") for e in range(8)],
        [(e * .5, .4, "B5") for e in range(8)],
        [(e * .5, .4, "B5" if e % 2 == 0 else "G5") for e in range(8)],
        [(e * .5, .4, "C6" if e % 2 == 0 else "A5") for e in range(8)],
        [(0, .5, "A5"), (.5, .5, "C6"), (1, 1, "F5"), (2, .5, "A5"), (2.5, .5, "C6"), (3, 1, "A5")],
        [(0, .5, "B5"), (.5, .5, "D6"), (1, 1, "G5"), (2, .5, "B5"), (2.5, .5, "D6"), (3, 1, "B5")],
        [(0, .5, "G#5"), (.5, .5, "B5"), (1, .5, "D6"), (1.5, .5, "B5"), (2, .5, "G#5"), (2.5, .5, "E5"), (3, 1, "B4")],
        [(0, 1, "E5"), (1, 1, "G#5"), (2, 1, "B5"), (3, 1, "D6")],
    ]
    for bar, chord in enumerate(chords):
        section = bar // 8
        breakdown = 16 <= bar < 20
        mel_vel = 0.62 if breakdown else 0.8
        crescendo = (bar - 16) / 4 if breakdown else 0
        notes = melody[bar]
        if breakdown:
            for beat, dur, note in notes:
                s.add(bar * 4 + beat, dur, note, (0.45 + 0.35 * (crescendo + beat / 16)), "pluck")
        else:
            add_melody(s, bar, notes, "pluck", vel=mel_vel)
            add_melody(s, bar, notes, "kalimba", vel=0.3, octave_shift=1 if section == 1 else 0)
        # 16th ostinato (marimba, low velocity) outlining the chord
        arp16(s, bar, chord, 4, "marimba", 0.36, pattern=(0, 2, 1, 2, 3, 2, 1, 2))
        # pad for tension
        for tname in CHORD_TONES[chord][:3]:
            m = midi(tone(tname, 3))
            if m < midi("E3"):
                m += 12
            s.add(bar * 4, 3.95, m, 0.42 if section != 2 else 0.55, "pad")
        # bass: driving octave eighths
        r = root_of(chord, 2)
        if r < midi("E2"):
            r += 12
        pat = [0, 0, 12, 0, 0, 12, 0, 12]
        for e, iv in enumerate(pat):
            s.add(bar * 4 + e * 0.5, 0.3, r + iv, 0.85 if e % 2 == 0 else 0.65, "bass")
        # drums
        for q in range(4):
            s.add(bar * 4 + q, 0.2, None, 0.9 if not breakdown else 0.55, "kick")
        if not breakdown:
            for q in (1, 3):
                s.add(bar * 4 + q, 0.2, None, 0.75, "snare")
                s.add(bar * 4 + q, 0.2, None, 0.35, "clap")
        for e in range(16):
            s.add(bar * 4 + e * 0.25, 0.1, None, 0.65 if e % 2 else 0.35, "hat")
        if breakdown or bar >= 20:
            for e in range(8):
                s.add(bar * 4 + e * 0.5, 0.1, fmidi(2300 if e % 2 == 0 else 1750), 0.5, "wood")
        if bar in (7, 15, 23):
            for k, f in enumerate((262, 220, 185, 147)):
                s.add(bar * 4 + 3 + k * 0.25, 0.25, fmidi(f), 0.65, "tom")
        if bar in (7, 15, 19):
            s.add(bar * 4 + 2, 2, None, 1.0, "swell")
    return s


SONGS = {"music_home": song_home, "music_game": song_game, "music_hard": song_hard}


# ============================================================================================ encoding

def find_ffmpeg():
    for p in ("/opt/homebrew/bin/ffmpeg", shutil.which("ffmpeg")):
        if p and os.path.exists(p):
            return p
    return None


def ffmpeg_has_libvorbis(ff):
    try:
        out = subprocess.run([ff, "-hide_banner", "-encoders"], capture_output=True, text=True, timeout=30).stdout
        return " libvorbis " in out
    except (OSError, subprocess.SubprocessError):
        return False


SOUNDFILE_ENCODE = (
    "import sys, soundfile as sf\n"
    "data, sr = sf.read(sys.argv[1], dtype='float32')\n"
    "sf.write(sys.argv[2], data, sr, format='OGG', subtype='VORBIS', compression_level=float(sys.argv[3]))\n"
)


def encode_ogg(wav_path, ogg_path, quality=5):
    """Vorbis VBR quality `quality` (0..10). Returns a short description of the encoder used."""
    ff = find_ffmpeg()
    if ff and ffmpeg_has_libvorbis(ff):
        subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-i", wav_path, "-c:a", "libvorbis",
                        "-q:a", str(quality), ogg_path], check=True)
        return "ffmpeg/libvorbis"
    level = 1.0 - quality / 10.0          # libsndfile: vorbis quality = 1 - compression_level
    try:
        import soundfile as sf  # noqa: F401
        subprocess.run([sys.executable, "-c", SOUNDFILE_ENCODE, wav_path, ogg_path, str(level)], check=True)
    except ImportError:
        uv = shutil.which("uv")
        if not uv:
            raise RuntimeError("No Vorbis encoder: install ffmpeg with libvorbis or `pip install soundfile`.")
        subprocess.run([uv, "run", "--quiet", "--with", "soundfile", "python", "-c", SOUNDFILE_ENCODE, wav_path,
                        ogg_path, str(level)], check=True)
    return "libsndfile/libvorbis"


def decode_audio(path):
    """Decodes any audio file to float32 (n, ch) with ffmpeg (for verification)."""
    ff = find_ffmpeg()
    if not ff:
        return None
    probe = subprocess.run([ff, "-hide_banner", "-i", path], capture_output=True, text=True).stderr
    ch = 2 if "stereo" in probe else 1
    raw = subprocess.run([ff, "-hide_banner", "-loglevel", "error", "-i", path, "-f", "f32le", "-acodec", "pcm_f32le",
                          "-ac", str(ch), "-ar", str(SR), "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, ch)


# ============================================================================================ main

def gen_sfx(names, rows):
    for i, name in enumerate(names):
        rng = np.random.default_rng(1000 + SFX_NAMES.index(name))
        x = finalize_sfx(globals()["sfx_" + name](rng))
        path = os.path.join(OUT, f"sfx_{name}.wav")
        write_wav16(path, x, rng)
        sr, y = wavfile.read(path)
        y = y.astype(np.float64) / 32768.0
        rows.append({
            "name": f"sfx_{name}", "dur": len(y) / sr, "peak": db(np.max(np.abs(y))), "lufs": lufs(y),
            # what a phone speaker reproduces (nothing much below ~300 Hz): catches sounds whose peak is all sub-bass
            "lufs_phone": lufs(sos_filter(y, "high", 300, order=4)),
            "dc": float(np.mean(y)), "edge": max(abs(y[0]), abs(y[-1])), "nan": bool(np.isnan(x).any()),
            "clip": int(np.sum(np.abs(y) >= 32767 / 32768.0)),
        })


def gen_music(names, rows, tmp):
    for name in names:
        song = SONGS[name]()
        mixed, _ = render_song(song)
        x = master(mixed)
        if np.isnan(x).any():
            raise RuntimeError(f"{name}: NaN in render")
        wav = os.path.join(tmp, name + ".wav")
        wavfile.write(wav, SR, x.astype(np.float32))
        ogg = os.path.join(OUT, name + ".ogg")
        enc = encode_ogg(wav, ogg, 5)
        dec = decode_audio(ogg)
        jump = float(np.max(np.abs(x[0] - x[-1])))
        step = float(np.percentile(np.abs(np.diff(x, axis=0)), 99.9))
        row = {
            "name": name, "bpm": song.bpm, "bars": song.bars, "dur": len(x) / SR, "lufs": lufs(x),
            "peak": db(np.max(np.abs(x))), "jump": jump, "step999": step, "enc": enc,
            "size_kb": os.path.getsize(ogg) / 1024, "events": len(song.events),
        }
        if dec is not None:
            row["dec_len_diff"] = len(dec) - len(x)
            row["dec_peak"] = db(float(np.max(np.abs(dec))))
            row["dec_jump"] = float(np.max(np.abs(dec[0] - dec[-1])))
        rows.append(row)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="sfx | music | a single name (e.g. match, music_home)")
    args = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)

    sfx = SFX_NAMES
    music = list(SONGS)
    if args.only == "sfx":
        music = []
    elif args.only == "music":
        sfx = []
    elif args.only:
        key = args.only.replace("sfx_", "")
        sfx = [key] if key in SFX_NAMES else []
        music = [args.only] if args.only in SONGS else []
        if not sfx and not music:
            sys.exit(f"unknown sound {args.only}")

    sfx_rows, music_rows = [], []
    gen_sfx(sfx, sfx_rows)
    with tempfile.TemporaryDirectory() as tmp:
        gen_music(music, music_rows, tmp)

    problems = []
    if sfx_rows:
        print(f"\n{'SFX':22s} {'dur s':>6s} {'peak dB':>8s} {'LUFS':>6s} {'>300Hz':>6s} {'DC':>9s} {'edge':>7s} {'clip':>4s}")
        for r in sfx_rows:
            print(f"{r['name']:22s} {r['dur']:6.2f} {r['peak']:8.2f} {r['lufs']:6.1f} {r['lufs_phone']:6.1f} {r['dc']:9.1e} "
                  f"{r['edge']:7.4f} {r['clip']:4d}")
            if r["nan"] or r["clip"] or abs(r["peak"] - SFX_PEAK_DB) > 0.15 or r["edge"] > 0.003 or abs(r["dc"]) > 2e-3:
                problems.append(r["name"])
    if music_rows:
        print(f"\n{'MUSIC':12s} {'bpm':>4s} {'bars':>4s} {'dur s':>6s} {'LUFS':>6s} {'peak':>6s} {'loop jump':>9s} "
              f"{'p99.9 step':>10s} {'dec Δn':>6s} {'dec peak':>8s} {'dec jump':>8s} {'KB':>6s}  encoder")
        for r in music_rows:
            print(f"{r['name']:12s} {r['bpm']:4d} {r['bars']:4d} {r['dur']:6.2f} {r['lufs']:6.1f} {r['peak']:6.2f} "
                  f"{r['jump']:9.4f} {r['step999']:10.4f} {r.get('dec_len_diff', 0):6d} {r.get('dec_peak', 0):8.2f} "
                  f"{r.get('dec_jump', 0):8.4f} {r['size_kb']:6.0f}  {r['enc']}")
            if r["jump"] > r["step999"] or abs(r["lufs"] - MUSIC_LUFS) > 1.0 or r["peak"] > -1.0:
                problems.append(r["name"])
    expected = {f"sfx_{n}.wav" for n in SFX_NAMES} | {f"{n}.ogg" for n in SONGS}
    present = {f for f in os.listdir(OUT) if f.endswith((".wav", ".ogg"))}
    missing = sorted(expected - present)
    extra = sorted(present - expected)
    if missing:
        print("missing:", missing)
    if extra:
        print("unexpected files in Audio/:", extra)
    print("\nOK" if not problems and not missing else f"\nCHECK: {problems} {missing}")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Procedural audio for Potion Pop! — every sound is synthesized here (no samples).

    uv run --with pillow --with numpy --with scipy python Tools/gen_audio.py [--only sfx|music|board|<name>] [--prune]
                                                                              [--preview]

SFX   -> Assets/_Game/Resources/Audio/sfx_<name>.wav   mono 16-bit 44.1 kHz, peak -1 dBFS, DC removed,
         smooth attack/release (no clicks), short reverb. One file per value of PotionPop.Sfx (snake_case); the list
         below is checked against the enum in Assets/_Game/Scripts/Core/AudioManager.cs on every run.
MUSIC -> Assets/_Game/Resources/Audio/music_<name>.ogg stereo Vorbis q5, seamless loops (~40 s), about -16 LUFS.
         Songs are written as chord progressions + melodies (note lists below), humanized, mixed stem by stem with
         stereo panning and a convolution reverb. Seamless looping: the loop is rendered with an extra tail and the
         tail is folded back onto the start, so the file is exactly periodic (what plays after the end is what the
         start expects).

--prune    deletes sfx_*.wav / music_*.ogg in Audio/ that no enum value uses (and their .meta files).
--preview  writes waveform + spectrogram sheets of the board sounds to ArtSource/preview/ (gitignored scratch).

Report columns: LUFS = integrated loudness (whole clip if < 0.4 s); Mmax = loudest 400 ms momentary loudness, the
fairest way to compare one-shots of different lengths (all SFX peak at -1 dBFS, so their relative loudness is set by
the design itself: the frequent board sounds sit around -17..-20, rewards/UI around -9..-14); >300Hz = what a phone
speaker reproduces. Re-running is deterministic (WAVs byte-identical; OGGs differ only by the Ogg stream serial).

Vorbis encoding uses ffmpeg with libvorbis when available (/opt/homebrew/bin/ffmpeg or PATH); otherwise libsndfile's
libvorbis through the `soundfile` package at the same quality (q5), installed on the fly with `uv run --with soundfile`
if needed (ffmpeg's built-in "vorbis" encoder is experimental and is never used).

Style: glossy and magical — glass clinks, liquid streams and bubble pops for the potions; celesta, music box, harp,
glockenspiel, bells and sparkles for the magic; pizzicato strings, soft flute and light percussion in the music.
Board sounds play constantly, so they are short, tonal and soft-edged (no harsh noise, highs rolled off), and the
ones whose pitch the code varies are low-passed so a +60 % pitch-up never aliases.
"""
import argparse
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zlib

import numpy as np
from scipy import signal
from scipy.io import wavfile
from scipy.ndimage import maximum_filter1d, minimum_filter1d, uniform_filter1d

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Game", "Resources", "Audio")
PREVIEW = os.path.join(ROOT, "ArtSource", "preview")
ENUM_SRC = os.path.join(ROOT, "Assets", "_Game", "Scripts", "Core", "AudioManager.cs")
SR = 44100
SFX_PEAK_DB = -1.0
MUSIC_LUFS = -16.0
MUSIC_CEILING_DB = -1.5      # sample-peak ceiling before encoding (leaves room for codec overshoot)

# Must match PotionPop.Sfx (Assets/_Game/Scripts/Core/AudioManager.cs) in snake_case, same order.
SFX_NAMES = [
    # UI / meta
    "click", "popup_open", "popup_close", "toggle", "error", "purchase", "pop", "sparkle", "countdown", "fanfare",
    "swoosh", "whoosh", "star", "coin", "reward", "heart", "card_flip", "chest_open", "spin_tick", "spin_win", "win",
    "lose", "combo", "unlock",
    # board (potion bottles)
    "select", "deselect", "pour", "pour_end", "complete", "invalid", "reveal", "stone_crack", "stone_break", "undo",
    "add_bottle", "wand", "shuffle", "crystal", "rainbow", "bubble",
]
BOARD_SFX = ["select", "deselect", "pour", "pour_end", "complete", "invalid", "reveal", "stone_crack", "stone_break",
             "undo", "add_bottle", "wand", "shuffle", "crystal", "rainbow", "bubble", "combo"]


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
    """Linear-attack ADSR (seconds) over n samples, note-off at gate seconds."""
    t = taxis(n)
    env = np.where(t < a, t / max(a, 1e-4), s + (1 - s) * np.exp(-(t - a) / max(d, 1e-4)))
    rel = t >= gate
    if rel.any():
        level = np.interp(gate, t, env)
        env[rel] = level * np.exp(-(t[rel] - gate) / max(r, 1e-4))
    return env


def bell_curve(n, power=2.0):
    """sin^power hump over n samples (0 at both ends)."""
    return np.sin(np.pi * np.linspace(0, 1, n)) ** power


# ============================================================================================ instruments

def modal(f, ratios, amps, taus, length, attack=0.0015, rng=None, detune=0.0, phase_rand=True):
    """Sum of exponentially decaying sine modes (mallet instruments, bells, tines, glass)."""
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


def celesta(f, vel=1.0, rng=None, length=None, decay=None):
    """Celesta: a steel bar over a wooden resonator struck by a felt hammer — round, sustained fundamental; the bar's
    inharmonic partials (2.76, 5.40, 8.93) only flash as a short shiny 'ping' at the attack. `decay` caps the ring
    time constant (a damped, tighter note for frequent sound effects)."""
    tau1 = float(np.clip(1.05 * (523.0 / f) ** 0.5, 0.3, 1.8))
    if decay:
        tau1 = min(tau1, decay)
    length = length or min(tau1 * 4.5, 3.5)
    x = modal(f, [1.0, 2.0, 2.756, 5.404, 8.933], [1.0, 0.06, 0.32, 0.12, 0.04],
              [tau1, tau1 * 0.3, 0.07, 0.035, 0.018], length, attack=0.0012, rng=rng)
    m = samples(0.004)
    x[:m] += mallet_noise(0.004, 4000, rng, tau=0.001) * 0.05
    return x * vel


def music_box(f, vel=1.0, rng=None, length=None):
    """Music-box tine (steel cantilever, clamped-free modes 1 : 6.27 : 17.55): pure fundamental with a glassy
    overtone that dies fast, a faint pin tick, and a sympathetic neighbour tine ~1 Hz off (the slow shimmer)."""
    tau1 = float(np.clip(1.4 * (523.0 / f) ** 0.6, 0.35, 2.4))
    length = length or min(tau1 * 4.5, 4.0)
    hi = 1.0 / (1.0 + (f / 2200.0) ** 2)          # the 6.27x overtone fades out for the highest tines
    x = modal(f, [1.0, 6.267, 17.55], [1.0, 0.5 * hi, 0.1 * hi], [tau1, tau1 / 7, tau1 / 25], length,
              attack=0.0004, rng=rng)
    x += 0.3 * modal(f + rng.uniform(0.7, 1.4), [1.0], [1.0], [tau1 * 1.1], length, attack=0.003, rng=rng)
    m = samples(0.0025)
    x[:m] += sos_filter(rng.standard_normal(m), "high", 5000) * exp_env(m, 0.0004, 0.0001) * 0.08
    return x * vel


def harp(f, vel=1.0, rng=None, length=None, pos=0.3, damp=None):
    """Concert-harp pluck: harmonic partials with a mid-string pluck spectrum, highs decaying faster, a touch of
    string stiffness, a soft fingertip onset; long natural ring (~2 s in the middle register). `damp` (s): the
    harpist's hand stops the string there (so a chord change doesn't ring into the next harmony)."""
    T0 = float(np.clip(1.9 * (262.0 / f) ** 0.55, 0.45, 3.6))
    length = length or min(T0 * 3.2, 4.0)
    if damp is not None:
        length = min(length, damp + 0.45)
    n = samples(length)
    t = taxis(n)
    K = int(max(1, min(16, 7500 // f)))
    out = np.zeros(n)
    for k in range(1, K + 1):
        a = (abs(math.sin(math.pi * k * pos)) + 0.05) / k ** 1.6
        tau = T0 / (1 + 0.5 * (k - 1) ** 1.2)
        m = min(n, samples(tau * 7))
        fk = f * k * math.sqrt(1 + 0.00008 * k * k)
        part = a * np.exp(-t[:m] / tau) * np.sin(2 * np.pi * fk * t[:m] + rng.uniform(0, 2 * np.pi))
        out[:m] += fade(part, 0, min(0.01, tau))
    a_n = samples(0.0018)
    out[:a_n] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a_n))
    if damp is not None:
        out *= np.exp(-np.maximum(t - damp, 0.0) / 0.07)
    return fade(out, 0, min(0.2, length * 0.25)) * vel


def body_gain(fk, scale=1.0):
    """Violin-family body response: air mode, main wood modes and the bridge hill (shifted down by `scale` for the
    bigger instruments)."""
    lf = math.log2(max(fk, 20.0))
    g = 0.3
    for fc, w, a in ((285.0, 0.28, 0.9), (520.0, 0.35, 0.65), (2600.0, 0.55, 0.5)):
        g += a * math.exp(-0.5 * ((lf - math.log2(fc * scale)) / w) ** 2)
    return g * fk / (fk + 200.0 * scale)


def pizz(f, vel=1.0, rng=None, voices=3, tau=None, pos=0.24, spread=0.012, cents=6.0, body=1.0):
    """Pizzicato string section: plucked harmonics through a body response, fast decay of the highs, a soft
    fingertip onset; several players slightly out of time and tune (that's what makes it a section)."""
    T0 = tau or float(np.clip(0.36 * (196.0 / f) ** 0.5, 0.1, 1.0))
    length = T0 * 5.5 + spread
    n = samples(length)
    t = taxis(n)
    K = int(max(1, min(18, 7000 // f)))
    a_n = samples(0.0025)
    out = np.zeros(n)
    for _ in range(voices):
        det = 2 ** (rng.normal(0, cents) / 1200.0) if voices > 1 else 1.0
        d = samples(rng.uniform(0, spread)) if voices > 1 else 0
        m = n - d
        voice = np.zeros(m)
        for k in range(1, K + 1):
            fk = f * det * k
            a = (abs(math.sin(math.pi * k * pos)) + 0.03) / k ** 1.25 * body_gain(fk, body)
            tk = T0 / (1 + 0.85 * (k - 1))
            mm = min(m, samples(tk * 8))
            part = a * np.exp(-t[:mm] / tk) * np.sin(2 * np.pi * fk * t[:mm] + rng.uniform(0, 2 * np.pi))
            voice[:mm] += fade(part, 0, min(0.008, tk))
        voice[:a_n] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a_n))
        out[d:] += voice * rng.uniform(0.8, 1.0)
    return fade(out, 0, 0.02) * vel / math.sqrt(voices)


def pizz_bass(f, vel=1.0, rng=None):
    """Cello / double-bass pizzicato: two players, longer ring, lower body resonances."""
    T0 = float(np.clip(0.8 * (82.0 / f) ** 0.35, 0.35, 1.1))
    return pizz(f, vel, rng, voices=2, tau=T0, pos=0.28, spread=0.005, cents=3.0, body=0.38)


def bell(f, vel=1.0, rng=None, length=1.2, ratio=3.5, index=2.2):
    """FM bell: carrier + inharmonic modulator with decaying index (bright attack, pure tail)."""
    n = samples(length)
    t = taxis(n)
    idx = index * np.exp(-t / (length * 0.18))
    x = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * ratio * t)) * exp_env(n, length * 0.3, 0.001)
    return fade(x, 0, min(0.15, length * 0.3)) * vel


def glass(f, vel=1.0, rng=None, length=0.25, tau=0.08, bright=1.0, split=0.0035, tick=0.12):
    """Glass tap: bending modes of a thin cylindrical shell (n = 2..5 -> 1 : 2.83 : 5.42 : 8.77), each a slightly
    split degenerate pair (no bottle is perfectly round -> soft beating), upper modes decaying faster, plus a crisp
    contact tick. `bright` < 1 gives a muted tap."""
    ratios = [1.0, 2.828, 5.423, 8.77]
    amps = [1.0, 0.42 * bright, 0.18 * bright, 0.07 * bright]
    taus = [tau, tau * 0.5, tau * 0.28, tau * 0.16]
    x = modal(f, ratios, amps, taus, length, attack=0.0005, rng=rng)
    x += 0.55 * modal(f * (1 + split), ratios, amps, taus, length, attack=0.0005, rng=rng)
    m = min(samples(0.004), len(x))
    x[:m] += sos_filter(rng.standard_normal(m), "high", 2500) * exp_env(m, 0.0006, 0.0001) * tick * bright
    return x * (vel / 1.55)


def glass_harmonica(f, dur, vel=1.0, rng=None, attack=0.07, release=0.35, beat_hz=1.6):
    """Rubbed-glass tone: nearly pure sine with a soft bowed attack; a twin ~1.6 Hz away makes it shimmer."""
    n = samples(dur + release * 3)
    t = taxis(n)
    env = adsr(n, attack, 0.5, 0.7, release, dur)
    x = np.zeros(n)
    for df, a in ((0.0, 1.0), (beat_hz, 0.75)):
        ph = rng.uniform(0, 2 * np.pi)
        w = 2 * np.pi * (f + df) * t + ph
        x += a * (np.sin(w) + 0.10 * np.sin(2 * w) + 0.035 * np.sin(3 * w))
    return fade(x * env, 0.004, 0.05) * vel / 1.9


def soft_flute(f, dur, vel=1.0, rng=None):
    """Soft concert-flute tone: strong fundamental, a little 2nd/3rd harmonic, breath noise around the harmonics, a
    short breathy 'chiff' at the onset, a slight scoop and a delayed, gentle vibrato."""
    n = samples(dur + 0.35)
    t = taxis(n)
    env = adsr(n, 0.08, 0.35, 0.88, 0.14, dur)
    vib = 11 * np.sin(2 * np.pi * 5.1 * t + rng.uniform(0, 6.28)) * np.clip((t - 0.22) / 0.35, 0, 1)
    scoop = -18 * np.exp(-t / 0.04)
    phase = 2 * np.pi * np.cumsum(f * 2 ** ((vib + scoop) / 1200.0)) / SR
    x = np.sin(phase) + 0.28 * np.sin(2 * phase + 0.4) + 0.09 * np.sin(3 * phase + 1.1) + 0.035 * np.sin(4 * phase)
    x *= 1 + 0.04 * np.sin(2 * np.pi * 5.1 * t)
    breath = sos_filter(rng.standard_normal(n), "band", [min(f * 1.5, 9000), min(f * 7, 16000)]) * 0.045
    chiff = sos_filter(rng.standard_normal(n), "band", [min(f * 2, 9000), min(f * 9, 18000)]) * exp_env(n, 0.025, 0.004)
    return fade((x + breath) * env + chiff * 0.25 * np.minimum(1.0, env * 3), 0.004, 0.04) * vel


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


def pad(f, dur, vel=1.0, rng=None):
    """Warm detuned string pad (stereo), slow attack and release."""
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


# ----------------------------------------------------------------------------------------- percussion

def kick(vel=1.0, rng=None, soft=False):
    n = samples(0.45)
    t = taxis(n)
    f = 48 + (130 if soft else 150) * np.exp(-t / 0.035)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * exp_env(n, 0.16 if soft else 0.2, 0.001)
    x[:samples(0.004)] += sos_filter(rng.standard_normal(samples(0.004)), "low", 3000) * (0.08 if soft else 0.15)
    return fade(x, 0.0005, 0.03) * vel


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


def triangle(vel=1.0, rng=None, length=1.4):
    """Orchestral triangle 'ting': a dense set of high inharmonic modes with a long ring."""
    x = modal(1180.0, [1.0, 2.71, 3.86, 5.02, 6.37, 7.7, 9.13], [0.45, 1.0, 0.75, 0.6, 0.5, 0.38, 0.28],
              [0.8, 0.75, 0.65, 0.55, 0.45, 0.38, 0.3], length, attack=0.0004, rng=rng)
    m = samples(0.003)
    x[:m] += sos_filter(rng.standard_normal(m), "high", 5000) * exp_env(m, 0.0005, 0.0001) * 0.2
    return fade(sos_filter(x, "high", 1500), 0.0005, 0.05) * vel * 0.5


def mark_tree(rng, count=18, span=0.45, lo=2300.0, hi=7000.0, up=True, vel=1.0):
    """Mark-tree (bar chimes) glissando: a quick cascade of small rods, each a free-bar modal ping."""
    buf = np.zeros(samples(span + 1.2))
    fs = np.geomspace(lo, hi, count)
    if not up:
        fs = fs[::-1]
    for i, f in enumerate(fs):
        tt = span * (i / (count - 1)) ** 1.15
        tau = rng.uniform(0.35, 0.7) * (3000.0 / f) ** 0.3
        ping = modal(f * rng.uniform(0.985, 1.015), [1.0, 2.756, 5.404], [1.0, 0.22, 0.06],
                     [tau, tau * 0.35, tau * 0.15], min(tau * 5, 1.1), attack=0.0006, rng=rng)
        place(buf, ping, tt, vel * rng.uniform(0.55, 1.0))
    return buf


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
    """Bubble 'bloop': a damped sine whose pitch glides f0 -> f1 (the Minnaert resonance of an air bubble rises as it
    nears the surface — the classic water-drop sound)."""
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


def cork_thup(rng):
    """A cork pressed into the neck: a muffled low 'thoop' (sine dropping fast), a hollow neck resonance 'pok' and a
    felt-like puff of noise. Kept in the low-mids so it still reads on a phone speaker."""
    b = np.zeros(samples(0.16))
    n = samples(0.14)
    t = taxis(n)
    f = 210 + 420 * np.exp(-t / 0.01)                                     # 'thoop': 630 -> 210 Hz
    place(b, np.sin(2 * np.pi * np.cumsum(f) / SR) * exp_env(n, 0.03, 0.0012), 0, 0.85)
    place(b, modal(720, [1.0, 1.52, 2.3], [1.0, 0.35, 0.15], [0.02, 0.011, 0.006], 0.1, attack=0.0008, rng=rng),
          0.001, 0.5)
    puff = sos_filter(rng.standard_normal(n), "band", [300, 1800]) * exp_env(n, 0.007, 0.0008)
    place(b, puff, 0, 0.2)
    return fade(b, 0.0008, 0.02)


def rock(f, vel=1.0, rng=None, tau=0.015):
    """Rock chip: a few inharmonic, heavily damped modes (randomized per chip) plus a gritty noise attack."""
    r = [1.0, 1.0 + rng.uniform(0.4, 0.75), 2.0 + rng.uniform(0.1, 0.7), 3.0 + rng.uniform(0.2, 1.0)]
    x = modal(f, r, [1.0, 0.7, 0.45, 0.25], [tau, tau * 0.75, tau * 0.55, tau * 0.4], tau * 8 + 0.01,
              attack=0.0003, rng=rng)
    m = min(samples(0.006), len(x))
    lo = min(f * 1.5, 6000.0)
    grit = sos_filter(rng.standard_normal(m), "band", [lo, min(f * 6, 16000.0)]) * exp_env(m, 0.0015, 0.0002)
    x[:m] += grit * 0.35
    return x * vel


def fracture(rng, count=6, span=0.016, lo=1400, hi=7500, tau=0.0025):
    """A crack running through stone: a quick train of micro-impulses, each a tiny band-passed noise burst."""
    n = samples(span + 0.03)
    t = taxis(n)
    env = np.zeros(n)
    times = np.sort(rng.uniform(0, span, count))
    times[0] = 0.0
    for i, t0 in enumerate(times):
        tt = t - t0
        env += np.where(tt >= 0, rng.uniform(0.5, 1.0) * 0.8 ** i * np.exp(-np.maximum(tt, 0) / tau), 0.0)
    x = sos_filter(rng.standard_normal(n), "band", [lo, hi]) * env
    return fade(x, 0.0002, 0.005)


def flutter_env(n, rng, lo=5.0, hi=22.0, depth=0.35):
    """Slow random amplitude flutter (turbulence of a liquid stream), mean 1."""
    x = sos_filter(rng.standard_normal(n + SR // 2), "band", [lo, hi])[SR // 2:]
    return np.clip(1 + depth * x / (np.std(x) + 1e-9), 0.3, 1.8)


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


def momentary_max(x):
    """Loudest 400 ms momentary loudness (BS.1770, mono), clip zero-padded: a fair way to compare one-shots of
    different lengths (a 0.1 s tick counts as quieter than a 0.4 s sound of the same level, as we hear it)."""
    y = k_weight(np.concatenate([np.zeros(samples(0.4)), x, np.zeros(samples(0.4))]))
    ms = uniform_filter1d(y ** 2, samples(0.4), mode="constant")
    return -0.691 + 10 * math.log10(max(float(ms.max()), 1e-12))


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


def trim_tail(x, thresh_db=-56):
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


def snake_case(pascal):
    """Same rule as AudioManager.ToSnakeCase: 'StoneCrack' -> 'stone_crack'."""
    out = []
    for i, c in enumerate(pascal):
        if c.isupper():
            if i > 0 and (pascal[i - 1].islower() or pascal[i - 1].isdigit() or
                          (i + 1 < len(pascal) and pascal[i + 1].islower() and pascal[i - 1].isupper())):
                out.append("_")
            out.append(c.lower())
        else:
            out.append(c)
    return "".join(out)


def enum_names(kind):
    """snake_case names of `enum <kind>` in AudioManager.cs, or None if it can't be read."""
    try:
        with open(ENUM_SRC, encoding="utf-8") as fh:
            src = fh.read()
    except OSError:
        return None
    m = re.search(r"\benum\s+" + kind + r"\s*\{(.*?)\}", src, re.S)
    if not m:
        return None
    body = re.sub(r"/\*.*?\*/", "", re.sub(r"//[^\n]*", "", m.group(1)), flags=re.S)
    return [snake_case(p.split("=")[0].strip()) for p in body.split(",") if p.strip()]


# ============================================================================================ SFX designs: UI / meta

def sfx_click(rng):
    b = np.zeros(samples(0.2))
    place(b, marimba(hz("G6"), 0.9, rng, length=0.12), 0)
    place(b, bubble(1100, 1700, 0.03, tau=0.012), 0.002, 0.35)
    return b


def sfx_popup_open(rng):
    b = np.zeros(samples(0.8))
    place(b, sweep_tone(330, 980, 0.16, tau=0.12, harm=(0.2,)), 0, 0.7)
    place(b, celesta(hz("C6"), 0.6, rng, length=0.6), 0.05)
    place(b, glock(hz("G6"), 0.4, rng, length=0.6), 0.12)
    place(b, celesta(hz("G6"), 0.35, rng, length=0.6), 0.12)
    place(b, sparkle(0.35, 4, rng, vel=0.25, start=0.12), 0.0)
    return reverb_mono(b, rng, 0.5, 0.18)


def sfx_popup_close(rng):
    b = np.zeros(samples(0.6))
    place(b, sweep_tone(900, 360, 0.13, tau=0.09, harm=(0.2,)), 0, 0.7)
    place(b, marimba(hz("G5"), 0.6, rng, length=0.3), 0.0)
    place(b, marimba(hz("C5"), 0.7, rng, length=0.4), 0.065)
    return reverb_mono(b, rng, 0.4, 0.12)


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
    """Short 'ta-da-daa!' (~1.7 s) for area unlocked / rewards: soft brass with a harp glissando sweeping into the
    chord, celesta and glockenspiel sparkle on top."""
    b = np.zeros(samples(2.4))
    place(b, brass(hz("G4"), 0.1, 0.5, rng), 0.0)
    place(b, brass(hz("C5"), 0.1, 0.5, rng), 0.12)
    for note in ("E5", "G5", "C6"):
        place(b, brass(hz(note), 0.95, 0.35, rng), 0.26)
    for note in ("C4", "G4"):
        place(b, brass(hz(note), 0.95, 0.2, rng, vibrato=False), 0.26)
    place(b, kick(0.5, rng, soft=True), 0.26)
    for i, note in enumerate(("C5", "E5", "G5", "C6", "E6", "G6")):
        place(b, harp(hz(note), 0.3 + 0.03 * i, rng, length=1.0), 0.1 + i * 0.026)
    for i, note in enumerate(("G6", "C7", "E7")):
        place(b, glock(hz(note), 0.3, rng, length=0.9), 0.28 + i * 0.05)
        place(b, celesta(hz(note) / 2, 0.35, rng, length=0.9), 0.28 + i * 0.05)
    place(b, sparkle(1.2, 10, rng, vel=0.2, start=0.35), 0.0)
    return reverb_mono(sos_filter(b, "low", 9000), rng, 0.9, 0.2)


def sfx_swoosh(rng):
    dur = 0.26
    n = samples(dur)
    x = filtered_noise(dur, lambda t: 1000 * (4500 / 1000) ** (t / dur), 0.9, rng)
    t = np.linspace(0, 1, n)
    env = np.minimum(t / 0.25, 1) * (1 - t) ** 1.5
    return reverb_mono(x * env, rng, 0.3, 0.1)


def sfx_whoosh(rng):
    dur = 0.48
    n = samples(dur)
    x = filtered_noise(dur, lambda t: 300 * (1800 / 300) ** np.sin(np.pi * 0.85 * t / dur), 1.1, rng)
    env = np.sin(np.pi * np.linspace(0, 1, n) ** 0.8) ** 2
    return reverb_mono(sos_filter(x * env, "low", 6000), rng, 0.4, 0.12)


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
    b = sos_filter(b, "low", 12000, order=4)       # the coin flights pitch it up to x1.36: keep the top clean
    return reverb_mono(b, rng, 0.45, 0.14)


def sfx_reward(rng):
    b = np.zeros(samples(2.0))
    n = samples(0.32)
    place(b, filtered_noise(0.32, lambda t: 1500 * (6000 / 1500) ** (t / 0.32), 0.7, rng) * np.linspace(0, 1, n) ** 2,
          0, 0.08)
    for i, note in enumerate(("G4", "C5", "E5", "G5", "C6")):
        place(b, harp(hz(note), 0.3 + 0.04 * i, rng, length=1.4), 0.16 + i * 0.028)
    for note in ("C6", "E6", "G6"):
        place(b, glock(hz(note), 0.5, rng, length=1.3), 0.3)
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


def sfx_card_flip(rng):
    b = np.zeros(samples(0.25))
    for t0, v in ((0.0, 1.0), (0.045, 0.6)):
        n = samples(0.05)
        place(b, sos_filter(rng.standard_normal(n), "high", 2000) * exp_env(n, 0.012, 0.0008), t0, v * 0.6)
    place(b, sweep_tone(200, 150, 0.05, tau=0.02), 0.04, 0.4)
    return b


def sfx_chest_open(rng):
    b = np.zeros(samples(2.0))
    place(b, sweep_tone(150, 90, 0.12, tau=0.06), 0, 0.7)
    place(b, marimba(hz("C4"), 0.6, rng, length=0.3), 0)
    for i, note in enumerate(("C5", "E5", "G5", "C6", "E6", "G6", "C7")):
        place(b, harp(hz(note), 0.28 + 0.03 * i, rng, length=1.2), 0.06 + i * 0.024)
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


def sfx_win(rng):
    """~3.5 s joyful fanfare: brass 'da-da-da-daaa, da-daaaa', a harp glissando into the final chord, glockenspiel
    and celesta runs, timpani-ish kicks, sparkle shower."""
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
    for i, note in enumerate(("C4", "E4", "G4", "C5", "E5", "G5", "C6", "E6", "G6")):
        place(b, harp(hz(note), 0.25 + 0.03 * i, rng, length=1.6), 0.82 + i * 0.026)
    for i, note in enumerate(("C6", "E6", "G6", "C7", "E7", "G7")):
        place(b, glock(hz(note), 0.4, rng, length=1.0), 1.08 + i * 0.045)
        place(b, celesta(hz(note) / 2, 0.35, rng, length=1.0), 1.08 + i * 0.045)
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


def sfx_combo(rng):
    """Praise sting for 'Great! / Amazing!' (~1.1 s): an airy whoosh and a harp glissando sweeping up into a bright
    celesta + glockenspiel arpeggio (C6 E6 G6 C7 E7), a bell chord and a sparkle shower."""
    b = np.zeros(samples(1.9))
    dur = 0.32
    n = samples(dur)
    place(b, filtered_noise(dur, lambda t: 900 * (6000 / 900) ** (t / dur), 0.6, rng) * np.linspace(0, 1, n) ** 2,
          0, 0.06)
    for i, note in enumerate(("C5", "D5", "E5", "G5", "A5", "C6", "D6", "E6", "G6", "A6")):
        place(b, harp(hz(note), 0.26 + 0.025 * i, rng, length=1.0), i * 0.022)
    for i, note in enumerate(("C6", "E6", "G6", "C7", "E7")):
        t0 = 0.22 + i * 0.045
        place(b, celesta(hz(note), 0.55 + 0.05 * i, rng, length=0.9), t0)
        place(b, glock(hz(note), 0.25 + 0.04 * i, rng, length=0.8), t0)
    for note in ("C7", "E7", "G7"):
        place(b, bell(hz(note), 0.13, rng, length=1.0), 0.42)
    place(b, sparkle(1.0, 12, rng, vel=0.24, start=0.4), 0.0)
    place(b, shimmer(0.8, rng, rise=0.2), 0.3, 0.03)
    return reverb_mono(sos_filter(b, "low", 11000), rng, 0.9, 0.24)


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


# ============================================================================================ SFX designs: board
# These play all the time: short, tonal, soft attacks on the noisy parts, highs rolled off. Most are in C major
# (C E G) so they sit with the game music (C major / A minor). The code varies the pitch of several of them (the
# complete chime up to +60 %), hence the low-passes: nothing important above ~11 kHz.

def sfx_select(rng):
    """Bottle lifted (~0.13 s): a light glass 'tink' (G6, split modes shimmer) with a tiny upward bloop for 'lift'."""
    b = np.zeros(samples(0.22))
    place(b, glass(hz("G6"), 1.0, rng, length=0.16, tau=0.05), 0)
    place(b, bubble(620, 1050, 0.04, tau=0.014, harm=0.05), 0.004, 0.22)
    b = sos_filter(sos_filter(b, "low", 9000), "high", 250)
    return reverb_mono(b, rng, 0.3, 0.07)


def sfx_deselect(rng):
    """Bottle put back down (~0.12 s): a lower, softer glass tap (D6, upper modes muted) over a small 'tuk' of the
    base touching the shelf, with a faint falling bloop."""
    b = np.zeros(samples(0.22))
    place(b, sos_filter(glass(hz("D6"), 0.8, rng, length=0.14, tau=0.03, bright=0.5), "low", 6000), 0.006)
    place(b, modal(380, [1.0, 1.73, 2.41], [1.0, 0.45, 0.2], [0.011, 0.008, 0.005], 0.08, attack=0.0008, rng=rng),
          0, 0.4)
    place(b, bubble(900, 640, 0.035, tau=0.012, harm=0.05), 0.0, 0.1)
    b = sos_filter(b, "high", 200)
    return reverb_mono(b, rng, 0.3, 0.06)


def sfx_pour(rng):
    """Liquid pouring into a bottle (~0.62 s, plays as each pour starts). A first 'blup' as the stream lands, then a
    soft band-passed noise stream with turbulent flutter whose air-column resonance (+ its 3rd harmonic, as in a
    quarter-wave tube) rises as the bottle fills — that rising 'fill' pitch is the cue — sprinkled with small bubble
    pops and two soft glugs; smooth cosine fade-out."""
    dur = 0.62
    n = samples(dur)
    t = taxis(n)
    b = np.zeros(samples(dur + 0.1))
    env = np.minimum(t / 0.04, 1.0)
    rel = dur - 0.26
    env *= np.where(t < rel, 1.0, 0.5 + 0.5 * np.cos(np.pi * np.clip((t - rel) / 0.26, 0, 1)))
    flutter = flutter_env(n, rng, depth=0.3)
    stream = filtered_noise(dur, lambda tt: 1100 * (1700 / 1100) ** (tt / dur), 0.8, rng)
    place(b, sos_filter(stream, "low", 4500) * env * flutter, 0, 0.05)
    f_res = lambda tt: 560 * (1 + 0.55 * (tt / dur))       # 560 -> 870 Hz: the air column shortens as it fills
    res = (filtered_noise(dur, f_res, 0.06, rng, nfft=2048, hop=256)
           + 0.35 * filtered_noise(dur, lambda tt: 3 * f_res(tt), 0.05, rng, nfft=2048, hop=256))
    place(b, res * env * flutter, 0, 0.06)
    place(b, bubble(520, 1050, 0.06, tau=0.02, harm=0.08), 0.012, 1.0)                 # the stream lands
    for t0, f0, g in ((0.13, 380, 0.38), (0.25, 430, 0.28)):                          # glugs
        place(b, bubble(f0, f0 * 1.7, 0.06, tau=0.022, harm=0.1), t0, g)
    tt = 0.03
    while True:                                                                        # small bubbles
        tt += rng.exponential(1 / 50)
        if tt > dur - 0.12:
            break
        f0 = math.exp(rng.uniform(math.log(800), math.log(2600)))
        tau = rng.uniform(0.006, 0.016)
        g = 0.22 * rng.uniform(0.3, 1.0) * float(np.interp(tt, [0, 0.1, dur - 0.25, dur], [0.6, 1, 1, 0.2]))
        place(b, bubble(f0, f0 * rng.uniform(1.2, 1.6), tau * 4, tau=tau, harm=0.05), tt, g)
    b = sos_filter(sos_filter(b, "high", 170), "low", 7000)
    return reverb_mono(b, rng, 0.35, 0.1)


def sfx_pour_end(rng):
    """Liquid settles in the target (~0.25 s): a soft splash that darkens as it settles, two droplets and a few tiny
    bubbles."""
    b = np.zeros(samples(0.34))
    n = samples(0.12)
    place(b, filtered_noise(0.12, lambda tt: 2600 * (1200 / 2600) ** (tt / 0.12), 1.0, rng) * exp_env(n, 0.03, 0.003),
          0, 0.16)
    m = samples(0.2)
    place(b, filtered_noise(0.2, lambda tt: 600 + 0 * tt, 0.6, rng) * exp_env(m, 0.06, 0.01), 0, 0.08)
    place(b, bubble(900, 1500, 0.05, tau=0.016, harm=0.06), 0.004, 0.7)
    place(b, bubble(1250, 2000, 0.04, tau=0.012, harm=0.05), 0.06, 0.45)
    place(b, bubble(1600, 2500, 0.035, tau=0.009), 0.12, 0.25)
    for _ in range(4):
        f0 = rng.uniform(1000, 2400)
        place(b, bubble(f0, f0 * 1.4, 0.03, tau=0.007, harm=0.0), rng.uniform(0.03, 0.2), 0.12)
    b = sos_filter(sos_filter(b, "high", 200), "low", 8000)
    return reverb_mono(b, rng, 0.35, 0.1)


def sfx_complete(rng):
    """Bottle completed (~0.75 s): a muffled cork 'thup', then a quick celesta + glockenspiel arpeggio (C6 E6 G6 C7)
    with a bell and sparkles. Purely consonant, so it stays musical when the code raises its pitch per combo step."""
    b = np.zeros(samples(1.3))
    place(b, cork_thup(rng), 0, 1.5)
    for i, (note, v) in enumerate((("C6", 0.36), ("E6", 0.44), ("G6", 0.54), ("C7", 0.72))):   # crescendo
        t0 = 0.075 + i * 0.048
        place(b, celesta(hz(note), v, rng, length=0.6, decay=0.22), t0)
        place(b, glock(hz(note), v * 0.25, rng, length=0.35), t0)
    place(b, bell(hz("C7"), 0.13, rng, length=0.6), 0.22)
    place(b, bell(hz("G7"), 0.05, rng, length=0.5), 0.24)
    place(b, sparkle(0.55, 6, rng, vel=0.18, start=0.22), 0.0)
    place(b, shimmer(0.45, rng, rise=0.08), 0.18, 0.02)
    b = sos_filter(b, "low", 11000, order=4)       # pitched up to x1.6 by the combo: nothing near Nyquist
    return reverb_mono(b, rng, 0.75, 0.2)


def sfx_invalid(rng):
    """Gentle 'nope' (~0.3 s): two muted glass taps a minor third down (G6, E6) on two soft low 'uh-uh' tones."""
    b = np.zeros(samples(0.45))
    for t0, note, f0, g in ((0.0, "G6", 300, 0.8), (0.11, "E6", 250, 0.72)):
        tap = sos_filter(glass(hz(note), 1.0, rng, length=0.09, tau=0.016, bright=0.5), "low", 3500)
        place(b, tap, t0, 0.5 * g)
        place(b, sweep_tone(f0, f0 * 0.82, 0.1, tau=0.045, harm=(0.35, 0.12)), t0, 0.65 * g)
    b = sos_filter(b, "low", 3000)
    return reverb_mono(b, rng, 0.3, 0.07)


def sfx_reveal(rng):
    """A hidden '?' layer shows its colour (~0.4 s): a shimmering rising swish (tremolo on band-passed noise), a few
    tiny pings, a liquid bloop and a soft celesta/glock bloom (G6 + D7)."""
    b = np.zeros(samples(0.9))
    dur = 0.34
    n = samples(dur)
    t = taxis(n)
    sw = filtered_noise(dur, lambda tt: 1300 * (6500 / 1300) ** (tt / dur), 0.55, rng)
    place(b, sw * bell_curve(n) * (0.7 + 0.3 * np.sin(2 * np.pi * 26 * t)), 0, 0.05)
    place(b, bubble(700, 1300, 0.045, tau=0.015), 0.14, 0.5)
    place(b, celesta(hz("G6"), 0.22, rng, length=0.45, decay=0.18), 0.16)
    place(b, glock(hz("D7"), 0.08, rng, length=0.3), 0.2)
    place(b, sparkle(0.35, 5, rng, vel=0.14, start=0.06), 0.0)
    b = sos_filter(b, "low", 11000)
    return reverb_mono(b, rng, 0.6, 0.2)


def sfx_stone_crack(rng):
    """The stone shell cracks as its counter ticks down (~0.25 s): a sharp crackle (micro-fracture train) on a dry
    rocky knock with a bit of weight, then a little grit trickling."""
    b = np.zeros(samples(0.3))
    crack = fracture(rng, count=9, span=0.045, lo=1200, hi=6500, tau=0.004)
    place(b, np.tanh(2.5 * crack / np.max(np.abs(crack))), 0, 0.55)                  # crunchy 'krk'
    place(b, rock(900, 1.0, rng, tau=0.03), 0.001, 0.8)
    place(b, rock(520, 1.0, rng, tau=0.04), 0.0, 0.6)
    place(b, sweep_tone(170, 110, 0.06, tau=0.022), 0, 0.25)
    for _ in range(6):
        place(b, rock(rng.uniform(2500, 5000), 1.0, rng, tau=0.004), rng.uniform(0.05, 0.17), 0.15)
    b = np.tanh(1.6 * b / np.max(np.abs(b)))                                            # glue the transient
    b = sos_filter(b, "low", 10000)
    return reverb_mono(b, rng, 0.25, 0.06)


def sfx_stone_break(rng):
    """The stone shatters and frees the bottle (~0.8 s): a heavy crack and thump, a clatter of rock chunks whose
    density decays like bouncing debris, then a magical release — a rising shimmer, a celesta arpeggio, a bell and
    sparkles."""
    b = np.zeros(samples(1.6))
    place(b, fracture(rng, count=10, span=0.03, lo=900, hi=6500), 0, 1.0)
    place(b, sweep_tone(150, 70, 0.18, tau=0.06), 0, 0.5)
    place(b, rock(380, 1.0, rng, tau=0.035), 0, 0.9)
    place(b, rock(640, 1.0, rng, tau=0.025), 0.004, 0.6)
    m = samples(0.25)
    burst = filtered_noise(0.25, lambda tt: 1800 * (500 / 1800) ** (tt / 0.25), 1.2, rng) * exp_env(m, 0.05, 0.001)
    place(b, burst, 0, 0.3)
    for _ in range(22):
        tt = 0.03 + 0.55 * rng.beta(1.2, 3.0)
        f = math.exp(rng.uniform(math.log(700), math.log(4200)))
        place(b, rock(f, 1.0, rng, tau=rng.uniform(0.006, 0.02)), tt, 0.35 * rng.uniform(0.3, 1.0) * (1 - tt))
    m = samples(0.6)
    rise = filtered_noise(0.6, lambda tt: 1500 * (8000 / 1500) ** (tt / 0.6), 0.6, rng) * bell_curve(m, 1.5)
    place(b, rise, 0.12, 0.06)
    for i, note in enumerate(("G5", "C6", "E6", "G6", "C7")):
        place(b, celesta(hz(note), 0.5 + 0.05 * i, rng, length=0.8), 0.16 + i * 0.045)
    place(b, bell(hz("E7"), 0.2, rng, length=0.9), 0.36)
    place(b, sparkle(0.7, 9, rng, vel=0.22, start=0.25), 0.0)
    b = sos_filter(b, "low", 10000)
    return reverb_mono(b, rng, 0.7, 0.18)


def sfx_undo(rng):
    """Take back a pour (~0.4 s): a short reverse whoosh (noise swelling while its band slides down, like a rewind)
    landing on a soft descending 'bwip' blip with a quiet celesta anchor."""
    b = np.zeros(samples(0.6))
    dur = 0.26
    n = samples(dur)
    t = taxis(n)
    x = filtered_noise(dur, lambda tt: 4200 * (1100 / 4200) ** (tt / dur), 0.7, rng)
    place(b, fade(x * (t / dur) ** 2.2, 0.002, 0.015), 0, 0.16)
    place(b, sweep_tone(1500, 560, 0.13, tau=0.07, harm=(0.12,)), 0.235, 0.55)
    place(b, celesta(hz("G5"), 0.3, rng, length=0.35), 0.25)
    b = sos_filter(b, "low", 10000)
    return reverb_mono(b, rng, 0.35, 0.1)


def sfx_add_bottle(rng):
    """An extra bottle appears (~0.6 s): a round glass 'pop' (bubble bloop + bright glass ting) with a little
    celesta note and sparkle."""
    b = np.zeros(samples(1.0))
    place(b, bubble(240, 820, 0.07, tau=0.026, harm=0.12), 0, 0.9)
    place(b, glass(hz("C7"), 0.8, rng, length=0.5, tau=0.16, bright=0.8), 0.035)
    place(b, celesta(hz("G6"), 0.35, rng, length=0.6), 0.035)
    place(b, sparkle(0.45, 5, rng, vel=0.22, start=0.08), 0.0)
    place(b, shimmer(0.45, rng, rise=0.06), 0.05, 0.03)
    b = sos_filter(b, "low", 11000)
    return reverb_mono(b, rng, 0.6, 0.2)


def sfx_wand(rng):
    """Magic wand (~1 s): an airy whoosh sweeping up, a harp glissando into a rising celesta run, a bell twinkle and a
    trail of sparkles."""
    b = np.zeros(samples(1.8))
    dur = 0.55
    n = samples(dur)
    place(b, filtered_noise(dur, lambda tt: 700 * (7500 / 700) ** (tt / dur), 0.6, rng) * bell_curve(n, 1.5), 0, 0.09)
    for i, note in enumerate(("C5", "D5", "E5", "G5", "A5", "C6", "D6", "E6", "G6", "A6")):
        place(b, harp(hz(note), 0.35 + 0.03 * i, rng, length=0.8), i * 0.026)
    for i, note in enumerate(("E6", "G6", "C7", "E7")):
        place(b, celesta(hz(note), 0.5 + 0.06 * i, rng, length=0.9), 0.24 + i * 0.05)
    place(b, bell(hz("C7"), 0.3, rng, length=1.1), 0.42)
    place(b, bell(hz("G7"), 0.12, rng, length=0.9), 0.46)
    place(b, sparkle(1.05, 14, rng, vel=0.22, start=0.3), 0.0)
    place(b, shimmer(0.8, rng, rise=0.25), 0.2, 0.03)
    b = sos_filter(b, "low", 11000)
    return reverb_mono(b, rng, 1.0, 0.26)


def sfx_shuffle(rng):
    """Shuffle (~0.8 s): a swirling whoosh (band centre circling up and down like a whirlpool, louder on the
    upswings), a low liquid slosh, bubbles tumbling through it and a soft sparkle as it settles."""
    b = np.zeros(samples(1.3))
    dur = 0.75
    n = samples(dur)
    t = taxis(n)
    swirl = lambda tt: 900 * 2 ** (1.1 * np.sin(2 * np.pi * 3.3 * tt) + 0.6 * tt / dur)
    env = bell_curve(n) * (0.75 + 0.25 * np.sin(2 * np.pi * 3.3 * t + 1.2))
    place(b, filtered_noise(dur, swirl, 0.55, rng) * env, 0, 0.14)
    slosh = filtered_noise(dur, lambda tt: 420 * 2 ** (0.5 * np.sin(2 * np.pi * 3.3 * tt + 2)), 0.5, rng)
    place(b, slosh * env, 0, 0.07)
    for tt in np.sort(rng.uniform(0.06, 0.68, 14)):
        f0 = rng.uniform(500, 1700)
        place(b, bubble(f0, f0 * rng.uniform(1.4, 1.9), 0.05, tau=0.016, harm=0.06), tt, 0.3 * rng.uniform(0.5, 1.0))
    place(b, sparkle(0.35, 4, rng, vel=0.18, start=0.0), 0.62)
    b = sos_filter(sos_filter(b, "high", 150), "low", 10000)
    return reverb_mono(b, rng, 0.5, 0.14)


def sfx_crystal(rng):
    """Crystal ball (~0.9 s): a glass-harmonica chord (E6 G6 B6 D7 — Cmaj9 colours, soft bowed attacks, slowly beating
    pairs) inside a tremolo shimmer, a breathy rise and a few sparkles: mystical, but in key."""
    b = np.zeros(samples(1.5))
    place(b, glass(hz("E7"), 1.0, rng, length=0.4, tau=0.07, bright=0.7), 0.0)          # the crystal 'ting'
    for i, note in enumerate(("E6", "G6", "B6", "D7")):
        place(b, glass_harmonica(hz(note), 0.45 - 0.04 * i, 0.9, rng, release=0.22), 0.04 + i * 0.05, 0.065)
    dur = 0.8
    n = samples(dur)
    t = taxis(n)
    sh = sos_filter(rng.standard_normal(n), "band", [5000, 11000]) * bell_curve(n, 1.2)
    place(b, sh * (0.6 + 0.4 * np.sin(2 * np.pi * 7 * t)), 0.05, 0.03)
    m = samples(0.4)
    place(b, filtered_noise(0.4, lambda tt: 900 * (3500 / 900) ** (tt / 0.4), 0.5, rng) * bell_curve(m), 0, 0.04)
    place(b, sparkle(0.75, 7, rng, vel=0.12, start=0.2), 0.0)
    b = sos_filter(b, "low", 11000)
    return reverb_mono(b, rng, 1.0, 0.26)


def sfx_rainbow(rng):
    """Rainbow potion (~0.9 s): a burst of bubbles rising in pitch, then a bright pentatonic arpeggio (celesta over
    harp) capped by a bell chord, sparkles and shimmer."""
    b = np.zeros(samples(1.7))
    for i in range(16):
        tt = 0.32 * (i / 16) ** 0.8 + rng.uniform(-0.01, 0.01)
        f0 = 450 * (2000 / 450) ** (i / 15) * rng.uniform(0.9, 1.1)
        place(b, bubble(f0, f0 * 1.6, 0.05, tau=0.016), max(tt, 0.0), 0.32 * rng.uniform(0.6, 1.0))
    for i, note in enumerate(("C6", "D6", "E6", "G6", "A6", "C7", "D7", "E7")):
        t0 = 0.18 + i * 0.042
        place(b, celesta(hz(note), 0.45 + 0.04 * i, rng, length=0.8), t0)
        place(b, harp(hz(note) / 2, 0.3, rng, length=0.9), t0)
    for note in ("C7", "E7", "G7"):
        place(b, bell(hz(note), 0.12, rng, length=1.0), 0.52)
    place(b, sparkle(0.9, 10, rng, vel=0.2, start=0.45), 0.0)
    place(b, shimmer(0.7, rng, rise=0.2), 0.3, 0.03)
    b = sos_filter(b, "low", 11000)
    return reverb_mono(b, rng, 0.9, 0.24)


def sfx_bubble(rng):
    """One cute bubble (~0.15 s): a damped sine gliding up (the bubble's resonance rising as it surfaces) and a tiny
    tick as it bursts."""
    b = np.zeros(samples(0.2))
    place(b, bubble(560, 1150, 0.075, tau=0.022, harm=0.1), 0)
    place(b, bubble(2400, 3000, 0.012, tau=0.003, harm=0.0), 0.068, 0.12)
    b = sos_filter(b, "low", 10000)
    return reverb_mono(b, rng, 0.25, 0.06)


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
    def __init__(self, name, bpm, bars, seed, mix, bpb=4, reverb=(1.6, 2.2), pans=None):
        self.name, self.bpm, self.bars, self.seed, self.bpb = name, bpm, bars, seed, bpb
        self.reverb = reverb                # (rt60 s, IR length s)
        self.pans = pans or {}              # per-song pan overrides (else INSTRUMENTS)
        self.events = []                    # (beat, dur_beats, midi or None, vel, instrument)
        # Stem loudness targets in LU relative to the lead (0). Each instrument stem is rendered, measured
        # (BS.1770 integrated, gated: i.e. its level while playing) and scaled to its target before mixing.
        self.mix = mix

    @property
    def beat_s(self):
        return 60.0 / self.bpm

    @property
    def loop_s(self):
        return self.bars * self.bpb * self.beat_s

    def add(self, beat, dur, note, vel, inst):
        self.events.append((beat, dur, midi(note) if isinstance(note, str) else note, vel, inst))


def fmidi(freq):
    """Frequency in Hz -> (fractional) MIDI note, for unpitched-but-tuned percussion (woodblocks, toms)."""
    return 12 * math.log2(freq / 440.0) + 69


# instrument -> (pan -1..1, reverb send, timing humanization sigma in ms)
INSTRUMENTS = {
    "celesta": (0.15, 0.32, 4),
    "musicbox": (0.3, 0.4, 3),
    "harp": (-0.3, 0.36, 4),
    "pizz": (0.32, 0.2, 5),
    "pizz_bass": (-0.05, 0.06, 3),
    "flute": (-0.12, 0.38, 5),
    "strings": (0.0, 0.42, 0),
    "glock": (0.4, 0.36, 4),
    "kick_soft": (0.0, 0.03, 1.5),
    "snap": (-0.2, 0.22, 4),
    "shaker": (-0.38, 0.1, 4),
    "triangle": (0.45, 0.32, 3),
    "chimes": (0.25, 0.45, 0),
    "chimes_down": (-0.25, 0.45, 0),
    "wood": (0.42, 0.16, 3),
    "tom": (-0.12, 0.16, 3),
}


def render_event(inst, f, dur_s, vel, rng):
    if inst == "celesta":
        return celesta(f, vel, rng)
    if inst == "musicbox":
        return music_box(f, vel, rng)
    if inst == "harp":
        return harp(f, vel, rng, damp=dur_s)
    if inst == "pizz":
        return pizz(f, vel, rng)
    if inst == "pizz_bass":
        return pizz_bass(f, vel, rng)
    if inst == "flute":
        return soft_flute(f, dur_s, vel, rng)
    if inst == "strings":
        return pad(f, dur_s, vel, rng)
    if inst == "glock":
        return glock(f, vel, rng, length=1.5)
    if inst == "kick_soft":
        return kick(vel, rng, soft=True)
    if inst == "snap":
        return snap(vel, rng)
    if inst == "shaker":
        return shaker(vel, rng)
    if inst == "triangle":
        return triangle(vel, rng)
    if inst in ("chimes", "chimes_down"):
        return mark_tree(rng, span=max(0.2, dur_s), up=inst == "chimes", vel=vel)
    if inst == "wood":
        return woodblock(f or 1700, vel, rng, tau=0.03)
    if inst == "tom":
        return tom(f or 160, vel, rng)
    raise ValueError(inst)


def pan_gains(p):
    a = (p + 1) * math.pi / 4
    return math.cos(a), math.sin(a)


def render_song(song):
    """Renders the loop + tail stem by stem (each scaled to its mix target), adds a stereo convolution reverb and
    folds the tail onto the start so the loop is exactly periodic. Returns (stereo float (n, 2), stem report)."""
    rng = np.random.default_rng(song.seed)
    L = samples(song.loop_s)
    tail = samples(max(4.5, 2 * song.bpb * song.beat_s))
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
        gl, gr = pan_gains(song.pans.get(inst, pan))
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
    rt60, ir_len = song.reverb
    ir = make_ir(rt60, ir_len, np.random.default_rng(song.seed + 1), stereo=True, predelay=0.02)
    wet = np.stack([signal.fftconvolve(send[:, c], ir[:, c])[:n] for c in range(2)], axis=1)
    mix = dry + wet * 0.9
    mix = sos_filter(mix, "high", 32)
    mix += 0.26 * sos_filter(mix, "high", 5000)        # ~+2 dB 'air' shelf: a little gloss on the bells and tines
    # (Filters and reverb run before the fold: every response that spills past the loop end lands on the start, so
    # the result is exactly what a circular/looping render would give.)
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

def seq(song, bar, text, inst, vel=0.8, octave=0, legato=1.0, accent=0.86):
    """Mini notation, one bar: 'E5 1, G5 .5, r .5, B5 2' (note or r=rest, duration in beats) from the bar's downbeat.
    Off-beat notes get a slightly lighter touch. Raises if the bar does not add up to the meter."""
    beat = 0.0
    for tok in text.split(","):
        tok = tok.strip()
        if not tok:
            continue
        name, d = tok.split()
        d = float(d)
        if name != "r":
            on = abs(beat - round(beat)) < 1e-6
            song.add(bar * song.bpb + beat, d * legato, midi(name) + 12 * octave, vel * (1.0 if on else accent), inst)
        beat += d
    if abs(beat - song.bpb) > 1e-6:
        raise ValueError(f"{song.name} bar {bar}: '{text}' lasts {beat} beats, not {song.bpb}")


def melody(song, first_bar, bars, inst, vel=0.8, octave=0, legato=1.0, ramp=None):
    """One `seq` per bar; `ramp` = (v0, v1) for a crescendo across the bars."""
    for i, text in enumerate(bars):
        v = vel if ramp is None else ramp[0] + (ramp[1] - ramp[0]) * i / max(1, len(bars) - 1)
        seq(song, first_bar + i, text, inst, v, octave, legato)


def arp(song, bar, notes, pattern, inst, vel, step=0.5, start=0.0, end=None, dur=None, ring=False):
    """Broken chord: notes[pattern[i]] every `step` beats from `start` to `end` (beats within the bar). ring=True:
    every note sustains until `end` (harp/pedal style), then is damped."""
    end = song.bpb if end is None else end
    b, i = start, 0
    while b < end - 1e-6:
        on = abs(b - round(b)) < 1e-6
        d = (end - b) if ring else (dur or step)
        song.add(bar * song.bpb + b, d, notes[pattern[i % len(pattern)]], vel * (1.0 if on else 0.82), inst)
        b += step
        i += 1


def hits(song, bar, beats, notes, inst, vel, dur=0.5, roll=0.0):
    """Block chord on each of `beats` (optionally rolled by `roll` beats per note)."""
    for beat in beats:
        for k, note in enumerate(notes):
            song.add(bar * song.bpb + beat + k * roll, dur, note, vel, inst)


def gliss(song, beat, notes, span, inst="harp", vel=0.5, crescendo=0.35, tail=0.1):
    """Fast run (harp glissando) over `span` beats starting at absolute `beat`; the strings ring as a blur and are
    damped `tail` beats after the run ends."""
    k = max(1, len(notes) - 1)
    for i, note in enumerate(notes):
        song.add(beat + span * i / k, span * (1 - i / k) + tail, note, vel * (1 - crescendo + crescendo * i / k), inst)


SCALES = {"major": (0, 2, 4, 5, 7, 9, 11), "minor": (0, 2, 3, 5, 7, 8, 10), "harm_minor": (0, 2, 3, 5, 7, 8, 11),
          "pent": (0, 2, 4, 7, 9)}


def scale_run(root, kind, lo, hi):
    """Every note of `kind` scale on `root` (pitch class name) between notes lo..hi (inclusive), ascending."""
    pc = NOTE_INDEX[root[0]] + (1 if "#" in root else -1 if "b" in root[1:] else 0)
    steps = SCALES[kind]
    return [m for m in range(midi(lo), midi(hi) + 1) if (m - pc) % 12 in steps]


# ----------------------------------------------------------------------------------------- songs

def song_home():
    """'Luna's Waltz' — cheerful, inviting 3/4 waltz in F major, 144 BPM, 32 bars = 40.0 s. Celesta melody over a
    pizzicato oom-pah-pah (bass on 1, chord on 2 and 3), harp rolls then flowing arpeggios, a soft flute B section
    with a magical minor-iv turn (Bb -> Bbm), glockenspiel doubling the last A, triangle and shaker, mark-tree
    chimes into the B section and back into the loop."""
    s = Song("music_home", 144, 32, seed=101, bpb=3, reverb=(1.7, 2.3), mix={
        "celesta": 0, "flute": -1, "pizz_bass": -5, "pizz": -7.5, "harp": -8.5, "glock": -12, "strings": -13,
        "kick_soft": -14, "shaker": -17, "triangle": -18, "chimes": -13},
        pans={"celesta": 0.1, "pizz": -0.3, "harp": 0.3, "flute": -0.08})
    A = ["F", "Dm", "Gm", "C7", "F", "Dm", "Gm7", "C7"]
    chords = A + ["F", "Bb", "F", "C7", "F", "Bb", "C7", "F"] + ["Bb", "Bbm", "F", "D7", "Gm", "C7", "F", "C7"] + A
    V = {   # bass note, pizz chord (beats 2 and 3), harp voicing (low -> high)
        "F": ("F2", ["A3", "C4", "F4"], ["F3", "C4", "F4", "A4", "C5"]),
        "Dm": ("D2", ["A3", "D4", "F4"], ["D3", "A3", "D4", "F4", "A4"]),
        "Gm": ("G2", ["Bb3", "D4", "G4"], ["G3", "D4", "G4", "Bb4", "D5"]),
        "Gm7": ("G2", ["Bb3", "D4", "F4"], ["G3", "D4", "F4", "Bb4", "D5"]),
        "C7": ("C3", ["Bb3", "E4", "G4"], ["C3", "G3", "C4", "E4", "Bb4"]),
        "Bb": ("Bb2", ["Bb3", "D4", "F4"], ["Bb2", "F3", "Bb3", "D4", "F4"]),
        "Bbm": ("Bb2", ["Bb3", "Db4", "F4"], ["Bb2", "F3", "Bb3", "Db4", "F4"]),
        "D7": ("D3", ["A3", "C4", "F#4"], ["D3", "A3", "C4", "F#4", "A4"]),
    }
    mel_a = ["C5 1, F5 1, G5 1", "A5 2, F5 1", "G5 1, Bb5 1, D6 1", "C6 2, Bb5 1",
             "A5 1, C6 1, F6 1", "E6 1, D6 1, A5 1", "Bb5 1, A5 1, G5 1", "G5 1, A5 1, Bb5 1"]
    mel_a2 = ["A5 2, C6 1", "D6 1, C6 1, Bb5 1", "A5 1, G5 1, F5 1", "G5 2, E5 1",
              "F5 1, A5 1, C6 1", "D6 1, F6 1, D6 1", "C6 1, Bb5 1, G5 1", "F5 2, r 1"]
    mel_b = ["D5 1, F5 1, Bb5 1", "Db6 2, Bb5 1", "C6 2, A5 1", "F#5 1, A5 1, C6 1",
             "Bb5 2, G5 1", "E5 1, G5 1, Bb5 1", "A5 3", "Bb5 1, G5 1, E5 1"]
    mel_a3 = mel_a[:7] + ["Bb5 1, G5 1, E5 1"]
    melody(s, 0, mel_a, "celesta", 0.8)
    melody(s, 8, mel_a2, "celesta", 0.8)
    melody(s, 16, mel_b, "flute", 0.75, legato=0.95)
    melody(s, 24, mel_a3, "celesta", 0.82)
    melody(s, 24, mel_a3, "glock", 0.26, octave=1)
    for bar, ch in enumerate(chords):
        bass, chord, hv = V[ch]
        s.add(bar * 3, 0.9, bass, 0.85, "pizz_bass")
        hits(s, bar, (1, 2), chord, "pizz", 0.5 if bar % 2 else 0.55, dur=0.4)
        if bar < 8:                                       # first A: a harp roll on the downbeat
            hits(s, bar, (0,), hv[:4], "harp", 0.45, dur=2.8, roll=0.06)
        else:                                             # then flowing eighth-note arpeggios
            arp(s, bar, hv, (0, 2, 3, 4, 3, 2) if bar % 2 == 0 else (0, 1, 2, 4, 3, 1), "harp", 0.42, ring=True)
        if 16 <= bar < 24:
            hits(s, bar, (0,), chord, "strings", 0.42, dur=2.9)
        s.add(bar * 3, 0.2, None, 0.5, "kick_soft")
        s.add(bar * 3 + 1, 0.1, None, 0.55, "shaker")
        s.add(bar * 3 + 2, 0.1, None, 0.5, "shaker")
        s.add(bar * 3 + 2.5, 0.1, None, 0.28, "shaker")
        if bar % 4 == 0:
            s.add(bar * 3, 0.2, None, 0.7 if bar % 8 == 0 else 0.4, "triangle")
    s.add(15 * 3 + 1, 1.2, None, 0.8, "chimes")          # into the B section
    s.add(31 * 3 + 1, 1.2, None, 0.8, "chimes")          # into the loop start
    return s


def song_game():
    """'Potion Study' — calm and unobtrusive (puzzle focus): 4/4 in C major, 92 BPM, 16 bars = 41.7 s. Flowing harp
    arpeggios over maj7/9 chords with a magical minor-iv (Fm6), a sparse music-box melody, a soft flute line in the
    second half answered by music-box echoes, a quiet string pad and pizzicato bass, a brushed shaker, mark-tree
    chimes into each half."""
    s = Song("music_game", 92, 16, seed=202, bpb=4, reverb=(2.1, 2.8), mix={
        "musicbox": 0, "flute": -2, "harp": -4, "celesta": -10, "pizz_bass": -9, "strings": -12.5,
        "shaker": -19, "triangle": -18, "chimes": -13})
    chords = ["Cmaj7", "Fmaj7", "Cmaj7", "Fmaj7", "Am7", "Fmaj7", "Dm7", "G7sus4",
              "Cmaj7", "Em7", "Fmaj7", "Fm6", "Em7", "Am7", "Dm7", "G7sus4"]
    V = {   # bass note, harp voicing (low -> high), pad voicing
        "Cmaj7": ("C3", ["C3", "G3", "E4", "B4", "D5"], ["E4", "G4", "B4"]),
        "Fmaj7": ("F2", ["F2", "C3", "A3", "E4", "G4"], ["A3", "C4", "E4"]),
        "Am7": ("A2", ["A2", "E3", "G3", "C4", "E4"], ["C4", "E4", "G4"]),
        "Dm7": ("D3", ["D3", "A3", "C4", "F4", "A4"], ["F4", "A4", "C5"]),
        "G7sus4": ("G2", ["G2", "D3", "C4", "F4", "A4"], ["C4", "F4", "G4"]),
        "G9": ("G2", ["G2", "D3", "B3", "F4", "A4"], ["B3", "F4", "G4"]),
        "Em7": ("E3", ["E3", "B3", "D4", "G4", "B4"], ["D4", "G4", "B4"]),
        "Fm6": ("F2", ["F2", "C3", "Ab3", "D4", "F4"], ["Ab3", "D4", "F4"]),
    }
    mel_mb = ["G5 1.5, E6 1.5, D6 1", "C6 3, r 1", "G5 1.5, E6 1.5, G6 1", "A6 2, G6 1, E6 1",
              "r 1, C6 1, E6 1, G6 1", "A6 2, G6 1, C6 1", "D6 2, C6 1, A5 1", "C6 2, B5 2"]
    mel_fl = ["E5 2, G5 1, B5 1", "B5 3, A5 1", "A5 2, G5 1, E5 1", "Ab5 3, G5 1",
              "G5 2, E5 2", "C6 2, B5 1, A5 1", "F5 2, A5 1, C6 1", "D6 2, C6 2"]
    echoes = {9: "r 2, B6 1, G6 1", 11: "r 2, C7 1, Ab6 1", 13: "r 2, E7 1, C7 1", 15: "r 2, D7 1, C7 1"}
    melody(s, 0, mel_mb, "musicbox", 0.8)
    melody(s, 4, mel_mb[4:], "celesta", 0.5, octave=-1)
    melody(s, 8, mel_fl, "flute", 0.7, legato=0.95)
    for bar, text in echoes.items():
        seq(s, bar, text, "musicbox", 0.42)
    pat = (0, 1, 2, 3, 4, 3, 2, 1)
    for bar, ch in enumerate(chords):
        bass, hv, pv = V[ch]
        if bar == 7:                       # the melody resolves sus4 -> 3rd on beat 3: harp and pad follow
            arp(s, bar, hv, pat, "harp", 0.5, end=2, ring=True)
            arp(s, bar, V["G9"][1], pat[4:] + pat[:4], "harp", 0.5, start=2, ring=True)
            hits(s, bar, (0,), pv, "strings", 0.4, dur=1.95)
            hits(s, bar, (2,), V["G9"][2], "strings", 0.4, dur=1.95)
        else:
            arp(s, bar, hv, pat, "harp", 0.5, ring=True)
            hits(s, bar, (0,), pv, "strings", 0.4, dur=3.95)
        s.add(bar * 4, 1.5, bass, 0.7, "pizz_bass")
        s.add(bar * 4 + 2, 1.0, bass, 0.42, "pizz_bass")
        for e in range(8):
            s.add(bar * 4 + e * 0.5, 0.1, None, 0.36 if e % 2 == 0 else 0.22, "shaker")
    s.add(0, 0.2, None, 0.6, "triangle")
    s.add(8 * 4, 0.2, None, 0.6, "triangle")
    s.add(7 * 4 + 3, 1.2, None, 0.7, "chimes")
    s.add(15 * 4 + 3, 1.2, None, 0.7, "chimes")
    return s


def song_hard():
    """'Bubbling Cauldron' — a bit mysterious and tense but cute: 4/4 in A minor, 120 BPM, 20 bars = 40.0 s. A sneaky
    staccato pizzicato ostinato over the chromatic line cliché (A G# G F# F), a celesta melody with chromatic
    neighbours (music box doubling it the second time), finger snaps on 2 and 4, soft kick and timpani-like toms,
    a low string pad, harp glissandos, and a tick-tock woodblock building the last four bars back into the loop."""
    s = Song("music_hard", 120, 20, seed=303, bpb=4, reverb=(1.5, 2.0), mix={
        "celesta": 0, "musicbox": -7, "pizz": -4.5, "pizz_bass": -4, "kick_soft": -8, "snap": -10.5, "tom": -9,
        "strings": -12.5, "harp": -9, "chimes_down": -13, "shaker": -18, "wood": -14, "triangle": -18},
        pans={"celesta": 0.1, "pizz": -0.3, "snap": -0.12})
    chords = ["Am", "Am/G#", "Am/G", "D/F#", "F", "Dm", "E7sus4", "E7",
              "Am", "Am/G#", "Am/G", "D/F#", "F", "G", "E7", "Am",
              "Dm", "F", "E7sus4", "E7"]
    V = {   # bass note, ostinato / pad voicing (low, mid, high)
        "Am": ("A2", ["A3", "C4", "E4"]),
        "Am/G#": ("G#2", ["G#3", "C4", "E4"]),
        "Am/G": ("G2", ["G3", "C4", "E4"]),
        "D/F#": ("F#2", ["F#3", "A3", "D4"]),
        "F": ("F2", ["F3", "A3", "C4"]),
        "Dm": ("D2", ["F3", "A3", "D4"]),
        "E7sus4": ("E2", ["E3", "A3", "B3"]),
        "E7": ("E2", ["E3", "G#3", "D4"]),
        "G": ("G2", ["G3", "B3", "D4"]),
    }
    mel_a = ["A5 .5, C6 .5, E6 1, D#6 .5, E6 .5, C6 1", "B5 1.5, A5 .5, G#5 1, E5 1",
             "A5 .5, C6 .5, E6 1, F6 .5, E6 .5, C6 1", "D6 1.5, C6 .5, A5 1, F#5 1",
             "F5 .5, A5 .5, C6 1, E6 1, C6 1", "D6 1, A5 1, F5 .5, E5 .5, D5 1",
             "E5 1, A5 1, B5 1, D6 1", "G#5 2, E5 1, r 1"]
    mel_a2 = mel_a[:4] + ["C6 .5, A5 .5, F5 1, A5 1, C6 1", "D6 .5, B5 .5, G5 1, B5 1, D6 1",
                          "E6 1, D6 1, B5 1, G#5 1", "A5 2, r 2"]
    mel_b = ["D6 .5, A5 .5, F5 .5, A5 .5, D6 .5, A5 .5, F5 .5, A5 .5",
             "C6 .5, A5 .5, F5 .5, A5 .5, C6 .5, A5 .5, F5 .5, A5 .5",
             "B5 .5, A5 .5, E5 .5, A5 .5, B5 .5, A5 .5, E5 .5, A5 .5",
             "B5 .5, G#5 .5, E5 .5, G#5 .5, B5 .5, D6 .5, E6 .5, G#5 .5"]
    melody(s, 0, mel_a, "celesta", 0.8)
    melody(s, 8, mel_a2, "celesta", 0.8)
    melody(s, 8, mel_a2, "musicbox", 0.45, octave=1)
    melody(s, 16, mel_b, "celesta", 0.8, ramp=(0.55, 0.85))
    ost = (0, 2, 1, 2, 0, 2, 1, 2)
    acc = (1.0, 0.62, 0.8, 0.62, 0.92, 0.62, 0.8, 0.62)
    for bar, ch in enumerate(chords):
        bass, vo = V[ch]
        for e in range(8):
            s.add(bar * 4 + e * 0.5, 0.3, vo[ost[e]], 0.6 * acc[e], "pizz")
        up = midi(bass) + 12
        for q, (m, v) in enumerate(((bass, 0.85), (up, 0.5), (bass, 0.72), (up, 0.5))):
            s.add(bar * 4 + q, 0.5, m, v, "pizz_bass")
        if bar >= 8:
            hits(s, bar, (0,), vo, "strings", 0.45 if bar < 16 else 0.55, dur=3.95)
        s.add(bar * 4, 0.2, None, 0.8, "kick_soft")
        s.add(bar * 4 + 2, 0.2, None, 0.6, "kick_soft")
        s.add(bar * 4 + 1, 0.1, None, 0.7, "snap")
        s.add(bar * 4 + 3, 0.1, None, 0.7, "snap")
        for e in range(16):
            s.add(bar * 4 + e * 0.25, 0.1, None, 0.5 if e % 4 == 2 else (0.32 if e % 2 else 0.22), "shaker")
        if bar >= 16:
            for e in range(8):
                s.add(bar * 4 + e * 0.5, 0.1, fmidi(2300 if e % 2 == 0 else 1750), 0.4 + 0.1 * (bar - 16), "wood")
    for bar in (0, 4, 8, 12):
        s.add(bar * 4, 0.3, fmidi(110), 0.6, "tom")
    for k, f in enumerate((196, 165, 147, 131)):          # timpani fill into the loop start
        s.add(19 * 4 + 3 + k * 0.25, 0.25, fmidi(f), 0.55 + 0.05 * k, "tom")
    s.add(0, 0.2, None, 0.6, "triangle")
    s.add(8 * 4, 0.2, None, 0.6, "triangle")
    gliss(s, 7 * 4 + 3, scale_run("A", "harm_minor", "A3", "A5"), 0.9, vel=0.5)
    gliss(s, 15 * 4 + 3, scale_run("A", "harm_minor", "E4", "E6"), 0.9, vel=0.5)
    s.add(19 * 4 + 2, 1.5, None, 0.7, "chimes_down")
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


# ============================================================================================ preview sheets

def _colormap(v):
    """[0, 1] -> RGB (night violet -> magenta -> amber -> cream)."""
    stops = np.array([[0.0, 10, 6, 24], [0.3, 70, 20, 110], [0.55, 190, 50, 120], [0.78, 250, 145, 60],
                      [1.0, 255, 246, 200]])
    return np.stack([np.interp(v, stops[:, 0], stops[:, c]) for c in (1, 2, 3)], axis=-1).astype(np.uint8)


def render_preview(names, path, px_per_s=700, cols=2):
    """Waveform + log-frequency spectrogram (60 Hz - 16 kHz, 80 dB range) of each sfx, on a common time scale."""
    from PIL import Image, ImageDraw, ImageFont
    clips = []
    for name in names:
        p = os.path.join(OUT, f"sfx_{name}.wav")
        if os.path.exists(p):
            sr, y = wavfile.read(p)
            clips.append((name, y.astype(np.float64) / 32768.0))
    if not clips:
        return None
    try:
        font = ImageFont.load_default(size=15)
    except TypeError:
        font = ImageFont.load_default()
    W = int(px_per_s * max(len(y) for _, y in clips) / SR) + 16
    WAVE_H, SPEC_H, HEAD = 70, 190, 24
    tile_h = HEAD + WAVE_H + SPEC_H + 12
    rows = (len(clips) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * W + 8, rows * tile_h + 8), (24, 18, 36))
    fgrid = np.geomspace(60, 16000, SPEC_H)[::-1]
    for i, (name, y) in enumerate(clips):
        x0, y0 = 8 + (i % cols) * W, 8 + (i // cols) * tile_h
        d = ImageDraw.Draw(sheet)
        dur = len(y) / SR
        d.text((x0, y0 + 2), f"sfx_{name}   {dur:.2f} s   peak {db(np.max(np.abs(y))):.1f} dB   "
                             f"M {momentary_max(y):.1f} LUFS", fill=(235, 225, 255), font=font)
        w = max(2, int(px_per_s * dur))
        cols_idx = np.linspace(0, len(y), w + 1).astype(int)
        top = y0 + HEAD
        d.rectangle([x0, top, x0 + W - 12, top + WAVE_H], fill=(14, 10, 26))
        # peak envelope over >= 5 ms per column (a plain per-column min/max shows moire on steady tones when zoomed)
        env = maximum_filter1d(np.abs(y), max(samples(0.005), len(y) // w + 1))
        for c in range(w):
            a = float(env[min(len(y) - 1, (cols_idx[c] + cols_idx[c + 1]) // 2)])
            d.line([x0 + c, top + WAVE_H / 2 - a * WAVE_H / 2, x0 + c, top + WAVE_H / 2 + a * WAVE_H / 2],
                   fill=(150, 210, 255))
        f, t, Z = signal.stft(y, SR, nperseg=1024, noverlap=1024 - 64)
        S = 20 * np.log10(np.abs(Z) + 1e-9)
        S = np.clip((S - (S.max() - 80)) / 80, 0, 1)
        fi = np.clip(np.searchsorted(f, fgrid), 0, len(f) - 1)
        ti = np.clip(np.searchsorted(t, np.linspace(0, dur, w)), 0, len(t) - 1)
        img = Image.fromarray(_colormap(S[fi][:, ti]))
        sheet.paste(img, (x0, top + WAVE_H + 4))
        for fl in (100, 1000, 10000):
            yy = top + WAVE_H + 4 + int(np.searchsorted(-fgrid, -fl))
            d.text((x0 + 2, yy - 8), f"{fl // 1000}k" if fl >= 1000 else f"{fl}", fill=(255, 255, 255), font=font)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sheet.save(path)
    return path


# ============================================================================================ main

def sfx_seed(name):
    """Stable per-sound seed (independent of the list order)."""
    return zlib.crc32(name.encode("utf-8"))


def gen_sfx(names, rows):
    for name in names:
        rng = np.random.default_rng(sfx_seed(name))
        x = finalize_sfx(globals()["sfx_" + name](rng))
        path = os.path.join(OUT, f"sfx_{name}.wav")
        write_wav16(path, x, rng)
        sr, y = wavfile.read(path)
        y = y.astype(np.float64) / 32768.0
        rows.append({
            "name": f"sfx_{name}", "dur": len(y) / sr, "peak": db(np.max(np.abs(y))), "lufs": lufs(y),
            "mmax": momentary_max(y),
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
            "name": name, "bpm": song.bpm, "bars": song.bars, "meter": f"{song.bpb}/4", "dur": len(x) / SR,
            "lufs": lufs(x), "peak": db(np.max(np.abs(x))), "jump": jump, "step999": step, "enc": enc,
            "size_kb": os.path.getsize(ogg) / 1024, "events": len(song.events),
        }
        if dec is not None:
            row["dec_len_diff"] = len(dec) - len(x)
            row["dec_peak"] = db(float(np.max(np.abs(dec))))
            row["dec_jump"] = float(np.max(np.abs(dec[0] - dec[-1])))
            row["dec_step999"] = float(np.percentile(np.abs(np.diff(dec, axis=0)), 99.9))
        rows.append(row)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="sfx | music | board | a single name (e.g. pour, music_home)")
    ap.add_argument("--prune", action="store_true", help="delete clips no enum value uses (and their .meta)")
    ap.add_argument("--preview", action="store_true", help="write board-sound sheets to ArtSource/preview/")
    args = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)

    sfx = SFX_NAMES
    music = list(SONGS)
    if args.only == "sfx":
        music = []
    elif args.only == "music":
        sfx = []
    elif args.only == "board":
        sfx, music = BOARD_SFX, []
    elif args.only:
        key = args.only.replace("sfx_", "")
        sfx = [key] if key in SFX_NAMES else []
        music = [args.only] if args.only in SONGS else []
        if not sfx and not music:
            sys.exit(f"unknown sound {args.only}")

    problems = []
    for kind, ours in (("Sfx", SFX_NAMES), ("Music", [n.replace("music_", "") for n in SONGS])):
        names = enum_names(kind)
        if names is None:
            print(f"warning: could not read enum {kind} from {ENUM_SRC}")
            continue
        if kind == "Music":
            names = [n for n in names if n != "none"]
        if names != ours:
            print(f"enum {kind} mismatch:\n  C#:     {names}\n  script: {ours}")
            problems.append(f"enum {kind}")

    sfx_rows, music_rows = [], []
    gen_sfx(sfx, sfx_rows)
    with tempfile.TemporaryDirectory() as tmp:
        gen_music(music, music_rows, tmp)

    if sfx_rows:
        print(f"\n{'SFX':22s} {'dur s':>6s} {'peak dB':>8s} {'LUFS':>6s} {'Mmax':>6s} {'>300Hz':>6s} {'DC':>9s} "
              f"{'edge':>7s} {'clip':>4s}")
        for r in sfx_rows:
            print(f"{r['name']:22s} {r['dur']:6.2f} {r['peak']:8.2f} {r['lufs']:6.1f} {r['mmax']:6.1f} "
                  f"{r['lufs_phone']:6.1f} {r['dc']:9.1e} {r['edge']:7.4f} {r['clip']:4d}")
            if r["nan"] or r["clip"] or abs(r["peak"] - SFX_PEAK_DB) > 0.15 or r["edge"] > 0.003 or abs(r["dc"]) > 2e-3:
                problems.append(r["name"])
    if music_rows:
        print(f"\n{'MUSIC':12s} {'bpm':>4s} {'bars':>4s} {'meter':>5s} {'dur s':>6s} {'LUFS':>6s} {'peak':>6s} "
              f"{'loop jump':>9s} {'p99.9 step':>10s} {'dec Δn':>6s} {'dec peak':>8s} {'dec jump':>8s} {'KB':>6s}  encoder")
        for r in music_rows:
            print(f"{r['name']:12s} {r['bpm']:4d} {r['bars']:4d} {r['meter']:>5s} {r['dur']:6.2f} {r['lufs']:6.1f} "
                  f"{r['peak']:6.2f} {r['jump']:9.4f} {r['step999']:10.4f} {r.get('dec_len_diff', 0):6d} "
                  f"{r.get('dec_peak', 0):8.2f} {r.get('dec_jump', 0):8.4f} {r['size_kb']:6.0f}  {r['enc']}")
            bad_dec = "dec_jump" in r and (r["dec_jump"] > r["dec_step999"] or r["dec_len_diff"] != 0)
            if r["jump"] > r["step999"] or abs(r["lufs"] - MUSIC_LUFS) > 1.0 or r["peak"] > -1.0 or bad_dec:
                problems.append(r["name"])

    expected = {f"sfx_{n}.wav" for n in SFX_NAMES} | {f"{n}.ogg" for n in SONGS}
    present = {f for f in os.listdir(OUT) if f.endswith((".wav", ".ogg"))}
    missing = sorted(expected - present)
    extra = sorted(present - expected)
    if extra and args.prune:
        for f in extra:
            for p in (os.path.join(OUT, f), os.path.join(OUT, f + ".meta")):
                if os.path.exists(p):
                    os.remove(p)
        print("pruned:", extra)
        extra = []
    if missing:
        print("missing:", missing)
    if extra:
        print("unexpected files in Audio/ (run with --prune to delete):", extra)
    if args.preview:
        durs = {}
        for n in BOARD_SFX:
            p = os.path.join(OUT, f"sfx_{n}.wav")
            if os.path.exists(p):
                sr, y = wavfile.read(p)
                durs[n] = len(y) / sr
        short = [n for n in BOARD_SFX if durs.get(n, 9) < 0.8]
        long_ = [n for n in BOARD_SFX if n in durs and n not in short]
        for tag, part in (("short", short), ("long", long_)):
            if part:
                pps = int(1100 / max(durs[n] for n in part))
                p = render_preview(part, os.path.join(PREVIEW, f"audio_board_{tag}.png"), px_per_s=pps)
                print("preview:", os.path.relpath(p, ROOT))
    print("\nOK" if not problems and not missing and not extra else f"\nCHECK: {problems} {missing} {extra}")


if __name__ == "__main__":
    main()

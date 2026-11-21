"""The Crucible's music, synthesised (no samples): written to
source/Shared/resources/web/music/the_crucible.mp3, as the account server serves it.

    python tools/crucible/sound.py

Needs numpy, and lameenc (pip install lameenc) or ffmpeg on the PATH (or at C:/ffmpeg/bin) to
make the MP3. The output is committed, so the build doesn't need them; run this again only to
change the music. The random parts are seeded, so a run gives the same file.

"the_crucible": a loop of 32 bars at 112 beats a minute (about 69 s), in C minor, climbing: war
drums and an anvil ringing every other bar, a driving bass, strings on Cm, Ab, Eb, Bb; brass
stabs from bar 9, a choir from bar 17 with a horn calling the theme over it, and in the last
eight bars everything at once. Its reverb's tail is folded back onto its start, so it loops
without a seam.
"""
import os
import shutil
import subprocess
import tempfile
import wave

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "web", "music", "the_crucible.mp3")
SR = 44100
BPM = 112
BEAT = 60.0 / BPM
BAR = 4 * BEAT
BARS = 32
N = int(BARS * BAR * SR)
rng = np.random.RandomState(11212026)


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12.0)


def t_of(seconds):
    return np.arange(int(seconds * SR)) / float(SR)


def env(n, attack, release, sustain=1.0):
    e = np.full(n, sustain)
    a = min(n, int(attack * SR))
    if a:
        e[:a] = np.linspace(0, sustain, a)
    r = min(n - a, int(release * SR))
    if r > 0:
        e[n - r:] *= np.exp(-np.linspace(0, 6, r))
    return e


def decay(n, seconds):
    return np.exp(-np.arange(n) / (seconds * SR))


def lowpass(x, cutoff):
    """a two-pole low-pass by FFT, gentle."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    return np.fft.irfft(np.fft.rfft(x) / (1 + (f / cutoff) ** 2), n)


def saw(f, t, detune=(-0.12, 0.0, 0.13)):
    out = np.zeros(len(t))
    for d in detune:
        ff = f * (1 + d / 100.0)
        ph = rng.uniform(0, 1)
        out += 2 * ((ff * t + ph) % 1.0) - 1
    return out / len(detune)


def pan(mono, p):
    a = (p + 1) * np.pi / 4
    return np.stack([mono * np.cos(a), mono * np.sin(a)])


def place(buf, sig, at):
    if sig.ndim == 1:
        sig = np.stack([sig, sig])
    at = int(at) % buf.shape[1]
    n = sig.shape[1]
    end = at + n
    if end <= buf.shape[1]:
        buf[:, at:end] += sig
    else:
        k = buf.shape[1] - at
        buf[:, at:] += sig[:, :k]
        place(buf, sig[:, k:], 0)


def beat_at(bar, beat):
    return (bar * BAR + beat * BEAT) * SR


# ---- voices ----

def drum(low=True, power=1.0):
    """a war drum: a sine falling in pitch, a thump of noise."""
    secs = 0.9 if low else 0.45
    t = t_of(secs)
    f0, f1 = (70, 42) if low else (180, 110)
    f = f1 + (f0 - f1) * np.exp(-t * 18)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * decay(len(t), secs * 0.35)
    noise = lowpass(rng.standard_normal(len(t)), 900 if low else 2500) * decay(len(t), 0.05) * 0.8
    return (tone + noise) * power


def anvil():
    """the forge's anvil: inharmonic partials ringing out."""
    t = t_of(2.2)
    out = np.zeros(len(t))
    for ratio, a, d in ((1.0, 1.0, 0.9), (2.76, 0.6, 0.5), (5.4, 0.4, 0.3), (8.9, 0.25, 0.18), (13.3, 0.15, 0.1)):
        out += a * np.sin(2 * np.pi * 620 * ratio * t) * decay(len(t), d)
    click = rng.standard_normal(len(t)) * decay(len(t), 0.006)
    return (out * 0.35 + click * 0.5) * env(len(t), 0.001, 0.3)


def bass(midi, secs):
    t = t_of(secs)
    x = saw(hz(midi), t, (-0.08, 0.08)) + 0.5 * np.sin(2 * np.pi * hz(midi - 12) * t)
    return lowpass(x, 420) * env(len(t), 0.004, 0.08) * 0.9


def strings(chord, secs):
    t = t_of(secs)
    out = np.zeros(len(t))
    for m in chord:
        out += saw(hz(m), t)
    out = lowpass(out / len(chord), 1800)
    return out * env(len(t), 0.25, 0.5)


def brass(chord, secs):
    t = t_of(secs)
    out = np.zeros(len(t))
    for m in chord:
        f = hz(m)
        sq = np.sign(np.sin(2 * np.pi * f * t + 0.3))
        out += 0.6 * saw(f, t, (-0.05, 0.06)) + 0.25 * sq
    e = env(len(t), 0.02, secs * 0.6)
    # the bite: brighter as it opens
    bright = lowpass(out, 2600) * e
    dull = lowpass(out, 700) * e
    return (0.6 * bright + 0.4 * dull) / len(chord)


def choir(chord, secs):
    t = t_of(secs)
    out = np.zeros(len(t))
    for m in chord:
        for d in (-0.3, 0.0, 0.35):
            ff = hz(m) * (1 + d / 100.0)
            ph = rng.uniform(0, 2 * np.pi)
            vib = 1 + 0.004 * np.sin(2 * np.pi * 5.0 * t + ph)
            for k, a in ((1, 1.0), (2, 0.4), (3, 0.25), (4, 0.1)):
                out += a * np.sin(2 * np.pi * ff * k * t * vib + ph * k)
    return out / (len(chord) * 3) * env(len(t), secs * 0.3, secs * 0.35)


def horn(midi, secs):
    t = t_of(secs)
    f = hz(midi)
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(1, t / 0.3)
    x = np.zeros(len(t))
    for k, a in ((1, 1.0), (2, 0.55), (3, 0.4), (4, 0.22), (5, 0.12), (6, 0.06)):
        x += a * np.sin(2 * np.pi * f * k * np.cumsum(vib) / SR)
    return lowpass(x, 2200) * env(len(t), 0.05, min(0.4, secs * 0.5)) * 0.5


# ---- the piece ----

# Cm, Ab, Eb, Bb, a bar each
CHORDS = [[48, 51, 55], [44, 48, 51], [51, 55, 58], [46, 50, 53]]
ROOTS = [36, 32, 39, 34]
# the theme: (bar of the four, beat, length in beats, note)
THEME = [(0, 0, 1.5, 67), (0, 1.5, 0.5, 68), (0, 2, 2, 67), (1, 0, 1, 63), (1, 1, 1, 65), (1, 2, 2, 68),
         (2, 0, 1.5, 70), (2, 1.5, 0.5, 72), (2, 2, 2, 70), (3, 0, 1, 74), (3, 1, 1, 72), (3, 2, 2, 70)]


def build():
    mix = np.zeros((2, N))
    for bar in range(BARS):
        k = bar % 4
        section = bar // 8          # 0 drums+bass, 1 +brass, 2 +choir and theme, 3 everything
        chord = CHORDS[k]
        # drums: the low drum on 1 and 3, and on 2-and; high drums filling in, more as it climbs
        for b in (0, 2):
            place(mix, pan(drum(True, 1.0), -0.1), beat_at(bar, b))
        place(mix, pan(drum(True, 0.7), 0.1), beat_at(bar, 1.5))
        if section >= 1:
            for b in (1, 3):
                place(mix, pan(drum(False, 0.5), 0.35), beat_at(bar, b))
        if section >= 2 or k == 3:
            for b in (3.25, 3.5, 3.75):
                place(mix, pan(drum(False, 0.35 + 0.1 * section), -0.35), beat_at(bar, b))
        # the anvil, every other bar
        if bar % 2 == 0:
            place(mix, pan(anvil(), 0.25) * 0.6, beat_at(bar, 0))
        # bass: driving eighths on the root
        for e in range(8):
            note = ROOTS[k] + (12 if e in (3, 7) else 0)
            place(mix, pan(bass(note, BEAT / 2 * 0.95), 0.0) * 0.55, beat_at(bar, e / 2))
        # strings all the while
        place(mix, pan(strings(chord, BAR * 1.05), -0.3) * 0.32, beat_at(bar, 0))
        place(mix, pan(strings([c + 12 for c in chord], BAR * 1.05), 0.3) * 0.2, beat_at(bar, 0))
        # brass stabs from bar 9: on 1 and the and of 2
        if section >= 1:
            for b, ln in ((0, 0.6), (1.5, 0.4), (3, 0.6) if section >= 3 else (None, 0)):
                if b is None:
                    continue
                place(mix, pan(brass([c + 12 for c in chord], ln), 0.15) * 0.4, beat_at(bar, b))
        # choir and the horn's theme from bar 17
        if section >= 2:
            place(mix, pan(choir([c + 12 for c in chord], BAR * 1.1), -0.2) * 0.35, beat_at(bar, 0))
            for (tb, beat, length, note) in THEME:
                if tb == k:
                    place(mix, pan(horn(note, length * BEAT * 0.98), 0.2) * (0.5 if section == 2 else 0.6), beat_at(bar, beat))
                    if section == 3:
                        place(mix, pan(horn(note - 12, length * BEAT * 0.98), -0.2) * 0.3, beat_at(bar, beat))
    # a little room: early reflections and a decaying tail, folded round (place wraps)
    wet = np.zeros_like(mix)
    for i, (ms, g) in enumerate(((23, 0.3), (37, 0.25), (61, 0.2), (89, 0.16), (131, 0.12), (197, 0.09), (271, 0.06))):
        d = int(ms * SR / 1000)
        side = 1 if i % 2 else 0
        wet[side] += np.roll(mix[side], d) * g
        wet[1 - side] += np.roll(mix[side], d + 7) * g * 0.6
    out = mix + wet
    out /= np.max(np.abs(out)) * 1.08
    return out


def write(out):
    pcm = (np.clip(out, -1, 1) * 32767).astype(np.int16).T.copy()
    try:
        import lameenc
        enc = lameenc.Encoder()
        enc.set_bit_rate(160)
        enc.set_in_sample_rate(SR)
        enc.set_channels(2)
        enc.set_quality(2)
        data = enc.encode(pcm.tobytes()) + enc.flush()
        open(OUT, "wb").write(data)
        return
    except ImportError:
        pass
    ffmpeg = shutil.which("ffmpeg") or ("C:/ffmpeg/bin/ffmpeg.exe" if os.path.exists("C:/ffmpeg/bin/ffmpeg.exe") else None)
    assert ffmpeg, "needs lameenc or ffmpeg"
    tmp = os.path.join(tempfile.mkdtemp(), "crucible.wav")
    with wave.open(tmp, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    subprocess.check_call([ffmpeg, "-y", "-loglevel", "error", "-i", tmp, "-codec:a", "libmp3lame", "-b:a", "160k", "-map_metadata", "-1", OUT])


if __name__ == "__main__":
    write(build())
    print("wrote", OUT, "%.1f s" % (N / SR))

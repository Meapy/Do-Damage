"""The Black Between's music and its bosses' sounds, synthesised (no samples): written to
source/Shared/resources/web/music/ and web/sfx/monster/ as MP3s, as the account server serves them.

    python tools/black_between/sound.py

Needs numpy and lameenc (pip install numpy lameenc). The output is committed, so the build doesn't
need them; run this again only to change the sounds. The random parts are seeded, so a run gives
the same files.

The music, "the_black_between": a loop of 32 bars at 84 beats a minute (about 91 s), in D minor:
the void's drone, a choir of four chords (Dm, Bb, Gm, A), Aurel's gold in bell arpeggios, Vesper's
violet in a low pulse and timpani, stars glinting, and from bar 17 the Mysterious One's theme on a
glassy lead. Its reverb's tail is folded back onto its start, so it loops without a seam.
"""
import os

import lameenc
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WEB = os.path.join(ROOT, "source", "Shared", "resources", "web")
SR = 44100
rng = np.random.default_rng(8102026)


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def t_of(seconds):
    return np.arange(int(seconds * SR)) / SR


def env(n, attack, release, sustain=1.0):
    """attack and release in seconds over n samples: a linear rise, a hold, an exponential fall."""
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
    """one-pole low-pass, by FFT (a gentle slope, fine for pads and noise)."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    return np.fft.irfft(np.fft.rfft(x) / np.sqrt(1 + (f / cutoff) ** 2), n)


def bandpass(x, lo, hi):
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    spec = np.fft.rfft(x)
    spec[(f < lo) | (f > hi)] = 0
    return np.fft.irfft(spec, n)


def reverb_ir(seconds, damp=0.5, seed=1):
    r = np.random.default_rng(seed)
    n = int(seconds * SR)
    ir = r.standard_normal((2, n)) * np.exp(-np.arange(n) / (seconds * SR / 6.5))
    # high frequencies die sooner
    ir = np.stack([lowpass(c, 5000 * (1 - damp) + 1500) for c in ir])
    ir[:, : int(0.012 * SR)] = 0  # a little pre-delay
    return ir / np.sqrt((ir ** 2).sum(axis=1, keepdims=True))


def convolve(x, ir):
    n = len(x) + ir.shape[1] - 1
    size = 1 << (n - 1).bit_length()
    X = np.fft.rfft(x, size)
    return np.stack([np.fft.irfft(X * np.fft.rfft(c, size), size)[:n] for c in ir])


def pan(mono, p):
    """p from -1 (left) to 1 (right), equal power."""
    a = (p + 1) * np.pi / 4
    return np.stack([mono * np.cos(a), mono * np.sin(a)])


def place(buf, sig, at):
    """add a stereo (2, n) or mono signal into buf (2, N) at sample `at`, wrapping round the end."""
    if sig.ndim == 1:
        sig = np.stack([sig, sig])
    n = sig.shape[1]
    end = at + n
    if end <= buf.shape[1]:
        buf[:, at:end] += sig
    else:
        k = buf.shape[1] - at
        buf[:, at:] += sig[:, :k]
        place(buf, sig[:, k:], 0)


# ---- voices ----

def bell(freq, seconds, bright=1.0):
    """FM bell: a ratio-3.5 modulator whose depth falls faster than the tone."""
    t = t_of(seconds)
    mod = np.sin(2 * np.pi * freq * 3.5 * t) * (2.2 * bright) * decay(len(t), seconds * 0.18)
    tone = np.sin(2 * np.pi * freq * t + mod) + 0.25 * np.sin(2 * np.pi * freq * 2.76 * t) * decay(len(t), seconds * 0.12)
    return tone * decay(len(t), seconds * 0.35) * env(len(t), 0.002, 0.05)


def choir(freqs, seconds):
    """a soft additive pad: each note a few detuned voices, odd and even partials falling off,
    a slow vowel-like sweep."""
    t = t_of(seconds)
    out = np.zeros(len(t))
    for f in freqs:
        for d in (-0.35, 0.0, 0.4):
            ff = hz(f) * 2 ** (d / 100 * 12 / 12)
            ph = rng.uniform(0, 2 * np.pi)
            for k, a in ((1, 1.0), (2, 0.45), (3, 0.28), (4, 0.12), (5, 0.07)):
                vib = 1 + 0.003 * np.sin(2 * np.pi * 5.2 * t + ph)
                out += a * np.sin(2 * np.pi * ff * k * t * vib + ph * k)
    out /= len(freqs) * 3
    sweep = 0.75 + 0.25 * np.sin(2 * np.pi * t / seconds)
    return out * sweep * env(len(t), seconds * 0.35, seconds * 0.4)


def drone(seconds):
    """the void: D1 and A1, a slow beating saw through a low-pass that breathes."""
    t = t_of(seconds)
    out = np.zeros(len(t))
    for m, a in ((26, 1.0), (33, 0.6), (38, 0.35)):
        for d in (-0.15, 0.15):
            f = hz(m) * (1 + d / 100)
            saw = 2 * ((f * t) % 1.0) - 1
            out += a * saw
    out = lowpass(out, 180)
    return out * (0.8 + 0.2 * np.sin(2 * np.pi * t / (seconds / 4)))


def timpani(freq, seconds=1.6):
    t = t_of(seconds)
    pitch = freq * (1 + 0.6 * decay(len(t), 0.02))
    body = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * decay(len(t), 0.45)
    hit = lowpass(rng.standard_normal(len(t)), 900) * decay(len(t), 0.03)
    return (body + 0.6 * hit) * env(len(t), 0.001, 0.2)


def pulse(freq, seconds):
    """Vesper's violet pulse: a soft sub thump."""
    t = t_of(seconds)
    pitch = freq * (1 + 1.5 * decay(len(t), 0.015))
    return np.sin(2 * np.pi * np.cumsum(pitch) / SR) * decay(len(t), 0.18)


def glass(freq, seconds):
    """the Mysterious One's lead: a glassy pair of sines a fifth and an octave up, slow attack,
    a little vibrato coming in."""
    t = t_of(seconds)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / 0.6, 0, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    tone = np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.18 * np.sin(3 * ph) + 0.08 * np.sin(1.5 * ph)
    return tone * env(len(t), 0.08, seconds * 0.5)


def sparkle(seconds):
    t = t_of(seconds)
    f = rng.uniform(2400, 5200)
    return np.sin(2 * np.pi * f * t) * decay(len(t), 0.25) * env(len(t), 0.001, 0.1)


# ---- the music ----

BPM = 84
BEAT = 60 / BPM
BAR = 4 * BEAT
BARS = 32
# Dm, Bb, Gm, A: two bars each, round four times
CHORDS = [
    ([50, 53, 57, 62], 38),   # Dm (D3 F3 A3 D4), root D2
    ([46, 53, 58, 62], 34),   # Bb
    ([43, 55, 58, 62], 31),   # Gm
    ([45, 52, 57, 61], 33),   # A (the C# leaning home)
]
# the bells: each chord's notes an octave or two up, an eighth-note figure
ARP = [0, 2, 1, 3, 2, 1, 3, 2]
# the theme (MIDI, beats): from bar 17, two four-bar phrases, played twice
THEME = [
    (74, 2), (77, 1), (76, 1), (74, 2), (69, 2),
    (70, 2), (74, 1), (72, 1), (70, 3), (69, 1),
    (67, 2), (70, 1), (74, 1), (79, 3), (77, 1),
    (76, 2), (73, 2), (74, 4),
]


def music():
    n = int(BARS * BAR * SR)
    dry = np.zeros((2, n))
    wet = np.zeros((2, n))

    # the drone, all through (rendered in one piece, so it loops: its length is the loop's)
    d = drone(BARS * BAR)
    place(dry, pan(d, 0) * 0.32, 0)

    for bar in range(BARS):
        chord, root = CHORDS[(bar // 2) % 4]
        start = int(bar * BAR * SR)
        # the choir, a chord over two bars, overlapping the next a little
        if bar % 2 == 0:
            c = choir(chord, 2 * BAR + 0.8)
            place(wet, pan(c, -0.25) * 0.20, start)
            place(wet, pan(choir([m + 12 for m in chord[1:]], 2 * BAR + 0.8), 0.3) * 0.08, start)
        # the bells from bar 3: eighth notes, softer on the off-beats
        if bar >= 2:
            for i, k in enumerate(ARP):
                f = hz(chord[k] + 24)
                at = start + int(i * BEAT / 2 * SR)
                vol = (0.09 if i % 2 == 0 else 0.055) * (1.15 if bar >= 16 else 1.0)
                place(wet, pan(bell(f, 1.6, 0.8), 0.45 if i % 2 else -0.1) * vol, at)
        # Vesper's pulse from bar 5, on every beat; the timpani on each bar's first beat
        if bar >= 4:
            for beat in range(4):
                at = start + int(beat * BEAT * SR)
                place(dry, pan(pulse(hz(root), 0.6), 0) * (0.30 if beat == 0 else 0.17), at)
            place(wet, pan(timpani(hz(root + 12)), -0.35) * 0.32, start)
            if bar % 4 == 3:  # a roll into the next phrase
                for i in range(4):
                    place(wet, pan(timpani(hz(root + 12), 0.8), -0.35) * (0.10 + 0.04 * i),
                          start + int((3 * BEAT + i * BEAT / 4) * SR))
        # stars
        for _ in range(3):
            at = start + int(rng.uniform(0, BAR) * SR)
            place(wet, pan(sparkle(0.9), rng.uniform(-0.8, 0.8)) * 0.025, at)

    # the theme from bar 17, twice
    for rep in range(2):
        at_beat = (16 + rep * 8) * 4
        for note, beats in THEME:
            g = glass(hz(note), beats * BEAT + 0.4)
            place(wet, pan(g, 0.1) * 0.11, int(at_beat * BEAT * SR))
            # the answer an octave down, quieter, half a beat behind (the Between's echo)
            place(wet, pan(glass(hz(note - 12), beats * BEAT + 0.4), -0.4) * 0.05, int((at_beat + 0.5) * BEAT * SR))
            at_beat += beats

    # the room: a long, dark reverb on everything but the drone and the pulse; its tail folds back
    # onto the start, so the loop has none of its own
    ir = reverb_ir(4.5, damp=0.55)
    room = np.zeros((2, n))
    for ch in range(2):
        r = convolve(wet[ch], ir[ch:ch + 1])[0]
        room[ch] += r[:n]
        tail = r[n:]
        k = 0
        while k < len(tail):
            m = min(n, len(tail) - k)
            room[ch, :m] += tail[k:k + m]
            k += m
    mix = dry + 0.55 * wet + 0.5 * room
    return master(mix, 0.58)


def master(x, peak):
    x = x - x.mean(axis=1, keepdims=True)
    x = np.tanh(x * 1.3) / np.tanh(1.3) if np.abs(x).max() > 1 else x
    return x * (peak / np.abs(x).max())


# ---- the bosses' sounds ----

def noise(seconds):
    return rng.standard_normal(int(seconds * SR))


def vesper_hit():
    # a violet crystal chipped: two high bells a tritone apart over a breath of noise
    s = bell(hz(86), 0.35, 1.3) + 0.7 * bell(hz(80), 0.35, 1.0)
    n = bandpass(noise(0.35), 3000, 9000) * decay(int(0.35 * SR), 0.03)
    return s * 0.6 + n * 0.25


def vesper_death():
    # her starlight falls away: a falling cascade of bells, a sinking choir
    out = np.zeros(int(2.4 * SR))
    for i, m in enumerate([93, 89, 86, 81, 77, 74, 69, 65]):
        b = bell(hz(m), 1.4, 1.1) * (0.55 - i * 0.04)
        at = int(i * 0.12 * SR)
        out[at:at + len(b)] += b[: len(out) - at]
    c = choir([62, 65, 69], 2.4)
    t = t_of(2.4)
    return out + 0.6 * c * np.linspace(1, 0.3, len(t))


def aurel_hit():
    # gold armour struck: inharmonic metal partials, quick
    t = t_of(0.3)
    out = sum(a * np.sin(2 * np.pi * f * t) * decay(len(t), dd) for f, a, dd in
              ((620, 1.0, 0.09), (1530, 0.7, 0.06), (2710, 0.5, 0.04), (3890, 0.35, 0.03), (5230, 0.2, 0.02)))
    clank = bandpass(noise(0.3), 1500, 7000) * decay(len(t), 0.01)
    return out * 0.45 + clank * 0.35


def aurel_death():
    # his sword rings out and the dawn bells toll
    t = t_of(2.8)
    ring = sum(a * np.sin(2 * np.pi * f * t) * decay(len(t), dd) for f, a, dd in
               ((1180, 1.0, 1.2), (2950, 0.5, 0.7), (4410, 0.3, 0.5)))
    out = 0.35 * ring
    for i, m in enumerate([74, 81, 86]):
        b = bell(hz(m), 2.2, 0.9) * 0.55
        at = int((0.15 + i * 0.35) * SR)
        out[at:at + len(b)] += b[: len(out) - at]
    return out


def mysterious_hit():
    # something vast struck: a deep thud, a gold and a violet tone at once, beating
    t = t_of(0.45)
    thud = pulse(hz(31), 0.45) * 1.2
    tones = (np.sin(2 * np.pi * hz(62) * t) + np.sin(2 * np.pi * hz(63) * t)) * decay(len(t), 0.12) * 0.35
    air = bandpass(noise(0.45), 200, 1200) * decay(len(t), 0.05) * 0.4
    return thud + tones + air


def mysterious_death():
    # the rift closes: a rumble swelling, a gold and a violet tone rising together, then gone
    t = t_of(3.6)
    n = len(t)
    rumble = lowpass(noise(3.6), 120) * 3 * env(n, 1.6, 1.4)
    sweep = hz(38) * 2 ** (np.clip(t / 2.6, 0, 1) * 3)
    gold = np.sin(2 * np.pi * np.cumsum(sweep) / SR)
    violet = np.sin(2 * np.pi * np.cumsum(sweep * 1.4983) / SR)
    rise = (gold + 0.8 * violet) * env(n, 2.2, 1.2) * 0.35
    out = rumble + rise
    # and the last toll as it shuts
    b = bell(hz(50), 1.0, 1.4)
    at = int(2.6 * SR)
    out[at:at + len(b)] += b[: n - at] * 0.7
    return out


SFX = {
    "vesper_hit": vesper_hit, "vesper_death": vesper_death,
    "aurel_hit": aurel_hit, "aurel_death": aurel_death,
    "mysterious_hit": mysterious_hit, "mysterious_death": mysterious_death,
}


def mp3(x, path, kbps, channels):
    enc = lameenc.Encoder()
    enc.set_bit_rate(kbps)
    enc.set_in_sample_rate(SR)
    enc.set_channels(channels)
    enc.set_quality(2)
    pcm = np.clip(x, -1, 1)
    pcm = (pcm.T if channels == 2 else pcm) * 32767
    data = enc.encode(pcm.astype("<i2").tobytes()) + enc.flush()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    print("wrote %s (%.1f s, %d KB)" % (os.path.relpath(path, ROOT), x.shape[-1] / SR, len(data) // 1024))


def main():
    for name, make in SFX.items():
        x = make()
        x = x - x.mean()
        x = x * (0.7 / np.abs(x).max())
        x[-int(0.01 * SR):] *= np.linspace(1, 0, int(0.01 * SR))
        mp3(x, os.path.join(WEB, "sfx", "monster", name + ".mp3"), 96, 1)
    mp3(music(), os.path.join(WEB, "music", "the_black_between.mp3"), 160, 2)


if __name__ == "__main__":
    main()

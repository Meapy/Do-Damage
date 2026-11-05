"""Makes the pings' sounds (the Rust client's middle mouse button, client-rs/app/src/pings.rs):
source/Shared/resources/web/sfx/ping.mp3, a bright two-note chime, and ping_danger.mp3, two
sharp falling beeps. Synthesised here, so they're ours; needs ffmpeg on the PATH for the mp3s.

    python tools/make_ping_sounds.py
"""

import math
import os
import struct
import subprocess
import sys
import tempfile
import wave

RATE = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "source", "Shared", "resources", "web", "sfx")


def note(freq, start, length, volume, overtones=((2.0, 0.25), (3.0, 0.08)), attack=0.004, square=0.0):
    """A note: a sine (and its overtones, or a touch of square) fading out over `length` s."""
    out = []
    for i in range(int(length * RATE)):
        t = i / RATE
        env = min(1.0, t / attack) * math.exp(-t * 7.0 / length)
        s = math.sin(2 * math.pi * freq * t)
        for mult, amp in overtones:
            s += amp * math.sin(2 * math.pi * freq * mult * t)
        if square:
            s = (1 - square) * s + square * (1.0 if s >= 0 else -1.0)
        out.append((start + i, s * env * volume))
    return out


def mix(notes, length):
    buf = [0.0] * int(length * RATE)
    for n in notes:
        for i, v in n:
            if 0 <= i < len(buf):
                buf[i] += v
    peak = max(1e-9, max(abs(v) for v in buf))
    return [v / peak * 0.8 for v in buf]


def write(name, samples):
    with tempfile.TemporaryDirectory() as tmp:
        wav = os.path.join(tmp, name + ".wav")
        with wave.open(wav, "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(RATE)
            w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in samples))
        mp3 = os.path.join(OUT, name + ".mp3")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-ac", "1", "-ar", str(RATE), "-b:a", "64k", mp3], check=True)
        print(os.path.normpath(mp3))


def main():
    if not os.path.isdir(OUT):
        sys.exit(f"no {OUT}")
    # look, go, loot, help: E6 then A6, bell-like
    write("ping", mix([note(1318.5, 0, 0.32, 0.7), note(1760.0, int(0.075 * RATE), 0.42, 0.8)], 0.55))
    # danger: two quick falling beeps with an edge to them
    beep = ((2.0, 0.15),)
    write("ping_danger", mix([
        note(988.0, 0, 0.14, 0.8, beep, square=0.35),
        note(740.0, int(0.13 * RATE), 0.22, 0.8, beep, square=0.35),
    ], 0.4))


if __name__ == "__main__":
    main()

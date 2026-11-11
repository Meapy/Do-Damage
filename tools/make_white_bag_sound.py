"""Makes the white bag's sound (the Rust client plays it when a white bag lands near you, with a
flash round the edges of the view: game.rs, `WHITE_BAG`): source/Shared/resources/web/sfx/
white_bag.mp3, a bright crystal "ting": a high bell struck twice in quick succession, the second
an octave and a fifth up, with a shimmer of high partials ringing out. Synthesised here, so it's
ours; needs ffmpeg (on the PATH, or pip install imageio-ffmpeg).

    python tools/make_white_bag_sound.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_ping_sounds import OUT, RATE, mix, note, write  # noqa: E402

# a bell's inharmonic partials (struck metal), quieter the higher they go
BELL = ((2.76, 0.32), (5.40, 0.14), (8.93, 0.06))


def main():
    if not os.path.isdir(OUT):
        sys.exit(f"no {OUT}")
    write("white_bag", mix([
        note(1567.98, 0, 0.9, 0.55, BELL, attack=0.002),                   # G6
        note(2349.32, int(0.07 * RATE), 1.3, 0.75, BELL, attack=0.002),    # D7, ringing longer
        note(4698.63, int(0.07 * RATE), 0.5, 0.18, (), attack=0.002),      # its sparkle
        note(3135.96, int(0.16 * RATE), 1.1, 0.22, ((2.0, 0.2),), attack=0.01),  # a soft shimmer under it
    ], 1.4))


if __name__ == "__main__":
    main()

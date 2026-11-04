"""WT49: the reference library's timecode for every frame a ruler tick can land on, at the rates
the ruler must label exactly: 23.976, 25, 29.97 (drop-frame) and 59.94 (drop-frame).

    python make_timecodes.py

Ticks fall on multiples of their step from frame 0: 1, 2, 5 or 10 frames, or a whole number of
nominal seconds (1 s to 1 h). timecodes.json holds, per rate, the timecode
opentimelineio 0.18.1 writes (drop-frame inferred from the rate) for every multiple of every
frame step in the first two minutes and of every second step in the first two hours.
"""

import json
from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent
RATES = {"23.976": 24000 / 1001, "25": 25.0, "29.97": 30000 / 1001, "59.94": 60000 / 1001}
FRAME_STEPS = [1, 2, 5, 10]
SECOND_STEPS = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600]


def main():
    out = {}
    for name, rate in RATES.items():
        nominal = round(rate)
        frames = set()
        for step in FRAME_STEPS:
            frames.update(range(0, nominal * 120 + 1, step))
        for seconds in SECOND_STEPS:
            frames.update(range(0, nominal * 7200 + 1, nominal * seconds))
        out[name] = {
            "rate": rate,
            "timecodes": {str(f): otio.opentime.to_timecode(otio.opentime.RationalTime(f, rate), rate) for f in sorted(frames)},
        }
    (HERE / "timecodes.json").write_text(json.dumps(out, indent=1, sort_keys=True) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

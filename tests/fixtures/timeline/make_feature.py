"""WT47: a feature-length cut for drawing the timeline at scale, written by opentimelineio 0.18.1.

    python make_feature.py

Writes feature.otio: 12 video and 12 audio tracks of 125 clips each, 3,000 clips in all, with
lengths from 1 to 8 seconds, a gap every 9th item and a dissolve every 25th cut on the video
tracks. Deterministic, so the drawing benchmark always measures the same cut.
"""

import random
from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent
RT = otio.opentime.RationalTime
TR = otio.opentime.TimeRange
RATE = 24


def track(kind, number, rng):
    t = otio.schema.Track(name=f"{kind[0]}{number}", kind=kind)
    for i in range(125):
        if i and i % 9 == 0:
            t.append(otio.schema.Gap(source_range=TR(RT(0, RATE), RT(rng.randint(12, 48), RATE))))
        if kind == otio.schema.TrackKind.Video and i and i % 25 == 0:
            t.append(otio.schema.Transition(name="dissolve", transition_type="SMPTE_Dissolve",
                                            in_offset=RT(6, RATE), out_offset=RT(6, RATE)))
        length = rng.randint(RATE, 8 * RATE)
        t.append(otio.schema.Clip(
            name=f"{kind[0]}{number} shot {i + 1:03d}",
            media_reference=otio.schema.ExternalReference(target_url=f"file:///media/{kind[0]}{number}_{i + 1:03d}.mov"),
            source_range=TR(RT(rng.randint(0, 240), RATE), RT(length, RATE))))
    return t


def main():
    rng = random.Random(47)
    timeline = otio.schema.Timeline(name="feature", global_start_time=RT(86400, RATE))
    for n in range(1, 13):
        timeline.tracks.append(track(otio.schema.TrackKind.Video, n, rng))
    for n in range(1, 13):
        timeline.tracks.append(track(otio.schema.TrackKind.Audio, n, rng))
    (HERE / "feature.otio").write_bytes(timeline.to_json_string().encode("utf-8"))


if __name__ == "__main__":
    main()

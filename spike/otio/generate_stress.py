"""WT11: the stress timelines the OTIO spike measures, made with the reference library.

No real feature timeline can be committed, so these stand in for one: written by
opentimelineio itself, so the file is what the reference writes, and seeded, so every run
measures the same bytes.

    python generate_stress.py <out-folder>

- feature-3000.otio: a 2-hour, 24 fps feature, 3,000 clips over 4 video and 4 audio tracks,
  with gaps, transitions, markers, speed effects and studio metadata on every clip.
- feature-10000.otio: the same shape at 10,000 clips, to see how load time scales.
- nested-unknown.otio: nested stacks, and a schema no library knows (MyStudio.Note.3), which
  must survive a round trip untouched.
"""

import json
import random
import sys
from pathlib import Path

import opentimelineio as otio

RATE = 24.0


def rt(frames: float) -> otio.opentime.RationalTime:
    return otio.opentime.RationalTime(frames, RATE)


def clip(rng: random.Random, index: int, frames: int) -> otio.schema.Clip:
    start = rng.randrange(0, 2000)
    media = otio.schema.ExternalReference(
        target_url=f"file:///D:/footage/reel{index % 40:02d}/A{index:05d}.mov",
        available_range=otio.opentime.TimeRange(rt(0), rt(start + frames + 200)),
    )
    media.metadata["probe"] = {"codec": rng.choice(["prores", "dnxhr", "h264"]), "bits": 10}
    c = otio.schema.Clip(
        name=f"A{index:05d}",
        media_reference=media,
        source_range=otio.opentime.TimeRange(rt(start), rt(frames)),
    )
    c.metadata["studio"] = {
        "scene": index // 30,
        "take": index % 7,
        "notes": f"take {index % 7} is the keeper" if index % 11 == 0 else "",
        "tags": ["vfx"] if index % 13 == 0 else [],
    }
    if index % 17 == 0:
        c.effects.append(otio.schema.LinearTimeWarp(name="speed", time_scalar=0.5))
    if index % 9 == 0:
        c.markers.append(
            otio.schema.Marker(
                name=f"note {index}",
                marked_range=otio.opentime.TimeRange(rt(start + 2), rt(0)),
                color=otio.schema.MarkerColor.RED,
            )
        )
    return c


def feature(clips: int, seed: int) -> otio.schema.Timeline:
    rng = random.Random(seed)
    timeline = otio.schema.Timeline(name=f"feature-{clips}")
    timeline.global_start_time = otio.opentime.RationalTime(86400, RATE)
    per_track = clips // 8
    # 2 hours at 24 fps over per_track clips, gaps included.
    average = max(4, int(2 * 60 * 60 * RATE / per_track))
    index = 0
    for kind, count in ((otio.schema.TrackKind.Video, 4), (otio.schema.TrackKind.Audio, 4)):
        for t in range(count):
            track = otio.schema.Track(name=f"{kind[0]}{t + 1}", kind=kind)
            for n in range(per_track):
                frames = max(2, int(rng.gauss(average, average / 3)))
                track.append(clip(rng, index, frames))
                index += 1
                if n % 25 == 24:
                    track.append(otio.schema.Gap(source_range=otio.opentime.TimeRange(rt(0), rt(12))))
                if kind == otio.schema.TrackKind.Video and n % 40 == 39 and n + 1 < per_track:
                    track.append(
                        otio.schema.Transition(
                            name="dissolve",
                            transition_type=otio.schema.TransitionTypes.SMPTE_Dissolve,
                            in_offset=rt(2),
                            out_offset=rt(2),
                        )
                    )
            timeline.tracks.append(track)
    return timeline


def nested_unknown() -> otio.schema.Timeline:
    unknown = {
        "OTIO_SCHEMA": "MyStudio.Note.3",
        "author": "editor",
        "body": "keep the unknown schema byte for byte",
        "pinned": True,
        "range": {"OTIO_SCHEMA": "TimeRange.1",
                  "duration": {"OTIO_SCHEMA": "RationalTime.1", "rate": 24.0, "value": 48.0},
                  "start_time": {"OTIO_SCHEMA": "RationalTime.1", "rate": 24.0, "value": 0.0}},
        "children": [{"OTIO_SCHEMA": "MyStudio.Reply.1", "text": "agreed", "score": 0.125}],
    }
    timeline = otio.schema.Timeline(name="nested-unknown")
    outer = otio.schema.Track(name="V1")
    for depth in range(3):
        stack = otio.schema.Stack(name=f"stack-{depth}")
        inner = otio.schema.Track(name=f"inner-{depth}")
        inner.append(otio.schema.Clip(
            name=f"inner-clip-{depth}",
            media_reference=otio.schema.MissingReference(),
            source_range=otio.opentime.TimeRange(rt(0), rt(24)),
        ))
        stack.append(inner)
        outer.append(stack)
    timeline.tracks.append(outer)
    # An object of a schema the library does not know, read back so it is an UnknownSchema.
    timeline.tracks.metadata["note"] = otio.adapters.read_from_string(json.dumps(unknown), "otio_json")
    return timeline


def main() -> None:
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    otio.adapters.write_to_file(feature(3000, 3000), str(out / "feature-3000.otio"))
    otio.adapters.write_to_file(feature(10000, 10000), str(out / "feature-10000.otio"))
    otio.adapters.write_to_file(nested_unknown(), str(out / "nested-unknown.otio"))


if __name__ == "__main__":
    main()

"""WT19: timelines broken on purpose, one fault each, written by opentimelineio 0.18.1 so that
everything else about them is valid. expected.json lists the problems WinTimeline's validator
must report for each, as "code@path" (path: child indices from the tracks stack).

    python make_broken.py
"""

import json
from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent
RT = otio.opentime.RationalTime
TR = otio.opentime.TimeRange


def clip(name, frames, rate=24, start=0, available=None):
    reference = otio.schema.MissingReference()
    if available is not None:
        reference = otio.schema.ExternalReference(target_url="file:///media/" + name + ".mov", available_range=available)
    return otio.schema.Clip(name=name, media_reference=reference, source_range=TR(RT(start, rate), RT(frames, rate)))


def timeline(*children, start=None):
    t = otio.schema.Timeline(name="broken")
    if start is not None:
        t.global_start_time = start
    track = otio.schema.Track(name="V1")
    for child in children:
        track.append(child)
    t.tracks.append(track)
    return t


def transition(name, in_frames, out_frames):
    return otio.schema.Transition(name=name, transition_type="SMPTE_Dissolve", in_offset=RT(in_frames, 24), out_offset=RT(out_frames, 24))


def main():
    files = {}

    files["clean"] = (timeline(clip("a", 24), transition("x", 4, 4), clip("b", 24), start=RT(86400, 24)), [])
    files["long-transition"] = (timeline(clip("a", 10), transition("x", 20, 2), clip("b", 10)), ["transition-too-long@0/1"])
    files["orphan-transition"] = (timeline(transition("x", 2, 2), clip("a", 10)), ["transition-without-neighbour@0/0"])
    files["zero-duration"] = (timeline(clip("zero", 0), clip("negative", -5)), ["non-positive-duration@0/0", "non-positive-duration@0/1"])
    files["outside-media"] = (timeline(clip("late", 20, start=40, available=TR(RT(0, 24), RT(48, 24)))), ["source-outside-media@0/0"])
    files["rate-mismatch"] = (timeline(clip("film", 24), clip("pal", 25, rate=25), start=RT(0, 24)), ["rate-mismatch@0/1"])

    no_range = otio.schema.Clip(name="nothing", media_reference=otio.schema.MissingReference())
    files["no-range"] = (timeline(no_range), ["range-uncomputable@0/0"])

    marked = clip("marked", 24)
    for _ in range(2):
        marked.markers.append(otio.schema.Marker(name="fix", marked_range=TR(RT(1, 24), RT(0, 24))))
    files["duplicate-markers"] = (timeline(marked), ["duplicate-marker-name@0/0"])

    # Ten levels of stack inside the track, which is itself level 1: levels 9 to 11 are past the limit of 8.
    inner = clip("deep", 24)
    for _ in range(10):
        stack = otio.schema.Stack(name="nest")
        stack.append(inner)
        inner = stack
    deep = timeline(inner)
    files["deep-nesting"] = (deep, ["nesting-too-deep@" + "0" + "/0" * k for k in (8, 9, 10)])

    expected = {}
    for name, (t, problems) in files.items():
        (HERE / f"{name}.otio").write_bytes(t.to_json_string().encode("utf-8"))
        expected[name] = problems
    (HERE / "expected.json").write_bytes((json.dumps(expected, indent=1) + "\n").encode("utf-8"))


if __name__ == "__main__":
    main()

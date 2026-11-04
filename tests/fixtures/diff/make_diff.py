"""WT21: two versions of a cut whose differences are known, written by opentimelineio 0.18.1.

    python make_diff.py

Writes cut_v1.otio, cut_v2.otio, and cuts.otio holding both as one collection. From v1 to v2:
c5 moved to the head, c2 trimmed (head +5, tail -3 frames), c3 removed, c7 replaced by c7b,
c6 added at the end; c1, c4 and the audio clip a1 are unchanged (c1 and c4 only ripple).
"""

from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent
RT = otio.opentime.RationalTime
TR = otio.opentime.TimeRange


def clip(name, media, start, duration):
    return otio.schema.Clip(
        name=name,
        media_reference=otio.schema.ExternalReference(target_url=f"file:///media/{media}.mov"),
        source_range=TR(RT(start, 24), RT(duration, 24)))


def cut(name, video):
    timeline = otio.schema.Timeline(name=name, global_start_time=RT(86400, 24))
    v1 = otio.schema.Track(name="V1")
    for c in video:
        v1.append(clip(*c))
    a1 = otio.schema.Track(name="A1", kind=otio.schema.TrackKind.Audio)
    a1.append(clip("a1", "S001", 0, 120))
    timeline.tracks.append(v1)
    timeline.tracks.append(a1)
    return timeline


V1 = [
    ("c1", "A001", 0, 48),
    ("c2", "A002", 10, 48),
    ("c3", "A003", 0, 24),
    ("c4", "A004", 0, 36),
    ("c5", "A005", 0, 24),
    ("c7", "A007", 0, 24),
]

V2 = [
    ("c5", "A005", 0, 24),
    ("c1", "A001", 0, 48),
    ("c2", "A002", 15, 40),
    ("c4", "A004", 0, 36),
    ("c7b", "B007", 0, 24),
    ("c6", "A006", 0, 30),
]


def main():
    first, second = cut("cut v1", V1), cut("cut v2", V2)
    (HERE / "cut_v1.otio").write_bytes(first.to_json_string().encode("utf-8"))
    (HERE / "cut_v2.otio").write_bytes(second.to_json_string().encode("utf-8"))
    collection = otio.schema.SerializableCollection(name="cuts")
    collection.append(cut("cut v1", V1))
    collection.append(cut("cut v2", V2))
    (HERE / "cuts.otio").write_bytes(collection.to_json_string().encode("utf-8"))


if __name__ == "__main__":
    main()

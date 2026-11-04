"""WT20: a file holding several timelines, as editorial hand-offs carry reels.

    python make_collections.py

Writes reels.otio, a SerializableCollection of three reel timelines written by
opentimelineio 0.18.1, whose bytes a save must reproduce exactly.
"""

from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent
RT = otio.opentime.RationalTime
TR = otio.opentime.TimeRange


def reel(number, clips):
    timeline = otio.schema.Timeline(name=f"reel{number}", global_start_time=RT(86400 + number * 3600 * 24, 24))
    track = otio.schema.Track(name="V1")
    for i in range(clips):
        track.append(otio.schema.Clip(
            name=f"r{number}c{i + 1}",
            media_reference=otio.schema.ExternalReference(target_url=f"file:///media/r{number}c{i + 1}.mov"),
            source_range=TR(RT(0, 24), RT(24 * (i + 1), 24))))
    timeline.tracks.append(track)
    return timeline


def main():
    collection = otio.schema.SerializableCollection(name="feature reels")
    for number, clips in ((1, 2), (2, 3), (3, 1)):
        collection.append(reel(number, clips))
    (HERE / "reels.otio").write_bytes(collection.to_json_string().encode("utf-8"))


if __name__ == "__main__":
    main()

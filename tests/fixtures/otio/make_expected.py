"""WT12: what the reference library answers for every object in the corpus, for the model to match.

    python make_expected.py

Reads corpus/*.otio with opentimelineio 0.18.1 (pip install opentimelineio==0.18.1) and writes
expected/<name>.json. Re-run it only when the corpus or the reference version changes; the
tests read the JSON, so neither Python nor the library is needed to run them.

For each composable, depth first with its index path from the timeline's tracks: its schema,
name, duration, range in parent and trimmed range in parent, and for items the available and
trimmed ranges. A call the reference refuses is recorded as "error" and one answering None as
null, so the model must refuse and answer None in the same places. Times are [value, rate].
Timecodes: each clip's source start and the global start time at the corpus's own rates.
"""

import json
from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent


def t(time):
    return [time.value, time.rate]


def r(time_range):
    if time_range is None:
        return None
    return [t(time_range.start_time), t(time_range.duration)]


def ask(call, convert):
    try:
        return convert(call())
    except Exception:  # the reference refused: the model must refuse too
        return "error"


def timecode(time, rate):
    try:
        return time.to_timecode(rate)
    except Exception:
        return "error"


def walk(composition, path, out):
    for index, child in enumerate(composition):
        here = f"{path}/{index}" if path else str(index)
        entry = {
            "path": here,
            "schema": child.schema_name(),
            "name": child.name,
            "duration": ask(child.duration, t),
            "range_in_parent": ask(child.range_in_parent, r),
            "trimmed_range_in_parent": ask(child.trimmed_range_in_parent, r),
        }
        if isinstance(child, otio.core.Item):
            entry["available_range"] = ask(child.available_range, r)
            entry["trimmed_range"] = ask(child.trimmed_range, r)
        if isinstance(child, otio.schema.Clip) and child.source_range is not None:
            start = child.source_range.start_time
            entry["source_start_timecode"] = timecode(start, start.rate)
        out.append(entry)
        if isinstance(child, otio.core.Composition):
            walk(child, here, out)


def main():
    out_dir = HERE / "expected"
    out_dir.mkdir(exist_ok=True)
    for file in sorted((HERE / "corpus").glob("*.otio")):
        timeline = otio.adapters.read_from_file(str(file))
        expected = {"file": file.name, "schema": timeline.schema_name()}
        if isinstance(timeline, otio.schema.Timeline):
            expected["duration"] = ask(timeline.duration, t)
            expected["clips"] = len(list(timeline.find_clips()))
            start = timeline.global_start_time
            expected["global_start_timecode"] = None if start is None else timecode(start, start.rate)
            children = []
            walk(timeline.tracks, "", children)
            expected["children"] = children
        (out_dir / (file.stem + ".json")).write_text(json.dumps(expected, indent=1) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

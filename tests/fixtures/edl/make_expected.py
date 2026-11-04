"""WT15: what the reference CMX 3600 adapter (otio-cmx3600-adapter 1.0.0, OpenTimelineIO
0.18.1) reads from each sample EDL, for WinTimeline's EDL reader to match byte for byte.

    python make_expected.py

samples/ are the adapter repository's own test EDLs (Apache-2.0, see samples/LICENSE.txt).
For each, expected/<name>.otio is the timeline the reference read, written by the reference;
where the reference refuses the file, expected/<name>.error.txt holds its message instead.
Every file is also read with ignore_timecode_mismatch=True into <name>.ignore.otio (or
.ignore.error.txt). 25fps.edl is read at 25 fps, the rest at the default 24.
"""

from pathlib import Path

from otio_cmx3600_adapter import cmx_3600

HERE = Path(__file__).parent


def main():
    out = HERE / "expected"
    out.mkdir(exist_ok=True)
    for old in out.iterdir():
        old.unlink()
    for edl in sorted((HERE / "samples").glob("*.edl")):
        rate = 25 if edl.stem == "25fps" else 24
        text = edl.read_text(encoding="utf-8")
        for suffix, ignore in (("", False), (".ignore", True)):
            try:
                timeline = cmx_3600.read_from_string(text, rate=rate, ignore_timecode_mismatch=ignore)
                (out / f"{edl.stem}{suffix}.otio").write_bytes(timeline.to_json_string().encode("utf-8"))
            except Exception as error:  # the reference refused: the port must refuse too
                (out / f"{edl.stem}{suffix}.error.txt").write_bytes(f"{type(error).__name__}: {error}".encode("utf-8"))


if __name__ == "__main__":
    main()

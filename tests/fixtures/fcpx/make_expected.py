"""WT156: what the reference FCPXML adapter (otio-fcpx-xml-adapter 1.0.0, OpenTimelineIO
0.18.1) reads from each sample, for WinTimeline's reader to match byte for byte.

    python make_expected.py

samples/ are the adapter repository's own test files (Apache-2.0, see samples/LICENSE.txt).
For each, expected/<name>.otio is what the reference read, written by the reference; where it
refuses the file, expected/<name>.error.txt holds its message instead.
"""

from pathlib import Path

from otio_fcpx_xml_adapter import fcpx_xml

HERE = Path(__file__).parent


def main():
    out = HERE / "expected"
    out.mkdir(exist_ok=True)
    for old in out.iterdir():
        old.unlink()
    for sample in sorted((HERE / "samples").glob("*.fcpxml")):
        try:
            result = fcpx_xml.read_from_string(sample.read_text(encoding="utf-8"))
            (out / f"{sample.stem}.otio").write_bytes(result.to_json_string().encode("utf-8"))
        except Exception as error:  # the reference refused: the port must refuse too
            (out / f"{sample.stem}.error.txt").write_bytes(f"{type(error).__name__}: {error}".encode("utf-8"))


if __name__ == "__main__":
    main()

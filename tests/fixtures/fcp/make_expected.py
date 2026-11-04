"""WT156: what the reference FCP 7 XML adapter (otio-fcp-adapter 1.0.0, OpenTimelineIO 0.18.1)
reads from each sample, for WinTimeline's reader to match byte for byte.

    python make_expected.py

samples/ are the adapter repository's own test files (Apache-2.0, see samples/LICENSE.txt).
For each, expected/<name>.otio is what the reference read, written by the reference; where it
refuses the file, expected/<name>.error.txt holds its message instead.
"""

import importlib.util
import sys
from pathlib import Path

HERE = Path(__file__).parent


def load_reference():
    """The published fcp_xml.py cannot be imported: it reaches outside its package with
    'from .. import (core, opentime, schema)', a leftover from when it lived inside
    opentimelineio, and imports xml.etree.cElementTree, which Python 3.9 removed. Only those
    two imports are repointed; every line of reading logic runs as published."""
    import otio_fcp_adapter

    path = Path(otio_fcp_adapter.__file__).parent / "fcp_xml.py"
    source = path.read_text(encoding="utf-8")
    source = source.replace("from .. import (\n    core,\n    opentime,\n    schema,\n)", "from opentimelineio import core, opentime, schema")
    source = source.replace("from xml.etree import cElementTree", "from xml.etree import ElementTree as cElementTree")
    spec = importlib.util.spec_from_loader("fcp_xml_reference", loader=None)
    module = importlib.util.module_from_spec(spec)
    sys.modules["fcp_xml_reference"] = module
    exec(compile(source, str(path), "exec"), module.__dict__)
    return module


fcp_xml = load_reference()


def main():
    out = HERE / "expected"
    out.mkdir(exist_ok=True)
    for old in out.iterdir():
        old.unlink()
    for sample in sorted((HERE / "samples").glob("*.xml")):
        try:
            result = fcp_xml.read_from_string(sample.read_text(encoding="utf-8"))
            (out / f"{sample.stem}.otio").write_bytes(result.to_json_string().encode("utf-8"))
        except Exception as error:  # the reference refused: the port must refuse too
            (out / f"{sample.stem}.error.txt").write_bytes(f"{type(error).__name__}: {error}".encode("utf-8"))


if __name__ == "__main__":
    main()

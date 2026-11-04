"""WT14: a bundle written by the reference, for WinTimeline's bundle reader and writer to match.

    python make_bundles.py

Builds a two-clip timeline whose media are media/shot-a.mov and media/shot-b.wav beside this
script, plus a third clip on a web URL, and writes it with opentimelineio 0.18.1 as
reference.otioz (MissingIfNotFile). Its content.otio is kept as reference-content.otio for a
byte comparison. The .otiod writer is not used: on Windows it spells the media URL with a
backslash (media\\shot-a.mov), which is the reference's quirk and not the format's.
Re-run only when the reference changes.
"""

import zipfile
from pathlib import Path

import opentimelineio as otio

HERE = Path(__file__).parent.resolve()
RT = otio.opentime.RationalTime
TR = otio.opentime.TimeRange


def clip(name, url):
    reference = otio.schema.ExternalReference(target_url=url, available_range=TR(RT(0, 24), RT(48, 24)))
    reference.metadata["probe"] = {"codec": "prores"}
    return otio.schema.Clip(name=name, media_reference=reference, source_range=TR(RT(0, 24), RT(24, 24)))


def main():
    timeline = otio.schema.Timeline(name="bundle")
    track = otio.schema.Track(name="V1")
    track.append(clip("a", otio.url_utils.url_from_filepath(str(HERE / "media" / "shot-a.mov"))))
    track.append(clip("b", otio.url_utils.url_from_filepath(str(HERE / "media" / "shot-b.wav"))))
    track.append(clip("web", "https://example.com/c.mov"))
    timeline.tracks.append(track)

    target = HERE / "reference.otioz"
    if target.exists():
        target.unlink()
    otio.adapters.write_to_file(timeline, str(target), media_policy=otio.adapters.file_bundle_utils.MediaReferencePolicy.MissingIfNotFile)

    with zipfile.ZipFile(target) as bundle:
        (HERE / "reference-content.otio").write_bytes(bundle.read("content.otio"))

    # What the reference was given, with this machine's media folder as a {MEDIA} placeholder
    # the test fills in with its own checkout's.
    media_url = otio.url_utils.url_from_filepath(str(HERE / "media"))
    source = timeline.to_json_string().replace(media_url, "{MEDIA}")
    (HERE / "source.otio").write_bytes(source.encode("utf-8"))


if __name__ == "__main__":
    main()

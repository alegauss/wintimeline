# Decisions

## Block A — Project foundation

- ✅ **WT151** **A texture on the shared device cannot reach D3D12 without a copy, and nothing reports VRAM pressure or GPU decode** — D3D11.1 is the core device and D3D12 only for interop: on an RTX 4060 a 4K BGRA hand-off by shared handle took 0.04 ms against 9.8 ms through staging.

## Block B — OTIO document model and file formats

- ✅ **WT11** **Nobody knows yet whether .otio files are read by a C# port or by the C++ reference library behind P/Invoke** — The OTIO model is a managed C# port, because it loads 1.6x faster in a quarter of the memory and needs no per-call C ABI.
- ✅ **WT22** **Bins, proxies settings, layouts and AI analysis have nowhere to live without polluting the shared .otio** — The project sidecar is a folder of JSON documents with vectors as raw float32 beside a JSON index, not SQLite: 4x faster reads, BCL only (spike/sidecar).
- ✅ **WT160** **Clips cannot be copied and pasted within a timeline or between the tabs of a collection** — A paste goes in at the first cut at or after the playhead on each lane and never cuts a clip in two; splitting at the playhead waits on a split edit (WT74, WT75).
- ✅ **WT162** **A comparison cannot be exported as a change-list EDL or as markers for another tool** — Change-list events go in record order, one that would overlap starting where the last ends with its true place in a RECORD AT comment, since CMX readers refuse overlaps.

## Block C — Media probing and GPU decoding

## Block D — GPU compositing and playback engine

## Block E — Proxies, thumbnails, waveforms and background jobs

## Block F — Timeline view: GPU drawing and navigation

## Block G — Workspace, panels and viewers

## Block H — Editing: tools, trims and drag and drop

- ✅ **WT78** **Clips cannot be copied and pasted within or between timelines, or pasted as attributes** — A paste now overwrites or inserts exactly at the playhead, splitting the clip there, replacing WT160's paste at the next cut from before splits existed.

## Block I — Audio: playback, mixing and meters

## Block J — Effects, titles, keyframes and colour

## Block K — Local AI platform: models, runtimes and hardware fit

- ✅ **WT99** **No inference runtime is wired in, so no model can run on the GPU or NPU of this PC** — ONNX models run on Windows ML's ONNX Runtime, DirectML by default: Revelei measured TensorRT-RTX at 112 ms a tile against 324 ms but a 10-17 s engine build per session.

## Block L — AI-assisted editing

## Block M — Video export and interchange

## Block N — Performance, delivery and the public face

- ✅ **WT149** **UI strings are not centralised, so a later translation or terminology review would touch every view** — UI text is a C# class, not .resx: a multilingual UI is a non-goal, and x:Static on constants needs no generated class during WPF's markup compile.

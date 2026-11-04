# Decisions

## Block A — Project foundation

## Block B — OTIO document model and file formats

- ✅ **WT11** **Nobody knows yet whether .otio files are read by a C# port or by the C++ reference library behind P/Invoke** — The OTIO model is a managed C# port, because it loads 1.6x faster in a quarter of the memory and needs no per-call C ABI.
- ✅ **WT22** **Bins, proxies settings, layouts and AI analysis have nowhere to live without polluting the shared .otio** — The project sidecar is a folder of JSON documents with vectors as raw float32 beside a JSON index, not SQLite: 4x faster reads, BCL only (spike/sidecar).

## Block C — Media probing and GPU decoding

## Block D — GPU compositing and playback engine

## Block E — Proxies, thumbnails, waveforms and background jobs

## Block F — Timeline view: GPU drawing and navigation

## Block G — Workspace, panels and viewers

## Block H — Editing: tools, trims and drag and drop

## Block I — Audio: playback, mixing and meters

## Block J — Effects, titles, keyframes and colour

## Block K — Local AI platform: models, runtimes and hardware fit

- ✅ **WT99** **No inference runtime is wired in, so no model can run on the GPU or NPU of this PC** — ONNX models run on Windows ML's ONNX Runtime, DirectML by default: Revelei measured TensorRT-RTX at 112 ms a tile against 324 ms but a 10-17 s engine build per session.

## Block L — AI-assisted editing

## Block M — Video export and interchange

## Block N — Performance, delivery and the public face

- ✅ **WT149** **UI strings are not centralised, so a later translation or terminology review would touch every view** — UI text is a C# class, not .resx: a multilingual UI is a non-goal, and x:Static on constants needs no generated class during WPF's markup compile.

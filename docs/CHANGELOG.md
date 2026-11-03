# Shipped Ledger

## Block A — Project foundation

- ✅ **WT1** **No WPF .NET 10 solution with the Fluent ThemeMode builds and opens a WinTimeline window** — WinTimeline.slnx builds warning-free with the seven projects and the CLI, and the Fluent window opens with menu, dock area and status bar; winwright cases find it.
- ✅ **WT2** **Nothing stops the model project from referencing WPF or the GPU, or the UI from bypassing the command pipeline** — Architecture tests now hold the layering, a pure BCL-only model with no public setters outside Model.Commands, the UI-framework boundary and the build gates, naming each offender.

## Block B — OTIO document model and file formats

- ✅ **WT11** **Nobody knows yet whether .otio files are read by a C# port or by the C++ reference library behind P/Invoke** — Measured both paths on the OTIO corpus and 10,000-clip files: the C# port matches the reference's output, loads faster, holds a quarter of the memory (design recorded in `spike/otio/README.md`).

## Block C — Media probing and GPU decoding

## Block D — GPU compositing and playback engine

## Block E — Proxies, thumbnails, waveforms and background jobs

## Block F — Timeline view: GPU drawing and navigation

## Block G — Workspace, panels and viewers

## Block H — Editing: tools, trims and drag and drop

## Block I — Audio: playback, mixing and meters

## Block J — Effects, titles, keyframes and colour

## Block K — Local AI platform: models, runtimes and hardware fit

## Block L — AI-assisted editing

## Block M — Video export and interchange

## Block N — Performance, delivery and the public face


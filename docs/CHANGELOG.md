# Shipped Ledger

## Block A — Project foundation

- ✅ **WT1** **No WPF .NET 10 solution with the Fluent ThemeMode builds and opens a WinTimeline window** — WinTimeline.slnx builds warning-free with the seven projects and the CLI, and the Fluent window opens with menu, dock area and status bar; winwright cases find it.
- ✅ **WT2** **Nothing stops the model project from referencing WPF or the GPU, or the UI from bypassing the command pipeline** — Architecture tests now hold the layering, a pure BCL-only model with no public setters outside Model.Commands, the UI-framework boundary and the build gates, naming each offender.
- ✅ **WT3** **No shared D3D11 device exists that decoders, compositor, viewers and AI can all use without copies** — One multithread-protected D3D11.1 device opens on the chosen GPU or WARP, named in the status bar; Media Foundation decodes H.264 into it.
- ✅ **WT4** **WPF has no surface where a D3D swap chain can present video and the GPU-drawn timeline** — GpuSurface hosts a waitable flip swap chain on the shared device; viewer and timeline surfaces resize, follow DPI and report frames to a case, on hardware or WARP.
- ✅ **WT5** **User preferences have nowhere to be stored and validated** — Settings persist atomically in settings.json; a damaged file starts on defaults with a notice and is kept as .broken, a newer one is never overwritten, and the saved GPU is used.
- ✅ **WT6** **Failures in decode, GPU, export or AI leave no record a user can send or a developer can read** — A rotating JSONL log records each layer by category and level, drops sensitive records unless opted in, and Help saves a bundle of logs, settings, GPU, system and crash dump.
- ✅ **WT7** **An unhandled exception or a native crash closes the window without a dump or a recovery offer** — Managed errors are logged with a restart offer and a minidump when fatal; the start after any crash shows a recovery bar with the diagnostics bundle, proven on the VMware guest.
- ✅ **WT8** **There is no one command that builds and runs every test, case and evaluation the way the sibling apps do** — run-tests.cmd builds and runs every test and UI case suite, failing on any red or missing one; run-tests-vm.cmd runs it in the VMware guest, run.cmd tries scenarios, a script makes fixtures.
- ✅ **WT9 (workflow)** **No CI builds the solution or runs the headless tests on each push** — A GitHub Actions workflow builds every project but the UI cases, runs the headless suites and the roadmap lint on Windows; its steps pass when run locally.
- ✅ **WT10 (skill and plugin setting)** **The project skill describes another repository's npm gates and RG prefix, and the winwright plugin is not enabled** — The project skill names WT, this repo's gate (build, headless suites, run-tests-vm.cmd in the guest) and run-commit.cmd; settings enable winwright.

## Block B — OTIO document model and file formats

- ✅ **WT11** **Nobody knows yet whether .otio files are read by a C# port or by the C++ reference library behind P/Invoke** — Measured both paths on the OTIO corpus and 10,000-clip files: the C# port matches the reference's output, loads faster, holds a quarter of the memory (design recorded in `spike/otio/README.md`).
- ✅ **WT12** **No in-memory model holds a timeline: tracks, clips, gaps, transitions, markers and metadata have no types** — The OTIO model with opentime's time and timecode, computed ranges and change notifications loads the whole corpus, and every range matches the reference library's answer.

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


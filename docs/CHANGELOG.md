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
- ✅ **WT155 (fix)** **A GPU-less desk's Basic Render Driver is taken for hardware, so CI's render tests fail and a VM skips WARP** — The Basic Render Driver now counts as software and a GPU without video renders on itself; the workflow's results path is absolute and its actions on Node 24.
- ✅ **WT165** **CI fails every EDL comparison because the Windows runner checks fixtures out with CRLF** — Every file under tests/fixtures and cases/fixtures is checked out byte for byte whatever core.autocrlf says, with the generator scripts kept as text, so CI compares the bytes that were committed.

## Block B — OTIO document model and file formats

- ✅ **WT11** **Nobody knows yet whether .otio files are read by a C# port or by the C++ reference library behind P/Invoke** — Measured both paths on the OTIO corpus and 10,000-clip files: the C# port matches the reference's output, loads faster, holds a quarter of the memory (design recorded in `spike/otio/README.md`).
- ✅ **WT12** **No in-memory model holds a timeline: tracks, clips, gaps, transitions, markers and metadata have no types** — The OTIO model with opentime's time and timecode, computed ranges and change notifications loads the whole corpus, and every range matches the reference library's answer.
- ✅ **WT13** **A .otio file cannot be opened or saved, and unknown schemas from newer OTIO versions would be dropped** — .otio opens and saves from the app; the writer emits the reference's bytes, keeps unknown schemas, downgrades Clip to 1, saves atomically.
- ✅ **WT14** **An .otioz or .otiod bundle cannot be opened, so timelines shipped with their media by other tools are unusable** — .otioz and .otiod open with media extracted on demand and save in the reference's layout; the reference reads bundles written here.
- ✅ **WT15** **EDL (CMX 3600), FCP XML and AAF timelines cannot be imported, which is where most real projects come from** — One adapter interface lists formats for Open, and CMX 3600 EDLs read exactly as the reference adapter reads its 19 samples; FCP XML and the Python bridge are WT156, WT157.
- ✅ **WT16** **No edit can be undone, and there is no single place where a change to the document is described** — Every edit runs as a command through one history with transactions, drag coalescing and a byte budget; 10,000 random sequences undo and redo to the byte, and replay from JSON.
- ✅ **WT17** **A crash or power cut loses every edit since the last manual save** — A flushed journal of every command, undo and transaction rebuilds a crashed session up to its last committed command, undo intact, offered at start; an idle autosave backs it.
- ✅ **WT18** **A timeline whose media moved shows every clip as missing, with no way to point it at the new folder** — Missing media is named on open, and File > Relink media relinks every clip one located file implies, by folder substitution or by name, as one undoable step.
- ✅ **WT19** **A malformed timeline (overlapping transitions, negative durations, rate mismatches) opens silently and misbehaves later** — A validator reports bad transitions, empty items, out-of-media ranges, rate mismatches, deep nesting and duplicate markers on open; View > Problems fixes what it can.
- ✅ **WT20** **Only one timeline can be open, and a file holding a SerializableCollection of timelines shows just the first** — A file of several timelines opens as one tab each with its own undo, saves back byte-equal, and Edit > Duplicate timeline as version adds the next numbered copy beside it.
- ✅ **WT21** **Two versions of a cut cannot be compared, so what changed between v1 and v2 must be found by eye** — View > Compare lists what changed from another timeline to the shown one (moved, trimmed with frame deltas, replaced, added, removed); a known v1/v2 pair yields exactly its five edits.
- ✅ **WT22** **Bins, proxies settings, layouts and AI analysis have nowhere to live without polluting the shared .otio** — cut.otio.wtproj/ keeps bins, proxies, layout, analysis and embeddings beside the .otio, written atomically on save and restored on open; the .otio stays byte-identical.

## Block C — Media probing and GPU decoding

## Block D — GPU compositing and playback engine

## Block E — Proxies, thumbnails, waveforms and background jobs

- ✅ **WT40 (scheduler)** **Long work (proxies, analysis, renders) has no shared queue, priorities, progress or cancellation** — JobScheduler runs typed jobs by priority within resource tokens, with progress, ETA, pause, cancel, retry and throttling; 200 jobs survive a restart through the sidecar, each step once.

## Block F — Timeline view: GPU drawing and navigation

- ✅ **WT47 (panel)** **A timeline with thousands of clips cannot be drawn smoothly by the WPF visual tree** — The timeline surface draws lanes, clips, transitions, names, ruler and playhead with Direct2D from a viewport query, scrolls and zooms by wheel, and exposes each visible clip to UI Automation.

## Block G — Workspace, panels and viewers

- ✅ **WT66** **Actions can only be found in menus, so less common commands are hard to discover** — Ctrl+Shift+P opens a palette ranking every command fuzzily with synonyms and recent ones first, narrows to clips or markers by prefix, and jumps to typed timecodes; a test ties it to every menu item.
- ✅ **WT68 (welcome)** **Opening the app shows an empty window with no recent projects, templates or first steps** — An empty window shows a welcome page with Open, New, recent documents and a drop zone; a dropped document opens and dropped video or audio is probed and laid out as a new timeline.
- ✅ **WT69 (notices)** **Errors, finished jobs and warnings appear as modal dialogs or not at all** — Failures and completions appear as non-modal InfoBars with actions (a failed job's Retry requeues it), open, save and diagnostics report there, and the status bar shows the playhead timecode.

## Block H — Editing: tools, trims and drag and drop

## Block I — Audio: playback, mixing and meters

## Block J — Effects, titles, keyframes and colour

## Block K — Local AI platform: models, runtimes and hardware fit

- ✅ **WT99** **No inference runtime is wired in, so no model can run on the GPU or NPU of this PC** — ONNX models run through Windows ML on the app's GPU or a certified provider, picked by a probe with a named CPU fallback, under one LRU VRAM budget; the native engines moved to their own line.
- ✅ **WT100 (catalog)** **There is no catalog saying which models exist, their licence, size, hardware needs and download source** — A versioned model catalog ships in the app with licence, size, VRAM, providers and pinned URLs with SHA-256; CI checks it against its schema and a nightly job checks every URL and hash.

## Block L — AI-assisted editing

## Block M — Video export and interchange

## Block N — Performance, delivery and the public face


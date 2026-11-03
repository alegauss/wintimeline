# Strategy

## §S1 What WinTimeline is, and for whom

WinTimeline is a Windows-only viewer and editor whose native document is an
OpenTimelineIO timeline, drawn and played by the GPU, with local AI that helps edit, and
video export at the end. It serves two audiences with one tool:

- **Pipeline people** (editorial assistants, VFX coordinators, supervisors, TDs) who receive .otio, EDL and XML files and today need a heavyweight NLE or a Python script just to look at them. For them WinTimeline opens any timeline in seconds, plays it with proxies, shows what changed between versions, fixes it and hands it back without losing a key.
- **Creators** (YouTubers, podcasters, small studios) who want a fast editor on an ordinary Windows laptop. For them the draw is GPU playback, drag and drop, text-based editing and AI that runs offline.

The position against existing tools: lighter than Resolve or Premiere for review and
conform; more of an editor than OTIO viewers such as RV or xSTUDIO; OTIO-native, so
nothing is converted and lost on the way in or out. It joins Revelei and Modelado in the
family promise: AI on your PC, nothing leaves it.

Success is measured by three things: the OTIO corpus round-trips untouched (Block B),
the stress timeline plays without drops on a laptop (Block N budgets), and a person goes
from dropping a folder of clips to an exported video without reading a manual.

## §S2 Milestones: viewer first, then editor, then AI

The blocks are built in four releases, each usable on its own, so the riskiest
technology is proven before the largest UI is written.

1. **Viewer (0.1)**: Blocks A and B, C (decode), D (render graph, playback, viewer display), F (drawing, zoom, ruler, playhead, markers) and the program viewer and inspector from G. A user opens any .otio, sees every track, plays it in sync and reads its metadata. Spikes WT11, WT23 and WT4 come first, in the priority queue, because each one can change the architecture.
2. **Editor (0.2)**: Block E (proxies, thumbnails, waveforms), the rest of G, H (editing and drag and drop), I (audio) and the first effects of J. Saving edits back to .otio is the release's promise.
3. **Export (0.3)**: Block M and the rest of J. The first release that answers the original goal: a timeline becomes a video file that matches the viewer.
4. **AI (0.4 onwards)**: Block K, then L in this order: transcription and text editing, scene detection and search, the assistant, then the creative tools. Each AI feature ships alone, behind its model install.

Block N runs alongside: the bench and budgets from the first release, the installer from
0.1, the Store and the site when 0.3 can export.

## §S3 Architecture in one page

Layers, each a project, each allowed to reference only those below it (held by the
architecture tests, WT2):

- **Model**: the OTIO document, time maths and the command pipeline. Pure .NET, no WPF, no GPU, testable anywhere.
- **Media**: probing and decoding into textures and float audio; owns decoder pools.
- **Render**: the shared D3D11.1 device, GPU surfaces, shaders and caches.
- **Engine**: the render graph evaluator, playback clock, audio engine and job scheduler.
- **Inference**: Windows ML sessions, the model library, and clients for out-of-process native engines.
- **Export**: encoders and verification.
- **App**: WPF shell, panels and the timeline view; it reads the model and issues commands, never mutates the model directly.

One rule holds the design together: **every change to a document is a command**. The UI,
the keyboard, drag and drop, the AI assistant, macros and the CLI all go through the
same pipeline, so undo, autosave journals, history and AI previews come for free and
stay consistent.

A second rule keeps playback honest: **one render graph** produces every frame, whether
for the viewer, a thumbnail of a compound clip or the exporter. Only quality settings
differ, so what plays is what exports.

Native code that may crash (llama.cpp, whisper.cpp, some decoders) runs in host
processes, as Revelei's generation engine does.

## §S4 GPU strategy

The GPU is used end to end, so a 4K frame never visits system memory between decode and
display:

1. Hardware decode (DXVA through Media Foundation, or FFmpeg d3d11va) writes NV12/P010 textures on the shared device.
2. A compute shader converts each frame to the scene-linear working space (RGBA16F).
3. The render graph composites, applies effects and transitions in compute shaders.
4. The viewer presents through a flip-model swap chain with a display transform; the exporter converts back to YUV on the GPU and feeds the hardware encoder.

Direct3D 11.1 is the core API because Revelei's renderer, Media Foundation's device
manager and every vendor's hardware encoder interoperate with it with the least
ceremony; Direct3D 12 is used only where Windows ML needs it, through shared handles.
That choice is measured in WT3 and recorded as a decision.

Memory is managed against the adapter's reported budget: decoders, frame caches,
thumbnail atlases and AI sessions register with one budget and trim in a known order.
Presentation runs on a high-priority queue; AI and background jobs yield to it.

The VMware guest runs everything on WARP, which proves function, never speed; speed is
proven on the three reference machines.

## §S5 Preview and proxy strategy

Real-time preview is guaranteed by layers of fallback, in this order, each automatic and
visible:

1. **Frame cache**: anything seen recently plays from VRAM or compressed RAM.
2. **Original media** decoded on the GPU, when the decode scheduler can meet the deadline.
3. **Lower playback resolution** (1/2, 1/4) when the dropped-frame counter rises.
4. **Proxies**, generated in the background by the hardware encoder, when a codec is too heavy for this machine, as measured, not guessed.

Proxies use OTIO's own multiple media references, so a proxy is a second reference on
the same clip, never a different clip. The saved .otio keeps the original as active;
proxy paths live in the sidecar unless the user chooses to embed them. Export always
uses originals.

The guiding principle: the user should never have to think about proxies, but must
always be able to see which media is playing (the P badge) and force the choice.

## §S6 AI strategy: assistive, local and reviewable

AI in WinTimeline follows four rules.

1. **Local only.** Models run on the PC through Windows ML or native engines; the network is used for catalog downloads alone, enforced by one HTTP client (WT103).
2. **Analyse once, use everywhere.** Transcripts, shots, tags and embeddings are stored per media with the model version, and every feature (search, captions, text editing, the assistant) reads them rather than running models again.
3. **Propose, never impose.** Every AI edit arrives as a batch of ordinary commands with a preview; nothing changes until Apply, and one undo reverts it. The assistant sees a text description of the timeline, not the user's files.
4. **Earn the download.** Each feature states its model size, licence and hardware verdict before installing, and offers the install where it is used.

The ladder of value, which is also the build order: transcription and text-based editing
(the biggest time saver), filler and silence cleanup, captions, scene detection,
semantic search, the assistant, then reframing, audio cleanup, stems and ducking, beats,
interpolation, upscaling, privacy blur and automated QC. Ideas such as colour matching,
matting, TTS and podcast switching stay ideas until the earlier rungs ship and their
evaluation suite shows the models are good enough.

Model catalog, downloads and hardware fit are shared with Revelei, extracted into a
common library rather than copied.

## §S7 Interchange strategy: OTIO stays the truth

The .otio file is the document, not an export target. Three consequences:

- **Lossless by default.** Unknown schemas, unknown effects and every metadata key survive a save; a test over the OTIO corpus guards it (Block B criteria).
- **App state in two well-marked places.** State another tool should see (links, groups, effect parameters, captions) goes into `metadata["wintimeline"]`; state only this app needs (bins, proxies, analysis, layouts) goes into the project sidecar beside the file. A colleague opening the .otio in Resolve or Python sees a clean timeline.
- **Own effects are named, not hidden.** Effects are written as OTIO Effect objects named `wintimeline.<id>`, so other tools at least see that something is there.

Other formats arrive through adapters: EDL and FCPXML natively, the rest through an
optional Python bridge. Bundles (.otioz) are the archive and review-delivery format,
optionally carrying proxies instead of originals.

## §S8 Quality strategy and the spikes

Evidence, not appearance, closes a task:

- **Conformance**: OTIO behaviour is compared with the Python reference library over the same files in CI.
- **Frame truth**: generated fixtures with burnt-in frame counters and tones prove decode, trims, speed and export frame and sample accurate.
- **UI**: every screen ships with a winwright case run on the VMware guest, as in the sibling apps.
- **Speed**: the bench runs on three reference machines with budgets per release.
- **AI quality**: a labelled evaluation suite per feature records its metric; a regression fails the run.

Five spikes come before the code they decide, because each can change the architecture:
the OTIO implementation (WT11), the decode stack (WT23), the GPU surface host (WT4), the
encode stack (WT132) and the inference runtimes (WT99). Each ships with its measurements
and a decision record, so the reasoning outlives the spike.

## Block A — Project foundation

## Block B — OTIO document model and file formats

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


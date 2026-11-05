# Improvements

## Block A — Project foundation

### §WT9 Continuous integration

GitHub Actions on `windows-latest`: build, the headless test projects (Model, Media with
software decode, Render on WARP offscreen as Revelei's render tests do, Export with
software encode), the OTIO conformance job comparing with Python opentimelineio, and
`roadkeep lint`. GPU, hardware decode/encode and UI cases run on the VM and the
reference machine before release, not in CI. A nightly job checks the model catalog URLs
and hashes.

Done when a pull request shows the build, tests and lint as required checks.

### §WT10 Repository wiring for Claude sessions

The roadkeep guard hooks, no-clobber and the MCP server are already wired as in Revelei.
Two gaps remain. The project skill `wintimeline-roadmap-docs` was copied from
roadkeep-gui: it names prefix RG, a `roadkeep-gui` path and `npm run
typecheck/test/lint/build` as gates. It is rewritten for this repo: prefix WT,
`run-tests.cmd` and `run-tests-vm.cmd` as the gate, `roadkeep lint`, the build with
warnings as errors, and the one-task-one-commit rule kept as is. And
`.claude/settings.json` enables `winwright@winwright` with its marketplace, as Revelei
does, so UI cases can be authored with the plugin's help.

Done when the skill names only commands that exist in this repository and a session can
run a winwright case through the plugin.

### §WT153 Surface render thread at display refresh, and the host spike

Split out of WT4, which shipped `GpuSurface` (an `HwndHost` child window with a waitable
flip-model swap chain) and draws from `CompositionTarget.Rendering` on the UI thread.

Two things were not measured there:

- **The host itself.** A spike compares the `HwndHost` surface with `D3DImage` (through a
  D3D9Ex shared surface) and a composition swap chain (`DCompositionCreateSurfaceHandle`
  hosted in the WPF window), measuring input-to-photon latency, tearing, and what each
  costs in airspace and accessibility. The result is recorded and the losing paths' costs
  kept as the reason.
- **The frame rate.** A dedicated render thread per surface waits on the swap chain's
  `FrameLatencyWaitableObject` instead of riding `CompositionTarget.Rendering`, so playback
  reaches the display's refresh (120 Hz and above) without the UI thread in the loop. The
  surfaces must also survive a DPI change across monitors and docking moves without a
  black or stretched frame.

Done when the viewer and timeline surfaces sustain 120 Hz side by side on a 120 Hz
display (frame statistics from `IDXGISwapChain::GetFrameStatistics`), a DPI change
between monitors redraws both at the new size, and the spike's numbers are recorded. On
ship: --decides "the GPU surface is <chosen host>, because <measured reason>"

### §WT154 Native crash dumps through a launcher and createdump

Split out of WT7, which shipped the crash handling: managed exceptions logged with a
restart offer, a minidump (`MiniDump.Write`) for a terminating managed exception, and
the session marker that makes the next start show the recovery bar after any crash.

A crash in native code (a decoder, a driver, an inference engine) leaves no dump, and
every in-process route was measured to fail on .NET 10:

- an access violation in native code makes the runtime fail fast, which skips
  `AppDomain.UnhandledException` and any `SetUnhandledExceptionFilter` filter;
- Windows Error Reporting ignores `LocalDumps` under HKCU and honours only HKLM, which the
  per-user installer (WT143, `PrivilegesRequired=lowest`) cannot write;
- the runtime's own createdump works (`DOTNET_DbgEnableMiniDump=1`,
  `DOTNET_DbgMiniDumpType=1`, `DOTNET_DbgMiniDumpName=<crashes>\wintimeline-%p.dmp` wrote a
  25 MB dump of `--crash-test native`), but only when the variables exist before the
  process starts; setting them at start-up or in runtimeconfig.json does nothing.

So the installer's shortcuts and file associations start a small launcher that sets
those three variables, with the crash folder resolved from `WINTIMELINE_DATA` or
`LOCALAPPDATA`, and starts WinTimeline.exe with the same arguments. A run started any
other way (a case, a debugger) keeps today's behaviour.

Done when `--crash-test native` started through the launcher leaves a dump in the crash
folder, the next start's recovery bar names it, and the diagnostics bundle carries it.

### §WT155 The Basic Render Driver is software, and CI's results and actions

Found by the first GitHub Actions run (run 41748671407): on the Windows runner, which
has no GPU, DXGI lists the Microsoft Basic Render Driver (vendor 0x1414, device 0x8C)
without `DXGI_ADAPTER_FLAG_SOFTWARE`. `GpuAdapters` read only the flag, so it called
that adapter hardware: `AdapterChoice.Pick` chose it by default, the video-capable
device failed on it, and `GpuContext` fell back to WARP while the tests expected
hardware. A VM showing that driver would have done the same in the app.

`GpuAdapters.IsSoftware(flags, vendor, device)` now also treats vendor 0x1414 device
0x8C as software, so a desk with only it runs on WARP by choice and the hardware tests
skip with their reason. And `GpuContext` retries a hardware adapter without video
support before falling back to WARP, so a GPU that cannot decode still renders on
itself.

The same run warned that the results folder was empty (each test exe resolves
`--results-directory` against its own folder, so the workflow now passes an absolute
path) and that the actions run on the deprecated Node 20 (moved to checkout@v7,
setup-dotnet@v6, upload-artifact@v7 and setup-python@v7).

Done when the tests workflow passes on a GitHub runner.

## Block B — OTIO document model and file formats

### §WT159 Relinking by duration and content, and remembered rules

Split out of WT18, which shipped relinking by folder substitution and by file name
(`RelinkPlan` in `WinTimeline.Model.Media`, the `Relink` command, File > Relink media).

Two matches need to know what a file holds, which arrives with probing (WT24):

- **name plus duration**: a renamed copy (`A001_v2.mov` for `A001.mov`) is offered when its
  base name contains the original's and its duration equals the clip's available range;
- **content signature**: a file renamed beyond recognition is found by the same signature
  proxies use (WT46: size, a hash of the first and last MB, duration), compared against the
  signature the reference's metadata carries when it was probed on the first machine.

Both run after substitution and the name search, over the folders `RelinkPlan` already
indexes, and each candidate is listed for the person to confirm when it is not unique.
The substitution rules a person confirms are kept per project (WT22) and tried on open.

Done when a clip whose file was renamed and moved relinks by signature, and a project
reopened on the same machine relinks itself from its saved rule.

### §WT163 Comparison bands on the timeline

WT21's TimelineDiff gives each change the clips it concerns in both versions. Once the
timeline panel draws clips, it should tint each one by its change kind (moved, trimmed,
replaced, added), mark removed clips as thin ghosts where they were, and select the
matching list row when a band is clicked. The colours come from the Fluent theme's
semantic brushes, so they read in light and dark. Done when the cuts.otio pair shows
five bands on cut v2 and clicking the c2 band selects its Trimmed row.

### §WT164 Keeping the layout without a save

WT22 writes the sidecar on every save, so its layout and shown tab survive only when the
document was saved. Closing the window, or opening another file, should write the
sidecar's layout alone when the document has a path. This must not count as an edit, so
it neither dirties the document nor touches the journal. A read-only location should be
skipped silently, with a log line. Done when a file opened, switched to its third tab
and closed without saving reopens on that tab.

## Block C — Media probing and GPU decoding

### §WT23 Choosing the decode stack

A spike decodes a codec matrix (H.264 8-bit, HEVC Main10, AV1, VP9, ProRes 422/4444,
DNxHR HQX, MJPEG, PNG/EXR/DPX/TIFF sequences, XAVC, BRAW is out of scope) through:

- **Media Foundation** Source Reader with D3D11 device manager (DXVA, zero-copy NV12/P010 textures).
- **FFmpeg** (libavcodec, LGPL build, shared DLLs) with `hwaccel d3d11va`, and its software decoders for the pro intra codecs.

Measured: first-frame latency, random seek time to an arbitrary frame (the editor's real
workload), sustained decode fps at 4K, GPU/CPU load, and frame accuracy against a
reference frame dump. The expected outcome is a router: MF for long-GOP consumer codecs
with hardware support, FFmpeg for the rest, both behind one `IVideoDecoder` that yields
D3D11 textures.

Done when the matrix is filled with numbers and the routing table is chosen. On ship:
--decides "decode routing is <table>, because <measured reason>"

### §WT24 Media probing and the media info cache

A probe returns a `MediaInfo`: container, streams, codec, frame rate (exact rational,
VFR flag), duration in frames, start timecode (from tmcd track, SMPTE metadata or BWF
time reference), pixel format, bit depth, colour primaries/transfer/matrix/range, HDR
metadata, rotation, audio channels/layout/sample rate, and embedded markers. Results are
cached in a SQLite file keyed by path + size + mtime so reopening a project probes
nothing. Probes run on a bounded pool (default 4) with cancellation.

Variable frame rate media (phone footage) is flagged and offered for a constant-rate
proxy (Block E), since frame-accurate editing on VFR is unreliable.

A probe or decode failure is written to the app log (`Log.Write`, category Decode) with
the file's `MediaInfo` as fields, so Help > Create diagnostics bundle (WT6) carries it.

Done when a forced decode failure's diagnostics bundle holds the file's probe and the
error, and a folder of 500 mixed files probes in under 20 seconds cold and under 1
second warm, with values matching ffprobe.

### §WT25 Frame-accurate video decode into D3D11 textures

`IVideoDecoder.GetFrame(frameNumber)` returns a refcounted texture (NV12/P010 for
hardware paths, RGBA16F after conversion) on the shared device. Seeking goes to the
previous keyframe and decodes forward, discarding frames; a per-decoder GOP index (built
on first seek and cached) makes this predictable. Each decoder instance keeps a small
look-ahead queue for playback and is pooled per media file, with an LRU cap on open
decoders (default 16) so a 3,000-clip timeline does not exhaust handles or VRAM.

Timestamps are mapped to frame numbers with the exact rational rate; a test asserts
frame N's burnt-in counter equals N for every fixture codec.

Done when every codec in the matrix passes the burnt-in frame counter test at 100 random
seeks.

### §WT26 Image sequences and stills

`ImageSequenceReference` (target_url_base, name_prefix, frame_zero_padding, start_frame,
rate) maps to a decoder reading files through WIC (PNG, JPEG, TIFF, HEIF) and
OpenEXR/DPX readers (via OpenImageIO or FFmpeg, per the decode choice). Missing frames
in a sequence are reported and shown as a checkerboard. Stills are clips with a default
5 s duration and unlimited handles. Reads are parallel and prefetched along the playback
direction.

Done when a 4K EXR half-float sequence plays in real time from an NVMe drive on the
reference machine, or the measured limit is recorded.

### §WT27 Sample-accurate audio decode

`IAudioDecoder.Read(sampleStart, count)` returns planar float32 at the project's rate
(48 kHz default), resampled with a high-quality resampler (libswresample or the MF
resampler DSP) and channel-mapped per the clip's layout. Seeks are sample-accurate
(decode from the preceding packet and trim). WAV/BWF and AIFF read directly without a
codec. Multichannel sources (poly WAV from field recorders) expose each channel as a
selectable source channel.

Done when a 1 kHz tone fixture decoded at random positions matches the generated samples
within -90 dB.

### §WT28 Colour-correct conversion to the working space

A compute shader converts every decoded frame from its YUV format (matrix
BT.601/709/2020, range limited/full, chroma siting) to the working space: scene-linear
Rec.709 primaries in RGBA16F by default, or an ACEScg working space for projects that
ask for it. Transfer functions supported on input: sRGB, BT.1886, PQ, HLG, log curves of
common cameras (S-Log3, V-Log, LogC3/4, Canon Log 3) selected from metadata or per clip
by the user. The display transform is Block D's.

Done when SMPTE bars fixtures in each format convert to the expected linear values
within 1/1024.

### §WT29 Decode scheduling and GPU budgets

A scheduler sees the next N frames the playback engine will need across all active clips
and issues decode requests in deadline order to a pool of decode threads, keeping
hardware decode sessions within the GPU's limit (queried per adapter;
NVDEC/QuickSync/VCN session counts differ). When the deadline cannot be met, it reports
which clip is late so the engine can drop to proxies (Block E) instead of stuttering.

Done when a three-layer 4K H.264 test timeline plays without dropped frames on the
reference machine, and the late-clip report names the clip when a fourth layer is added.

### §WT30 Watching media for changes

A FileSystemWatcher per media folder (debounced) invalidates probe, decode, thumbnail
and proxy caches when a file changes, and shows a refresh badge on affected clips.
Growing files (being written by a recorder) are probed again on a timer and their
available range extends live.

Done when replacing a fixture file updates its frames in the viewer within two seconds.

## Block D — GPU compositing and playback engine

### §WT31 The frame render graph

For a given timeline time, an evaluator walks the Stack (top track over bottom, OTIO
order), resolves each track's item at that time (including transitions and nested
stacks, recursively), maps time through effects (LinearTimeWarp, FreezeFrame) to a
source frame, and emits a small render graph: decode nodes, per-clip effect chains,
transitions, and a composite. The graph is executed on the GPU in RGBA16F scene-linear
with premultiplied alpha.

The same evaluator runs for playback, scrubbing, thumbnails and export; export only
changes quality settings (no proxy, full resolution, high-quality scaling). Graphs are
cached by structure so consecutive frames reuse pipeline state.

Done when a fixture timeline's frames rendered here match reference frames rendered by a
CPU implementation of the same rules within 1/512 per channel.

### §WT32 Compositor: transforms, crop, opacity and blend modes

Each clip has a transform (position, scale, rotation, anchor, crop, flip), opacity and a
blend mode (normal, add, multiply, screen, overlay, soft light, difference, plus alpha
over), stored in an OTIO `Effect` named `wintimeline.Transform` with parameters in
metadata so other tools keep it. Scaling uses a high-quality Lanczos/bicubic compute
pass for export and bilinear for playback. Clips whose resolution or aspect differs from
the timeline follow a per-project conform policy (fit, fill, stretch, none). Everything
happens in HLSL compute shaders compiled at build time (DXC) with a shader cache.

Done when a fixture with every blend mode matches reference images.

### §WT33 Transition rendering

Transitions are shader nodes taking outgoing and incoming frames and a progress value:
cross dissolve (in linear light, with an option for gamma-space dissolve to match other
NLEs), dip to black/white, wipes (edge, clock, iris with softness), push and slide, and
a film dissolve. OTIO `SMPTE_Dissolve` maps to cross dissolve. Each transition exposes
parameters and an easing curve.

Done when a dissolve's middle frame equals the 50% linear mix of the two sources within
tolerance.

### §WT34 Playback engine, master clock and A/V sync

The audio device clock (WASAPI shared mode, Block I) is the master; the video presenter
schedules frames against it using the swap chain's frame statistics and
`IDXGIOutput::WaitForVBlank`-free presentation (waitable swap chain, flip model). A
render-ahead queue (default 6 frames) is filled by the graph executor; when the queue
empties the engine drops frames rather than slowing audio, and records each drop.
Playback rates: JKL shuttle -8x to 8x, with audio pitch-corrected scrubbing up to 2x and
muted beyond. Loop in/out and play around current (pre-roll/post-roll) are modes.

Timeline rate vs display refresh mismatch (23.976 on 60 Hz) uses even pulldown cadence;
displays with variable refresh are driven at a multiple of the timeline rate when
available.

Done when an A/V sync fixture (flash + beep every second) measured with a
photodiode-free loopback capture stays within ±1 ms of audio for ten minutes.

### §WT35 Rendered frame cache in VRAM and RAM

Composited frames are cached in a two-level LRU: VRAM (budget from the adapter's
`QueryVideoMemoryInfo`, default 25% of local budget) and system RAM (default 4 GB,
compressed with BC6H on the GPU). Keys are (graph hash of the frame, time, quality). Any
edit invalidates only the frames whose graph hash changed, so editing V2 does not drop
the cache of a section where V2 is empty. A thin bar under the ruler shows cached ranges
(green) like other NLEs. Background render fills the cache around the playhead when
idle.

Done when scrubbing a cached 10-second range shows every frame within one refresh, and
an edit invalidates exactly the expected range.

### §WT36 Viewer presentation and display colour

The viewer surface presents through a flip-model swap chain in R10G10B10A2 or FP16
scRGB. The display transform maps the working space to the monitor: sRGB/Rec.709 gamma
2.4 for SDR, and scRGB for HDR-capable outputs (detected through
`IDXGIOutput6::GetDesc1`), with tone mapping for SDR displays of HDR material. Overlays:
zoom (fit, 25-400%, pixel-accurate 100%), pan, safe action/title guides, aspect masks
(2.39, 1.85, 9:16, 4:5), centre cross, checkerboard behind alpha, and channel views (R,
G, B, A, luma).

Done when the viewer shows a reference chart identical to the exported file viewed in a
colour-managed player, on an SDR and an HDR display.

### §WT37 Playback quality levels

Playback resolution full, 1/2, 1/4, 1/8 applies at decode where the codec allows (FFmpeg
lowres for intra codecs, MF output size for hardware) and at the composite. An automatic
mode drops resolution one step when the dropped-frame counter rises and restores it when
idle, with a small indicator. Pausing always renders the current frame at full quality.

Done when auto mode keeps a heavy fixture at zero dropped frames on the reference
laptop.

### §WT38 Surviving device loss and adapter changes

All GPU resources are owned by a `GpuContext` that can be torn down and rebuilt. On
`DXGI_ERROR_DEVICE_REMOVED`/`RESET`, playback stops, caches are dropped, decoders
reopened, swap chains recreated, and the viewer shows 'Graphics device restored' instead
of a crash; the removal reason is logged. Moving the window to a monitor on another
adapter migrates to the adapter driving that monitor when the user enables it.

Done when a test that forces device removal (`ID3D12Device5::RemoveDevice` or D3D11
debug layer trigger) recovers playback without restarting.

### §WT39 GPU video scopes

Scopes are compute shaders reading the composited frame: luma waveform, RGB parade,
vectorscope (with skin tone line and 75%/100% targets), histogram, and a CIE
chromaticity plot for HDR. They run at playback rate on a downsampled frame and at full
resolution when paused. Scope panels dock like any other (Block G). Out-of-gamut and
out-of-range pixels can be highlighted in the viewer as zebras.

Done when scopes of SMPTE bars match the textbook traces.

## Block E — Proxies, thumbnails, waveforms and background jobs

### §WT40 The background job system

A `JobScheduler` runs typed jobs (Proxy, Thumbnails, Waveform, Analyse, Render,
Download) with priorities (interactive > visible > background), resource tokens (GPU
encoder sessions, decode sessions, CPU cores, network) and per-job progress, ETA,
cancel, pause and retry. Jobs persist in the project sidecar so an interrupted proxy
batch resumes after restart. When the user is playing back, background jobs throttle to
idle priority (`THREAD_MODE_BACKGROUND_BEGIN` plus GPU scheduling priority) so playback
never drops for a proxy. A Jobs panel and a taskbar progress indicator (ITaskbarList3)
show the state.

Done when 200 queued jobs run, pause, survive a restart and finish with correct
progress, and playback during them drops no frames.

### §WT41 GPU proxy generation

A proxy is a low-cost intra-friendly file per source: H.264 or HEVC all-I or short GOP
(GOP 1-4) at 1/2 or 1/4 resolution, encoded with the hardware encoder through Media
Foundation (NVENC, QuickSync, AMF via MFTs) or FFmpeg's
`h264_nvenc`/`h264_qsv`/`h264_amf`, with audio copied as PCM. Proxies keep the source's
frame count, rate and timecode exactly, and burn nothing in. Variable frame rate sources
get a constant-rate proxy at the nearest standard rate, flagged on the clip. Proxies are
written to a proxy folder (per project, or a global cache with a size budget and LRU
eviction) named by a hash of source path, size and mtime.

Presets: Fast (1/4 H.264), Balanced (1/2 HEVC), Quality (1/2 ProRes Proxy via FFmpeg),
Custom.

Done when a 10-minute 4K HEVC 10-bit file produces a frame-count-identical 1/2 proxy in
under 2 minutes on the reference machine with NVENC.

### §WT42 Proxy mode and per-clip resolution

OTIO's multiple media references are used as designed: each clip gets `media_references`
keys `DEFAULT_MEDIA` (original) and `PROXY` (written only into the sidecar unless the
user chooses to embed it), and `active_media_reference_key` stays DEFAULT in the saved
file. A global toggle (Ctrl+Shift+P) picks proxies for playback; per clip, the engine
falls back to the original when no proxy exists and to the proxy when the original is
offline. The scale difference is compensated so transforms and crops are identical in
both modes. Export always uses originals unless explicitly asked for a proxy-based draft
render.

Done when toggling proxy mode on a fixture shows the same framing and timing in both
modes, and an export from proxy mode uses originals.

### §WT43 Automatic proxy policy

On import, a policy decides: always, never, or automatic, where automatic creates
proxies for media whose measured decode cost (from the decode benchmark per codec and
resolution on this machine) exceeds the real-time budget, starting with clips placed in
the timeline. Low battery or metered network shares postpone proxy jobs. A storage
estimate is shown before a large batch.

Done when importing the codec matrix proxies only the heavy files on the reference
laptop.

### §WT44 Thumbnail generation and the thumbnail cache

Thumbnails are produced by the GPU (decode, colour convert, downscale in one pass) at
160 px height, at frame intervals that depend on zoom (a pyramid: every second, every 10
seconds, every minute), stored as BC7-compressed tiles in a cache file per media
(memory-mapped). Visible clips request first; poster frames (bin) use the frame at 10%
or a user-chosen one. The timeline renderer packs tiles into atlases on demand.

Done when a 3,000-clip timeline shows thumbnails for the visible area within one second
on reopen.

### §WT45 Waveform peaks and the waveform cache

Peaks (min, max, RMS) per channel are computed in levels of detail (256, 4,096 and
65,536 samples per point) and stored in a peak file per media, like `.pkf` files of
other editors. Computation streams the file once on a background job and is drawn
progressively. The renderer picks the level for the current zoom and draws with a GPU
line strip; at extreme zoom it draws samples from decoded audio.

Done when a two-hour WAV produces its peak file in under 15 seconds and the waveform is
drawn at any zoom without stalling.

### §WT46 Portable and shared proxies

Proxies are matched to sources by content signature (size + hash of first and last MB +
duration), not path, so a proxy folder copied with the project or placed on a share is
found. A project setting points at a shared proxy location; a .otioz review bundle can
carry proxies only: `OtioBundle.Write` (WT14) takes a resolver from target URL to local
file, so one mapping each source to its proxy writes that bundle; this line adds it to
Save As.

Done when a project moved to another drive letter with its proxy folder needs no
regeneration.

## Block F — Timeline view: GPU drawing and navigation

### §WT55 Clip appearance and badges

A video clip draws a filmstrip of thumbnails (head only, head+tail, or continuous,
chosen per track height), its name and source timecode; an audio clip draws its waveform
(peak or RMS, log or linear scale). Badges show speed changes (LinearTimeWarp %), freeze
frames, effects (fx), missing media (red hatching), proxy in use (P), and AI-generated
items. Clip colour follows OTIO `metadata` colour labels when present.

Done when every badge appears in a screenshot case against a fixture timeline that
exercises it.

### §WT58 Keyboard-only editing and UI Automation

Every timeline action has a keyboard path: focus moves between tracks (Ctrl+Up/Down) and
items (Tab within a track), edits act on the focused item, and the focus ring is drawn
on the GPU layer. The surface publishes a UI Automation tree (Track > Item) with names,
ranges as values and Invoke/Select patterns, so Narrator reads 'Clip interview_03,
00:01:12:04 to 00:01:20:00, video track 2' and winwright can locate items by name.

Done when a case trims and moves a clip using keys only, and Accessibility Insights
reports no errors on the panel.

### §WT167 Measuring the timeline on the reference laptop

WT47 drew the timeline with Direct2D from a viewport query, and its surface reports the
draw time (median and p95) in its UI Automation status. What no test here can say is
whether tests/fixtures/timeline/feature.otio, 24 tracks and 3,000 clips, scrolls and
zooms at the display's refresh rate on the reference laptop (Block N's budgets), because
WARP in the guest and this desk's discrete GPU are not that machine. A person opens the
cut there, scrolls and zooms for a minute, and reads the status: both numbers must stay
under the display's frame time. Thumbnail and waveform strips are WT55's and WPF track
headers are WT50's, so they are not part of this measurement.

### §WT168 Keeping the timeline's automation items cheap

WT47 made every clip in view a UI Automation item of the timeline surface, built afresh
on each query. At fit-all a feature-length cut shows about 500 clips, and WT48's case
measured one query of the surface's children taking 8 seconds in the guest, so a locator
for one clip times out and a screen reader stalls on the panel. The items should be kept
between queries and rebuilt only when the viewport or the layout changes, cheap to name
(bounds computed when asked, not upfront), and clips too narrow to point at (under 2 px)
grouped per lane into one item that names its range and count. Done when the 500-clip
fit-all view answers a ListItem query by name in under 200 ms on WARP.

### §WT169 Track heights, colours, drag order and targets

WT50 gave every lane a WPF header (TrackHeaders) beside the timeline surface: rename,
lock, mute or disable, solo, hide, add and delete, all undoable through TrackCommands
and TrackState, with the history's lock guard. Four things remain. Heights: each lane's
height follows its header (three presets from its context menu, a grip at its foot,
Alt+wheel over it), kept in the track's metadata as wintimeline:height, so
TimelineLayout and TimelineViewport take per-lane heights in place of one LaneHeight.
Colour: a wintimeline:color tints the lane's clips. Reordering: dragging a header moves
the track through TrackCommands.Move. Target: an insert/overwrite target toggle that
WT75's edits read, and that the playhead's Up and Down (WT51's EditPoints.Of, which
today takes every lane) narrow to. Done when a UI case drives each, and a resized lane
keeps its clips aligned with its header.

### §WT170 Edit points, linking and Select by

WT52 gave the timeline an item selection: the engine's Selection holds item ids (a
lane's label and the item's index in its track, as UI Automation names them). It changes
on a click, a Ctrl click (toggle) or a Shift click (the range from the last item
clicked, across the lanes between). A marquee drawn over empty lane space takes what it
touches. A and Shift+A take every item from the playhead on, or up to it. Ctrl+A takes
everything and Ctrl+Shift+A clears it. Each is outlined on the surface and read through
the surface items' selection pattern. Four things remain. Edit points: a press near a
clip's head or tail selects that edit point rather than the clip, which the trims of
WT72 act on. Linking shipped with WT79: a clip's link selects with it unless L turns
linked selection off. Edit > Select by: clips found by name, colour, media, effect or AI
tag. The inspector mirrors the selection once there is one. Done when each has a case
asserting the selected ids.

### §WT171 Dragging markers

WT54 shows the markers of the timeline's stack, its tracks and its items
(TimelineMarkers.Of in the engine places them in timeline seconds). It adds one with M
at the playhead and edits one with M on it, through EditMarker, which changes name,
colour, range and note in one undoable step. A marker cannot be moved yet. A press on a
marker should start a drag: on the ruler for the timeline's own, on the top band of its
lane for a track's or an item's. The drag moves the marker's start with WT53's
SnapTargets, excluding the marker itself, and the guide shows. Release is one EditMarker
with the new range in the owner's own time (for an item, the timeline time minus the
item's start plus its trimmed start). Kept inside what an item plays, since a marker
outside it is not shown. A ranged marker moves whole. Done when a case drags a marker
onto a cut and reads its new timecode, and an undo puts it back.

### §WT172 Markers from a Resolve export

WT54's done-when asks that markers from a Resolve-exported OTIO show at the same frames
and colours as in Resolve. The fixtures hold no Resolve export, so WT54 was checked
against what the reference adapters wrote: Premiere's FCP 7 XML
(otio/corpus/premiere_example.otio, markers on the stack and a clip) and FCPX
(fcpx/expected/fcpx_project.otio, clip markers past a trimmed start and one outside it).
MarkerTests asserts their placements and colours. What remains needs DaVinci Resolve: a
timeline with markers of several colours on the timeline and on clips, some ranged,
exported as OTIO, with a note of each marker's frame and colour as Resolve shows it.
That file goes under tests/fixtures/resolve/ with an engine test asserting
TimelineMarkers.Of against the note. Resolve writes its own colour names in the marker's
metadata (Resolve_OTIO), which may differ from the reference's enum. Where they disagree
the metadata colour should win, and that is the one code change this may bring.

### §WT173 Heat lanes for silence and AI suggestions

WT57 put heat lanes on the overview strip for cuts, validation problems and markers.
They are the engine's HeatLane, binned per four pixels of the strip, toggled from View >
Heat lanes, with a press within six pixels of a hot spot moving the playhead there. Two
of the lanes its design named have nothing to count yet. One is silence regions, which
needs the audio analysis that finds them. The other is AI suggestions not yet accepted,
which needs the assistant's proposals to be kept on the timeline rather than shown once.
Each joins as a HeatKind, with its times from where those blocks keep their results, a
View > Heat lanes item and a palette command. This is an idea until those blocks store
something a lane can count. Done when each new lane has a case that turns it on and
reads its count in the surface's status.

## Block G — Workspace, panels and viewers

### §WT59 Docking layout and workspaces

The main window hosts a docking manager (AvalonDock for WPF, themed to the Fluent
ThemeMode, or a lighter in-house layout if the spike finds AvalonDock clashes with the
GPU surfaces' airspace). Every panel is a registered `IPanel` with id, title, icon and
default dock position. Panels tab, split, float and move to another monitor; floating
windows hosting a GPU surface create their own swap chain. Workspaces (Edit, Review,
Audio, Colour, AI) are named layouts saved as JSON in user settings, switched from a
menu and Alt+Shift+1..5, with a reset to default.

Done when every panel can float to a second monitor and back with its GPU surface still
rendering, and workspaces restore after restart.

### §WT60 Program viewer and transport

The program viewer panel hosts the viewer surface with a transport bar: play/pause, JKL
indicator, frame step, go to in/out, loop, play around, timecode field (click to type a
target, +/- for relative), in/out marks, playback quality and proxy toggles, zoom menu
and overlays menu. Full-screen on any monitor (F) and a clean output to a secondary
display act as a client monitor. The timecode field accepts the same units as the ruler.

Done when every transport control has a case reading the resulting playhead frame.

### §WT61 Source viewer

Double-clicking a bin item or a timeline clip (match frame, F) opens it in the source
viewer, which has its own transport, in/out marks per media (remembered in the sidecar),
audio channel selection for the edit, and a drag handle that drags the marked range
(video, audio, or both) to the timeline or a bin as a subclip. A recent sources dropdown
switches between the last 20. Match frame from the source finds the frame in the
timeline.

Done when marking in/out, dragging to the timeline and match frame back are covered by
cases.

### §WT62 Media bins

The bin panel shows folders and items (media, subclips, sequences, generated items) in
list view (sortable columns: name, duration, rate, resolution, codec, timecode, date,
colour label, rating, AI tags) and grid view (hover-scrub thumbnails). Import via menu,
Ctrl+I, drop from Explorer (folders keep their structure), or watch folders. Smart bins
are saved searches (codec = HEVC and rating >= 3). Items have colour labels, ratings,
keywords and notes stored in the sidecar. Search is incremental over names, metadata,
transcripts and AI tags (Block L).

Done when importing a 500-file folder keeps the structure, and searching filters as the
user types under 50 ms.

### §WT63 Inspector with an OTIO metadata editor

The inspector shows the selection's properties in sections: Item (name, enabled, ranges
in the chosen unit, editable with validation), Media (references, active key, available
range, file info), Effects (stack, reorder, enable, parameters with keyframes from Block
J), Markers, and Metadata as an editable JSON tree (add key, edit value with type, copy
path). Multi-selection shows shared values and edits all. Every change is a command.

Done when editing a nested metadata value and saving writes exactly that change into the
.otio.

### §WT64 History panel

The history panel lists command descriptions with time and author (user, AI assistant,
script), clicking an entry moves the document to that state, and a branch is kept when
editing from an earlier state (redo tree, shown as a collapsible branch). AI-made edits
are grouped and labelled so they can be reverted as a unit.

Done when jumping back ten steps and forward again yields byte-equal documents.

### §WT65 Markers panel and review notes

A table of all markers (timeline, track, clip) with timecode, colour, name, comment,
duration and done state; filtering by colour or text; double-click jumps there. Bulk
change colour or delete. Export as CSV, EDL locators, SRT, or a PDF/HTML review report
with thumbnails; import from CSV and from Frame.io-style comment exports.

Done when an imported CSV of notes appears as markers at the right frames and exports
back identically.

### §WT67 Preferences window

A preferences window with searchable sections: General, Appearance (Fluent
light/dark/system, accent, timeline colours, UI scale), Playback (adapter, audio device,
buffer sizes, quality), Media and cache (locations, budgets, clean up), Proxies,
Autosave, Keymap (Block H editor), AI (Block K), Export defaults. Each setting shows its
default and a reset. Settings that need restart say so.

Done when every setting key in the schema appears in the window, verified by a test.

### §WT68 Welcome page, recent files and jump list

A welcome page lists recent projects with thumbnails, a big drop zone ('Drop an .otio, a
folder of clips or a video'), new project from presets (resolution, rate, colour space),
and sample timelines. Recent files also appear in the Windows jump list and Explorer's
'Open with'; .otio, .otioz and .otiod are associated on install with a thumbnail handler
(Block N).

Done when dropping a video on the welcome page creates a project and timeline with that
clip.

### §WT69 Notifications and the status bar

A non-modal notification area (Fluent InfoBar stack) shows job completions, warnings and
errors with actions (Show file, Retry, Open log). Background job completions when the
app is minimised use Windows toast notifications. The status bar shows the playhead
timecode, selection duration, dropped frames, GPU and VRAM usage, active proxies and AI
model state.

Done when a failing proxy job shows an InfoBar with a Retry that works.

## Block H — Editing: tools, trims and drag and drop

### §WT72 The four trims and the trim mode viewer

Hovering an edit point shows the trim cursor; dragging trims. Modifiers or the trim tool
(T) pick the type:

- **Trim** (default): changes one side, leaving a gap or overwriting.
- **Ripple** (Q/W style keys, or ripple tool): changes duration and shifts everything after.
- **Roll** (N): moves a cut between two clips, total duration unchanged.
- **Slip** (Y): changes a clip's source in point keeping its position.
- **Slide** (U): moves a clip between neighbours, adjusting them.

Trims respect available media range (handles) and show a red limit when exhausted.
During a trim the program viewer switches to a two-up view (outgoing last frame /
incoming first frame) with frame counters. Numeric trim: type +12 or -1:00 with an edit
point selected.

Done when each trim type has a case, and trims never produce overlapping items or source
ranges outside available range.

### §WT73 Drag and drop from bins and Explorer

The timeline accepts OLE drops of files (CF_HDROP) and of bin items (an in-process
format). A drop probes media off the UI thread, shows a placeholder clip with a spinner
immediately, then sets the real duration; overwrite/insert follow the same modifiers as
moving clips. Dropping several files lays them out in name or selection order; holding
Shift stacks them on successive tracks. A video file with audio creates linked video and
audio clips on the first free targeted tracks. Dropping a .otio, .edl or .xml file onto
the timeline offers to insert it as a nested compound. Dropping onto the source viewer
opens the file there for marking in/out.

Also supported outward: dragging a clip from the timeline to Explorer exports that range
as a file via the render queue (delayed rendering through IDataObject).

Done when cases drop files from an Explorer window and a bin and assert the created
clips and their ranges.

### §WT74 Three- and four-point editing

With in/out marked in the source viewer and an in point (or in/out) in the timeline, `,`
inserts and `.` overwrites onto targeted tracks; F11-style fit-to-fill creates a
LinearTimeWarp to fit four points; replace (F11) swaps the clip under the playhead
aligning on the playhead frame; place on top adds to the first free track above. Missing
points follow the standard rules (backtiming when only the timeline out is set). Track
targeting (source V1/A1 patched to timeline tracks) is shown in the track headers.

Done when every combination in a table of 3/4-point cases produces the expected item
range.

### §WT75 Lift, extract, ripple delete and gaps

Delete removes selected items leaving gaps; Shift+Delete ripple deletes; `;` lifts and
`'` extracts the in/out range on targeted tracks. Gaps are real OTIO `Gap` items and can
be selected and ripple-deleted; Close all gaps on a track is one command. A gap smaller
than two frames is flagged by validation as a likely mistake.

Done when lift and extract over ranges crossing clip boundaries leave exact durations,
verified against expected fixtures.

### §WT76 Editing transitions

Ctrl+D adds the default transition (cross dissolve, 1 s, centred) at the selected edit
points; transitions can be dragged from the effects panel onto a cut. Dragging a
transition's edges changes in_offset/out_offset within available handles; alignment can
be centred, start or end. `transition_type` keeps OTIO's SMPTE_Dissolve and custom
names; unknown types render as a dissolve with a warning badge but are preserved on
save.

Done when transitions read from corpus files render and re-save with identical offsets.

### §WT77 Speed, reverse and freeze frame

Ctrl+R opens a speed dialog (percentage or target duration, ripple option, reverse,
frame blending: nearest, blend, optical flow from the AI block). Speed is written as a
`LinearTimeWarp` with time_scalar; freeze frame as `FreezeFrame`. A rate-stretch tool
(R) changes speed by dragging an edge. Non-linear remaps from other tools arriving as
unknown effects are kept and shown as an fx badge.

Done when 50%, 200% and -100% clips render the expected source frames per output frame.

### §WT80 Keymap presets and the shortcut editor

All actions are registered commands with ids; a keymap maps chords to ids per context
(timeline, viewer, bin). Presets: WinTimeline default, Premiere-like, Resolve-like,
Avid-like. The shortcut editor shows a keyboard layout with conflicts highlighted and
search by action. JKL shuttle (with K+J/L frame stepping and repeated L for 2x/4x/8x) is
core. Keymaps export to JSON.

Done when each preset's documented core shortcuts are exercised by cases.

### §WT82 Sync locks across tracks

Each track has a sync lock toggle. A ripple on any track inserts or removes the same
duration on every sync-locked track at the edit frame, splitting clips if needed (or
inserting gaps). This mirrors Premiere's sync lock and Avid's sync behaviour, and is the
default for new tracks.

Done when ripple deleting on V1 with A1-A4 sync-locked leaves every pair in sync.

### §WT174 Cases for the keyed drags

WT70 made clips draggable on the timeline. The panel raises a MoveRequest on release,
picking the edit by the keys held: none overwrites, Ctrl inserts, Ctrl+Alt swaps with
the clip dropped on, and Alt held at the press duplicates. MainWindow.Move carries it
out through the model's TrackEdits (lift, overwrite, insert, extract) as one history
transaction. TrackEditTests proves each edit on cut_v1, and the overwrite drag has a UI
case ("a clip dragged along its track overwrites what it lands on"). The insert, swap
and duplicate drags do not, because a winwright drag cannot hold a key (WW604 in
winwright's roadmap). When it can, three cases join the move case's fixture. Each drags
c3 from 52.9% to 88.2% across on V1's lane (y 48) with Ctrl, Ctrl+Alt (onto c5) and Alt
held. Each reads the status line and the clips' names and spans against the shapes
TrackEditTests asserts, then undoes. Done when the three cases pass in the guest.

### §WT175 Multicam grid, live cutting and audio sync

WT81 made multicam clips: Edit > Create multicam turns the selected clips into angles,
lined up by timecode or by in points (Multicam.Create in the model). Each is a plain
nested stack, one track per angle, with only the shown angle enabled and its index under
wintimeline:multicam-angle. The number keys and Edit > Cut to angle split it at the
playhead and show another angle from there (Multicam.CutTo), each one undo step. Three
things remain, and each waits on a block that is not built. A grid of the angles in the
source viewer (2x2, 3x3) needs the source viewer (WT61). Cutting live during playback,
the number keys writing cuts as the program plays, needs the program viewer and
transport (WT60). Syncing angles by audio waveform cross-correlation needs decoded
audio, refined later by the AI block. The design's done-when stands: a three-angle
fixture synced by audio cuts live and plays back the chosen angles.

## Block I — Audio: playback, mixing and meters

### §WT83 WASAPI output and the audio engine

An audio engine runs on an MMCSS 'Pro Audio' thread with WASAPI shared mode
(event-driven, 10 ms period; exclusive mode optional), mixing the timeline's audio at 48
kHz float32. Device selection, default-device following, and hot-plug (headphones
unplugged) are handled without stopping the app. The engine reports its device clock
position, which the playback engine uses as master (Block D).

Done when unplugging and replugging the output device during playback resumes sound
within one second, and latency is reported in the status bar.

### §WT84 Clip gain, track mixer and master bus

Each audio clip has gain (dB) and pan; each track has a fader, pan, mute, solo and a
meter; a master bus has a fader and a limiter. The mixer panel shows channel strips like
a console. Volume and pan are keyframable (rubber bands drawn on the clip in the
timeline, Block J's keyframe model). Track layouts: mono, stereo, 5.1 with a downmix for
stereo monitoring. Values live in OTIO effects named `wintimeline.AudioGain` and in
track metadata.

Done when a fixture mix exported to WAV matches the expected sample values within 0.01
dB.

### §WT85 Meters and loudness (EBU R128 / ITU-R BS.1770)

Peak and true-peak meters per track and master, and a loudness panel with momentary,
short-term and integrated LUFS plus loudness range, following BS.1770-4. Targets: -14
LUFS (streaming), -16 (podcast), -23 (EBU R128), -24 (ATSC A/85). A full-program
loudness analysis runs as a background job and offers a normalise-to-target command on
the master.

Done when the EBU R128 test signals measure within ±0.1 LU.

### §WT86 Audio scrubbing and JKL audio

Dragging the playhead plays short grains (about 40 ms, windowed) at the scrub position
and speed; JKL at 2x-4x plays pitch-corrected audio (WSOLA time stretching); frame
stepping plays the frame's audio slice. Scrub audio can be toggled (Shift+S).

Done when scrubbing a fixture word plays it intelligibly at half and double speed.

### §WT87 Fades, crossfades and click-free edits

Fade handles on each audio clip corner (drag to set length, curve: linear, equal power,
S-curve); audio transitions as OTIO Transitions on audio tracks (equal-power crossfade
default). Every edit point gets an automatic 2 ms micro-fade unless disabled, so cuts
never click.

Done when a cut in a sine fixture exported shows no discontinuity above -60 dB.

### §WT88 Channel mapping per clip

A clip's channel map chooses which source channels feed which clip channels (mono from
ch 2, stereo from 1+2, split a stereo pair into two mono clips). Defaults per media can
be set in the bin and apply on edit. Channel names from iXML/BWF are shown.

Done when an 8-channel poly WAV fixture edits as chosen channels and exports the right
signals.

### §WT89 Voice-over recording

A record-arm button on audio track headers; recording captures from a WASAPI input with
pre-roll, writes a WAV into the project media folder and places the clip at the record
start, compensating input latency. Multiple takes stack as a nested take list.

Done when a recorded take lines up with the playhead frame it started on within one
millisecond.

## Block J — Effects, titles, keyframes and colour

### §WT90 The effect framework

An effect is a registered `IEffect` (id, version, parameter schema, HLSL compute shader
or CPU fallback) referenced from an OTIO `Effect` whose `effect_name` is
`wintimeline.<id>` and whose parameters live in `metadata["wintimeline"]["params"]`.
Unknown OTIO effects are kept and shown greyed with a warning. Effects stack per clip,
per track (adjustment) and on the master; they can be dragged from the effects panel
(searchable, categories, favourites, preview on hover) onto clips, tracks or a
selection. Parameters render in the inspector from the schema (slider, angle, colour,
point picked on the viewer, curve, enum, checkbox).

Done when every built-in effect saves to OTIO, reloads with identical parameters and
renders the same frame.

### §WT91 Keyframes and curves

Any numeric, colour or point parameter can be keyframed. Keyframes are stored in
clip-relative frames (so moving a clip moves its animation) with interpolation linear,
hold, bezier (with handles) and ease presets. Keyframes appear as a lane under the clip
in the timeline (expandable) and in a curve editor panel with zoom, multi-select, box
scale and snapping to frames. Copy and paste keyframes between parameters and clips.

Done when an animated fixture renders each frame equal to the evaluated curve within
1e-4.

### §WT92 The built-in effects set

First set, all GPU compute: Gaussian and directional blur, unsharp mask, crop with
feather, vignette, drop shadow, mirror/flip, letterbox, mosaic/blur region (shape mask),
chroma and luma key (with spill suppression and a matte view), shape masks (rectangle,
ellipse, bezier) with feather and tracking hook, film grain, glow, and a LUT applicator
(.cube 1D/3D with tetrahedral interpolation). Stabilisation is in the AI block. Each
effect has fixtures and reference images.

Done when each effect has a reference-image test and a winwright case applying it from
the panel.

### §WT93 Primary colour correction

A colour panel (Colour workspace) with: exposure and white balance (temperature/tint in
linear light, eyedropper on a neutral), lift/gamma/gain wheels with offset, contrast
with pivot, saturation and vibrance, RGB and hue-vs curves, and a before/after split in
the viewer. Grades are effects (`wintimeline.Primary`) so they travel in OTIO. Grade
copy/paste and a gallery of saved stills to compare. The working space conversion from
Block C guarantees the maths happen in linear light.

Done when an eyedropper on a grey card fixture neutralises it to equal RGB within 0.5%.

### §WT94 OpenColorIO configuration support

OCIO v2 (native library) loads a config (the built-in ACES studio config or a file);
input colour spaces per clip map from metadata or file rules; the working space and
display/view transforms come from the config. OCIO's GPU shader generator produces
HLSL-compatible code (via its shader text plus a 3D LUT fallback) inserted into the
render graph. The project setting chooses between the app's built-in pipeline and OCIO.

Done when frames of the ACES test images displayed through the ACES config match
ocioconvert output within tolerance.

### §WT95 Titles and text generators

Titles are clips with a `GeneratorReference` (`generator_kind` = `wintimeline.Text`)
whose parameters hold rich text (DirectWrite layout: font, size, weight, tracking,
leading, colour, stroke, shadow, background box), alignment and safe-area-aware
positioning. Edited directly on the viewer (click to type) and in the inspector.
Templates: lower third, centred title, rolling credits (with speed from duration), and a
subtitle style shared with captions. Text renders through Direct2D into a texture at
output resolution.

Done when a title round-trips through OTIO and renders identically, and an unknown
generator from another tool shows as a placeholder.

### §WT96 Generators

Generators as `GeneratorReference` clips: solid colour, linear/radial gradient, SMPTE
and EBU bars with 1 kHz tone, countdown leader, checkerboard, noise, and a placeholder
card with the missing shot's name. Known OTIO generator kinds from other tools
(SolidColor, SMPTEBars) map to these.

Done when bars and tone export with the correct values and frequency.

### §WT97 Captions track

A captions track kind (stored as an OTIO Track with kind `wintimeline.Captions` and
clips whose generator holds text) imports SRT, WebVTT and SCC; caption items are edited
in the timeline (trim, split, merge) and in a captions panel (list with text editing and
reading speed warnings: characters per second and line length). Styling per track.
Export as SRT/VTT sidecar, burn-in, or embedded in MP4 (mov_text) per Block M.

Done when an SRT imports, edits and exports byte-identical for unchanged items.

### §WT98 Shader effect plugins

A plugin is a folder with a manifest (id, parameters schema, category) and an HLSL
compute shader compiled at load by DXC with a fixed binding layout; no native code runs.
Plugins load from %APPDATA%\WinTimeline\effects and appear in the effects panel. OFX
support is a non-goal for now.

Done when a sample plugin folder adds a working effect without restarting the app.

## Block K — Local AI platform: models, runtimes and hardware fit

### §WT100 The model catalog

A signed JSON catalog (shipped in the app, refreshable from the project's GitHub
releases) lists each model: id, feature, variants (quantisations, sizes), files with
URLs (Hugging Face resolve URLs pinned to a commit), SHA-256, size, licence (SPDX id and
a 'commercial use' flag), minimum VRAM and RAM per variant, supported providers, and a
measured speed reference. The catalog is versioned; an older app ignores entries whose
schema it does not know.

Done when the catalog validates against its JSON schema in CI and every URL and hash in
it is checked by a nightly job.

### §WT101 Model download manager

The download stack is Revelei's (`ModelDownloads`, `DownloadQueue`, `ModelLibrary`,
`ModelsFolderMove`), extracted into a shared library rather than copied. Downloads
become Download jobs (Block E): `.part` files resumed with HTTP Range, SHA-256 while
streaming, deduplication by hash, parallel segments for large files, bandwidth limit,
and pause on metered connections. Files are renamed into the model store only after the
hash matches (default %LOCALAPPDATA%\WinTimeline\models, changeable to any drive; moving
the store is a job). A model is 'installed' only after hashes match. Removing a model
frees its files after confirmation, showing the features that will stop working.

An optional mirror setting and an offline import ('install from a folder or zip') cover
machines without internet access.

Done when killing the app mid-download resumes from the same byte, and a corrupted file
is detected and re-downloaded.

### §WT102 AI models page

A page in preferences (and a panel in the AI workspace) lists features (Transcribe,
Scene detection, Search, Assistant, Denoise, Upscale, ...) with their model, a hardware
verdict (runs well / runs slowly / will not fit) computed from the adapter's VRAM,
system RAM and the catalog, licence summary, size, and Install / Remove / Test speed
buttons. Test speed runs a short benchmark and stores the measured time, replacing the
estimate. Each feature that needs a missing model shows an inline 'Install model (1.2
GB)' prompt where it is used, never a dead button.

Done when every catalog feature appears with a verdict on the reference machine and the
VM, and the install prompt is covered by a case.

### §WT103 Local-only guarantee and audit log

AI features never use the network except for model downloads from catalog URLs. The HTTP
client used by the app is a single wrapper that only allows catalog hosts and the update
feed; any other destination is refused and logged. An 'AI activity' log lists each
inference (feature, model, media, duration) so a user can see what ran. A setting
disables all downloads for air-gapped use.

Done when a test that tries to reach a non-catalog host through the client is refused,
and the activity log records each feature run in the cases.

### §WT105 Sharing the GPU between AI and playback

Inference runs on a separate D3D12 command queue with lower priority
(`D3D12_COMMAND_QUEUE_PRIORITY_NORMAL` vs HIGH for presentation), in small batches that
yield between runs while playback is active, and pauses when the dropped-frame counter
rises. On machines with an NPU, analysis models that support it run there by default. A
per-feature setting forces CPU.

Done when transcription runs during playback of the stress timeline with zero dropped
frames on the reference machine.

### §WT166 Native engines out of process

WT99 wired Windows ML's ONNX Runtime in and put every session under one SessionCache
with an LRU VRAM budget (WinTimeline.Inference). The language model and, where it
measures faster than an ONNX Whisper, transcription run on native engines instead:
llama.cpp and whisper.cpp, each in a host exe that speaks a line protocol on stdin and
stdout, as Revelei.Generation.Host does for stable-diffusion.cpp. A native crash or a
cancel (killing the process tree) then never takes the editor down. The host session
implements IDisposable, so the same SessionCache loads, counts and unloads it beside the
ONNX sessions, and the engines follow the app's adapter. This waits on WT100's catalog
to download the engines and a small GGUF, since models are never committed. Done when a
test runs a small GGUF in its host beside an ONNX vision model through one cache,
unloads both, and GpuContext.VideoMemory shows the VRAM freed; the ship records with
--decides why llama.cpp was chosen over an ONNX language model, with the measurement.

## Block L — AI-assisted editing

### §WT106 Transcription with word timestamps

Whisper (large-v3-turbo as default, distil and small variants for weak machines) runs
through ONNX Runtime or whisper.cpp, chosen by measurement, on 16 kHz mono extracted
from the clip's chosen channels. Voice activity detection (Silero VAD) skips silence and
splits long files; word timestamps come from cross-attention alignment and are refined
by a forced-alignment pass. Language is detected or chosen per media. Results are stored
per media with model version (Block K) and shown in a Transcript panel synced to the
source viewer: clicking a word seeks there, the current word highlights during playback.

Done when a one-hour interview fixture transcribes on the reference machine in under 6
minutes with word times within 100 ms of a hand-aligned reference on 95% of words.

### §WT107 Speaker diarisation

A speaker embedding model (ECAPA/WeSpeaker ONNX) clusters voice segments into speakers
per media, and across media in a project so 'Speaker 1' in two files is the same person
when voices match. Speakers can be renamed once and the name propagates. Overlapping
speech is marked.

Done when a two-person podcast fixture is diarised with under 10% diarisation error
rate.

### §WT108 Text-based editing

The Transcript panel in timeline mode shows the transcript of the timeline itself
(concatenated from its clips' transcripts mapped through their source ranges). Deleting
words extracts their time range from the timeline (with a short configurable padding and
micro-fades); dragging a paragraph reorders the corresponding clips; selecting text and
pressing Insert edits that source range into the timeline at the cursor. Each text edit
is an ordinary command, undoable, and the timeline updates live. Edits snap to word
boundaries and to silence between words when possible.

Done when deleting a sentence in the transcript removes exactly its audio range,
verified against word timings, and undo restores it.

### §WT109 Filler word, silence and retake removal

A cleanup dialog finds: filler words (um, uh, like, you know, and per-language lists,
plus a model-flagged list), silences longer than a threshold (from VAD and loudness),
and retakes (consecutive sentences with high text similarity, keeping the last). Results
are shown as a reviewable list and as highlighted ranges in the timeline; the user
accepts all, some, or none, and the cut is applied as one undoable group labelled 'AI
cleanup'. Padding and crossfade lengths are configurable.

Done when a fixture with known fillers and pauses is cleaned to within one word of the
expected cut.

### §WT110 Automatic captions

Generate captions from the timeline transcript into a captions track, with line breaking
rules (max characters per line, max lines, min/max duration, reading speed, break at
punctuation and speaker change), styles (standard, social word-by-word highlight,
karaoke), and speaker labels. Editing caption text updates nothing in the media;
re-generation keeps manual edits unless asked. Export via Block M.

Done when generated captions for a fixture pass the reading-speed validator with no
warnings at default settings.

### §WT111 Shot and scene detection

TransNetV2 (ONNX) on downscaled frames detects hard cuts and gradual transitions; a
colour histogram pass refines boundaries to the exact frame. Results become markers,
subclips in the bin, or razor cuts on a timeline clip (scene edit detection, like
Resolve's), chosen by the user. A sensitivity slider and a review list with thumbnails
either side of each cut let the user drop false positives before applying.

Done when a fixture master with known cuts is split with precision and recall above 0.95
and every cut on the exact frame.

### §WT112 Visual tagging and logging

Per shot, sampled frames go through: an object detector (YOLO-family, permissive licence
variant), a shot-size classifier (extreme wide to extreme close-up), camera motion
estimation from optical flow (static, pan, tilt, zoom, handheld), face detection and
clustering (people grouped across clips, named once), and quality scores (blur,
exposure, noise). Tags are written to the bin item and are searchable and usable in
smart bins.

Done when tags on a labelled fixture set reach the agreed precision per category,
recorded in the section's test notes.

### §WT113 Semantic search over pictures and words

Image embeddings (SigLIP or CLIP, ONNX) are computed per shot keyframe and stored in a
vector index (HNSW, in the sidecar); text queries are embedded with the paired text
encoder. The search box in bins and the command palette (`?`) combines vector
similarity, transcript full-text search (FTS5) and tags, returning shots with a
thumbnail, the matching moment, and why it matched. Results drag straight into the
timeline as the matched range. 'Find similar' on any frame searches by image.

Done when a set of 20 described queries over a fixture library returns the expected shot
in the top five for at least 17.

### §WT114 The local editing assistant

A small instruct LLM (Qwen2.5/Qwen3 7B or Phi-4-mini class, GGUF Q4, chosen by
measurement for tool calling quality) runs in llama.cpp with grammar-constrained JSON
output. It sees a compact text view of the timeline (tracks, clips with names, ranges,
transcripts excerpts, markers, tags; never pixels unless a vision model is installed)
and a tool schema that mirrors the command pipeline: select, split, trim, move, delete
ranges, add markers, add transitions, set speed, add captions, search footage, create
sequence.

It replies with a plan and a batch of commands; the app shows the plan and a preview
(the diff overlay from the timeline comparison), and nothing changes until the user
clicks Apply. Applied batches are one undo group tagged as AI. Examples it must handle:
'remove all pauses longer than a second', 'put a marker at every mention of the
product', 'make a 30-second version for Instagram keeping the strongest quotes'.

Done when a scripted set of 30 requests on fixtures produces valid command batches for
at least 25, and invalid outputs never reach the document.

### §WT115 Local translation of captions

A translation model (NLLB-200 distilled via ONNX, or the local LLM for small jobs)
translates caption tracks into a new caption track per language, keeping timings and
re-breaking lines for the target language's reading speed. Glossary terms (names,
brands) are protected.

Done when a fixture caption track translates to Portuguese and Spanish with all timings
preserved and no line over the limits.

### §WT116 Rough cut from a script or brief

Given a script (pasted text or a .docx/.txt), the app aligns each line to the best
matching spoken take across the bin (transcript similarity, then take quality: fewer
fillers, no stumbles, best audio level), and builds a timeline in script order with
alternatives stored as clip alternates (OTIO multiple media references or a take list in
metadata). Given a brief instead ('a 2-minute story of the founder's journey'), the
assistant picks quotes and orders them, showing its reasoning per selection. B-roll
suggestions come from the next task.

Done when a fixture script assembles with every line matched to the expected take.

### §WT117 B-roll suggestions from the transcript

For a selected range of the timeline transcript, nouns and phrases are extracted and
searched against image embeddings and tags; suggestions appear in a side list with
thumbnails and drag to the track above the interview, cut to the phrase's duration.
Shots already used are deprioritised.

Done when suggestions for a fixture interview include the expected B-roll for most
marked phrases.

### §WT118 Automatic reframing for vertical and square

For a target aspect, each shot gets a subject track (face or person detection, falling
back to saliency), smoothed with a critically damped filter and shot-boundary resets,
written as keyframes of the clip's transform so the user can adjust them. Modes: follow
subject, still crop (best fixed crop per shot), split screen for two speakers. A new
timeline version is created ('Instagram 9:16') and linked to the original so later edits
can be propagated.

Done when reframing a fixture interview keeps the face inside the safe area on 98% of
frames.

### §WT119 Dialogue enhancement

An audio effect `wintimeline.Voice` runs DeepFilterNet (ONNX, real time on CPU) for
noise reduction with a strength control, plus a hum remover (50/60 Hz harmonics notch)
and a de-reverb option where a suitable model is installed. It renders live during
playback with a bypass toggle, and is cached per clip as rendered audio for export
speed.

Done when a noisy fixture improves its measured SNR by at least 15 dB without clipping.

### §WT120 Stem separation and auto-ducking

Demucs (htdemucs, ONNX) separates a clip into vocals, drums, bass and other, as a
background job producing linked audio clips. Auto-ducking reads speech regions
(VAD/transcript) on dialogue tracks and writes volume keyframes on music tracks (depth,
attack, release), editable afterwards.

Done when auto-ducking a fixture lowers music by the chosen depth during every speech
region.

### §WT122 Frame interpolation for speed changes

RIFE (ONNX, DirectML) generates intermediate frames for clips with time_scalar below 1
when frame blending is set to 'optical flow'. Generated frames are cached on disk per
clip and speed, produced by a background job, and used in playback once ready (blend
frames shown before that). Also offered for rate conversion (25 to 50 fps) at export.

Done when a 25% speed fixture plays with no repeated frames once processed.

### §WT123 Video upscaling and restoration

A clip-level 'Enhance' job upscales with a video-suitable Real-ESRGAN variant (or a
temporal model where licence allows) and optional face restoration, producing a cached
intermediate file used in place of the source (an extra media reference key `ENHANCED`).
The models and tiling strategy follow Revelei's proven implementation so both apps share
the catalog entries.

Done when an SD fixture upscaled to HD measures a better LPIPS than bicubic and plays
from the cache in real time.

### §WT124 Background removal and subject masks

Robust Video Matting (temporally stable, ONNX) or BiRefNet per frame produces an alpha
matte, available as an effect (remove background, blur background, replace) and as a
mask source for other effects. Mattes are cached per clip.

Done when a fixture with a person over an office background is matted with stable edges
across frames.

### §WT125 Automatic face and plate blur

Faces (and plates, with a plate detector) are detected and tracked through a clip; each
track becomes a blur region effect with keyframed position, and the user can exclude
chosen people (from face clusters) before applying. Tracks can be corrected frame by
frame.

Done when every bystander face in a fixture is covered on every frame after applying,
verified against annotations.

### §WT126 Automated QC of the cut

A QC run over the rendered timeline flags: flash frames (clips shorter than 3 frames),
black and frozen frames, audio gaps and clipping, loudness out of target, out-of-focus
or heavily shaken shots, unintended jump cuts (same shot, small gap), and missing
captions in speech. Findings go to the problems panel and as markers, each with a jump
and, where possible, a fix.

Done when a fixture seeded with each defect is reported with one finding per defect and
no false alarm on a clean fixture.

### §WT127 Chapters, descriptions and change summaries

The assistant drafts YouTube-style chapters (timecodes from markers or topic shifts in
the transcript), a description and tags, and turns the deterministic timeline diff into
a readable change note ('the opening was shortened by 4 s; the second interview answer
moved before the product demo'). Output is editable text, copyable, and exportable with
the render.

Done when a fixture change note mentions every change in the deterministic diff.

### §WT128 Automatic shot colour matching

Given a reference shot, other shots get a primary grade (white balance, exposure,
saturation, and a 3x3 matrix) computed from colour statistics of matched regions (faces
and neutrals preferred), written as an ordinary editable Primary effect.

Done when two fixture cameras filming one chart match within 2 delta E after the
automatic grade.

### §WT129 Automatic multicam switching by speaker

Each speaker is assigned an angle; the cut follows speaker turns with rules (minimum
shot length, wide shot on overlap or long pauses, occasional reaction shots). The result
is an ordinary multicam cut the editor can change.

Done when a three-angle fixture podcast is cut with the active speaker on screen during
at least 90% of speech.

### §WT130 Scratch narration from text

A local TTS model (Piper or Kokoro, permissive licences) turns text into a WAV clip
placed at the playhead, marked as 'scratch' so QC warns if it reaches an export. Voice
and speed selectable.

Done when a paragraph becomes a clip of the expected duration and the QC warns on
export.

### §WT131 Automatic audio sync

For a set of clips, cross-correlation of envelope features (and of decoded audio at full
rate for refinement) finds offsets between camera audio and recorder audio, including
drift estimation over long takes. Results create synced linked clips or multicam clips
and report confidence per pair.

Done when fixture pairs sync within one audio sample at 48 kHz, and a low-confidence
pair is flagged.

### §WT176 Neural beat and downbeat tracking

WT121 finds beats by onset detection (Engine/Analysis/Beats.cs): the decoded sound's
energy in one-millisecond frames, an onset where it climbs past a threshold, each
refined to the first sample past a fifth of its peak. That is exact on struck material:
the click-track fixture's beats land within 10 ms and its tempo reads 120 BPM. It marks
onsets, not beats, on music whose pulse is not struck (pads, strings, swung rhythms),
and it knows nothing of bars. The design's neural tracker closes that gap: a beat and
downbeat model (madmom-style RNN or BeatNet) in ONNX, listed in the model catalog
(Inference/Catalog/models.json) and fetched through GuardedHttp like the others, run on
the same decoded sound. Its downbeats become markers named "bar", and Cut to beats'
"every fourth beat" becomes "every bar". The onset detector stays as the fallback where
no model is installed. Done when a fixture of real music with known beat and bar times
scores an F-measure of 0.9 or better at 70 ms tolerance.

## Block M — Video export and interchange

### §WT132 Choosing the encode stack

Measured on NVIDIA (NVENC), Intel (QuickSync) and AMD (AMF) machines and the software
fallback: Media Foundation sink writer with hardware MFTs, and FFmpeg (`h264_nvenc`,
`hevc_qsv`, `av1_amf`, `libx264`/`libx265`/`libsvtav1`, `prores_ks`, `dnxhd`). Criteria:
speed, quality at equal bitrate (VMAF), 10-bit and HDR metadata support, B-frames and
lookahead control, zero-copy from D3D11 textures, and licensing of each component in the
shipped build (LGPL FFmpeg build without GPL encoders unless downloaded separately, like
a model).

Done when the matrix is filled and the routing per codec is chosen. On ship: --decides
"encode routing is <table>, because <measured reason>"

### §WT133 Rendering a timeline to a video file

The exporter drives the render graph at full quality, frame by frame in order (no
real-time constraint, originals not proxies), converts the working space to the output
colour space and pixel format on the GPU (BT.709 limited range NV12/P010 by default,
with correct VUI tags), and feeds the encoder from D3D11 textures without CPU copies
where the encoder allows. Audio is mixed offline by the same mixer at the output rate
and encoded (AAC, Opus, PCM). Containers: MP4, MOV, MKV, WebM. Range: whole timeline,
in/out, or selected clips. Progress, ETA, pause and cancel are part of the job (Block
E); the app stays usable during export.

Done when a fixture timeline exports to MP4 H.264 whose decoded frames match the
viewer's frames (PSNR > 45 dB) and whose audio aligns within one sample.

### §WT134 Export dialog and presets

The export dialog has a preset list (YouTube 1080p/4K, Instagram/TikTok 9:16,
X/LinkedIn, Vimeo high quality, ProRes 422 HQ master, DNxHR HQX master, H.264 review
copy with burnt-in timecode, audio only WAV/MP3, image sequence) and an advanced section
(codec, profile, rate control CBR/VBR/CQ, bitrate or quality, GOP, B-frames, resolution
and scaling, frame rate conversion, colour space and HDR metadata, audio codec and
channels, captions mode). Estimated file size and time are shown. User presets save to
JSON and can be shared.

Done when every built-in preset exports a fixture that passes a probe check of its
declared parameters.

### §WT135 Render queue

Exports are added to a render queue panel (timeline + range + preset + destination with
name tokens like {timeline}_{preset}_{date}), reordered, paused, retried; the queue
persists in the project and survives restart. Options: shut down or sleep when done,
toast notification, open folder. Parallel exports share encoder sessions per the GPU's
limit.

Done when a queue of five exports finishes after a restart in the middle.

### §WT136 HDR export

HEVC Main10 and AV1 10-bit export with PQ (HDR10: mastering display metadata and
MaxCLL/MaxFALL computed during render) or HLG; an SDR version of an HDR timeline uses
the same tone mapping as the viewer. Validation warns when the preset and timeline
colour space disagree.

Done when an HDR10 export is recognised as HDR by Windows' Media Player and its metadata
reads correctly in ffprobe.

### §WT137 Smart render and pass-through

When the output codec and parameters match the source and a segment has no effects,
transforms or transitions, whole GOPs are copied without re-encoding, and only segments
around cuts are re-encoded (GOP-boundary handling, matching SPS/PPS). Also a fast
lossless trim for single-clip exports.

Done when an effect-free fixture exports at least five times faster with byte-identical
copied GOPs.

### §WT138 Interchange export and conform packages

File > Export timeline writes .otio (choosing the schema version family), .otioz with
media or proxies, EDL CMX 3600 (per track, with reel names from metadata), FCPXML and
FCP7 XML, and an AAF through the adapter bridge. A conform package option renders each
used clip range with handles to ProRes or DNxHR files plus a timeline that references
them (consolidate/trim project), so a colourist receives only what is used.

Done when an exported FCPXML and EDL of a fixture re-import here with identical clip
ranges, and in Resolve with a documented check.

### §WT139 Stills, contact sheets and animated previews

Export frame at playhead (PNG, JPEG, TIFF 16-bit, EXR) with Ctrl+Shift+E; a contact
sheet of one frame per shot or per marker (PDF/PNG with timecodes and names); animated
WebP or GIF of the in/out range with palette optimisation. The AI block can pick the
most 'thumbnail-worthy' frame (sharp, faces, eyes open) per shot.

Done when a contact sheet of a fixture shows one frame per shot with correct timecodes.

### §WT140 Post-export verification

After each export, an optional verify step probes the file (duration, frame count, rate,
resolution, codec parameters, colour tags), decodes it fully on the hardware decoder
checking for errors, measures loudness and true peak, and compares sampled frames
against the render (PSNR). The result is a pass/fail report beside the file and in the
queue.

Done when a deliberately truncated export fails verification with a clear message.

## Block N — Performance, delivery and the public face

### §WT141 Reference machines and performance budgets

Three reference machines are named (a recent NVIDIA desktop, an Intel iGPU laptop, an
AMD laptop) plus the VMware guest (WARP, for function only). Budgets: cold start to
window under 1.5 s; open a 3,000-clip .otio under 2 s; scrub response under one refresh
on cached frames and under 100 ms uncached for H.264 1080p; playback of the stress
timeline without drops at 1/2 resolution on the laptop; export of the reference timeline
at faster than real time with hardware encode. `tools/WinTimeline.Bench` (as
Revelei.Bench) runs them headless and writes JSON; a frame-time overlay (Ctrl+Shift+F12)
shows live numbers.

Done when the bench runs on the three machines and its results are kept per release,
failing when a budget regresses by more than 10%.

### §WT142 Memory budgets and leak tests

Every cache (frames, thumbnails, decoders, AI sessions) registers with a `MemoryBudget`
that reacts to `QueryVideoMemoryInfo` budget changes and system low-memory
notifications, trimming in a defined order. A soak test plays, scrubs and edits the
stress timeline for two hours via the CLI automation and asserts private bytes and VRAM
stay within a band.

Done when the soak test passes on the reference laptop.

### §WT143 Installer, file associations and Explorer integration

An Inno Setup per-user installer like Revelei's (`build/installer.iss`,
`PrivilegesRequired=lowest`, `{localappdata}\Programs\WinTimeline`), carrying the FFmpeg
LGPL DLLs and third-party notices (`collect-notices.ps1`). It associates .otio, .otioz
and .otiod with icons, registers an Explorer thumbnail provider (`IThumbnailProvider`,
out of process) that renders a strip of the timeline's first clips or a track map, and a
preview handler showing track structure and duration. Models are not bundled except the
small ones needed by default features.

Done when installing on a clean VM associates the types and Explorer shows a thumbnail
for a fixture .otio.

### §WT144 Release check and update

Reuse Modelado's `ReleaseCheck`/`ReleaseUpdate` (GitHub releases API, signed SHA256SUMS,
silent install of the new installer after the app closes), with a setting to check
never, on start, or weekly, and release notes shown in the app.

Done when an older build on the VM finds, downloads and installs a test release.

### §WT145 Distributing through the Microsoft Store

As Revelei's and Modelado's Store tasks: an MSIX package signed by the Store. Points
specific to this app: models and optional FFmpeg GPL encoders downloaded after install
to a folder the user chooses (allowed for an MSIX app?), the Explorer thumbnail provider
and file associations expressed as package extensions, WER dump collection, and full
trust for the native engine host processes. If one of these rules out MSIX, the reason
is written down instead.

Done when WinTimeline is listed with downloads working, or the reason it cannot be is
recorded.

### §WT146 README, site and the sibling family

README: what it is (a GPU-fast OTIO viewer and editor for Windows that exports video,
with local AI that never uploads), a screen capture, download first and build second,
supported formats and codecs, hardware requirements per feature, model licences, and
'From the same author' links to Revelei and Modelado with the shared line ('AI on your
PC, nothing leaves it'). A `site/` page on GitHub Pages with the same design as the
siblings, a short demo video exported by the app itself, and a section for OTIO pipeline
users (studios, VFX) separate from the creator audience.

Done when the README and the site describe only features in the release they link to.

### §WT147 The command-line tool

`wintimeline` CLI (the `tools/WinTimeline.Cli` project): `info` (tracks, duration, media
status), `validate` (the problems list, exit code), `convert` (adapters), `render`
(preset, range, output), `proxies` (generate for a timeline), `analyse` (AI analyses
with installed models), `diff` (two timelines, JSON or text), `thumbnail`. Output in
text or JSON, documented in `docs/COMMAND-LINE.md`, with the same engine as the app.

Done when each command has a test, and `render` of a fixture produces the same file as
the app's export.

### §WT148 Scripting and macros

A macro recorder records commands from the history into a replayable JSON macro; a C#
scripting console (Roslyn scripting, sandboxed to the command API and the document
model, no file system writes except through exports) runs scripts against the open
document. A local named-pipe API lets external tools (Python with opentimelineio) push a
timeline into the open app and read the current one, for round-trips with pipeline
tools.

Done when a recorded macro replays on another timeline, and a Python script sends an
.otio that opens in a new tab.

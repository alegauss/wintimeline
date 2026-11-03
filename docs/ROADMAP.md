# Roadmap (active backlog)

## Priority

- WT23

## Block A — Project foundation

- 📋 **WT7** (deps: WT6 ✅) **An unhandled exception or a native crash closes the window without a dump or a recovery offer** — Native code (decoders, drivers, inference engines) will crash sometimes, and recovery plus a dump turns that into a fixable bug. → §WT7
- 📋 **WT8** (deps: WT2 ✅) **There is no one command that builds and runs every test, case and evaluation the way the sibling apps do** — The one-task-one-commit rule needs a gate, and the siblings' run-tests.cmd is the proven shape. → §WT8
- 📋 **WT9** (deps: WT8) **No CI builds the solution or runs the headless tests on each push** — A breaking change to the model or the OTIO round trip must be caught before a release, not by a user. → §WT9
- 📋 **WT10** (deps: WT8) **The project skill describes another repository's npm gates and RG prefix, and the winwright plugin is not enabled** — A session executing this roadmap follows that skill literally, so wrong gates mean commits validated by commands that do not exist here. → §WT10
- 📋 **WT150** (deps: WT3 ✅) **Shaders would be compiled at start-up, so a broken one crashes on the user's desk instead of failing the build** — Start-up time and shader errors belong to the build, and tuning a shader should not need a restart; split out of WT3. → §WT150
- 📋 **WT151** (deps: WT3 ✅) **A texture on the shared device cannot reach D3D12 without a copy, and nothing reports VRAM pressure or GPU decode** — Windows ML and heavy compute run on D3D12, and caches must shrink before the driver evicts; split out of WT3. → §WT151
- 📋 **WT152** (deps: WT4 ✅) **The GPU surfaces swallow mouse, touch, pen, keys and dropped files, and nothing can draw over them** — The viewer and timeline are where editing happens, so input, focus and drops must reach the app; split out of WT4. → §WT152
- 📋 **WT153** (deps: WT4 ✅) (requires: second-monitor) **The surfaces draw on the UI thread at its pace, and nobody measured the HwndHost against D3DImage or composition** — Playback needs the display's refresh with the UI thread out of the loop, and the host choice decides latency; split out of WT4. → §WT153

## Block B — OTIO document model and file formats

- 📋 **WT12** (deps: WT11 ✅) **No in-memory model holds a timeline: tracks, clips, gaps, transitions, markers and metadata have no types** — The viewer, the editor, the AI tools and the exporter all speak this model, so it is the spine of the application. → §WT12
- 📋 **WT13** (deps: WT12) **A .otio file cannot be opened or saved, and unknown schemas from newer OTIO versions would be dropped** — Opening and saving without loss is the first promise of a viewer-editor; a save that silently drops a studio's metadata loses trust forever. → §WT13
- 📋 **WT14** (deps: WT13) **An .otioz or .otiod bundle cannot be opened, so timelines shipped with their media by other tools are unusable** — Bundles are how OTIO moves a timeline together with its media between machines, and they are the natural archive format for this app too. → §WT14
- 📋 **WT15** (deps: WT13) **EDL (CMX 3600), FCP XML and AAF timelines cannot be imported, which is where most real projects come from** — Editors move work between Premiere, Resolve, Avid and finishing tools through these formats, so a viewer that reads only .otio sees little. → §WT15
- 📋 **WT16** (deps: WT12) **No edit can be undone, and there is no single place where a change to the document is described** — Every editing tool, the AI assistant and scripting all need one command pipeline, or undo breaks the first time two of them touch the same clip. → §WT16
- 📋 **WT17** (deps: WT13, WT16) **A crash or power cut loses every edit since the last manual save** — Long editing sessions are exactly when a GPU driver reset happens, and losing an hour of cuts is the failure people remember. → §WT17
- 📋 **WT18** (deps: WT13) **A timeline whose media moved shows every clip as missing, with no way to point it at the new folder** — OTIO files travel between machines and drive letters constantly, so relinking is needed on almost every file received from someone else. → §WT18
- 📋 **WT19** (deps: WT12) **A malformed timeline (overlapping transitions, negative durations, rate mismatches) opens silently and misbehaves later** — Files from other tools are often subtly broken, and reporting it on open beats an exporter crash an hour later. → §WT19
- 📋 **WT20** (deps: WT13) **Only one timeline can be open, and a file holding a SerializableCollection of timelines shows just the first** — Editorial hand-offs often carry several reels or versions in one file, and comparing versions side by side is a core review task. → §WT20
- 📋 **WT21** (deps: WT20) **Two versions of a cut cannot be compared, so what changed between v1 and v2 must be found by eye** — Editorial review is mostly asking what changed, and OTIO's structure makes an exact answer possible. → §WT21
- 📋 **WT22** (deps: WT13) **Bins, proxies settings, layouts and AI analysis have nowhere to live without polluting the shared .otio** — The .otio must stay clean for interchange, while the editor needs per-project state that survives reopening. → §WT22

## Block C — Media probing and GPU decoding

- 📋 **WT23** (deps: —) (requires: three-gpu-vendors) **No decision says which decoder reads which codec, so ProRes, DNxHR, HEVC 10-bit and image sequences have no defined path** — Media Foundation is native and GPU-friendly but misses pro codecs, while FFmpeg covers them at a licensing and packaging cost. → §WT23
- 📋 **WT24** (deps: WT23) **A media file's duration, rate, resolution, timecode, audio layout and colour tags are not known before it is played** — Every clip's available range and every rate conversion depend on reliable probing, done fast enough for a folder of hundreds of files. → §WT24
- 📋 **WT25** (deps: WT24, WT3 ✅) **Video frames cannot be decoded into GPU textures at an arbitrary frame number** — Frame-accurate random access, not linear playback, is the workload of an editor. → §WT25
- 📋 **WT26** (deps: WT25) **Image sequences (EXR, DPX, PNG, TIFF, JPEG) and stills cannot be used as clips** — VFX plates and graphics arrive as sequences, and OTIO has an ImageSequenceReference for them. → §WT26
- 📋 **WT27** (deps: WT24) **Audio streams cannot be decoded to float samples at an arbitrary sample position** — Playback, waveforms, mixing, AI transcription and export all consume decoded audio. → §WT27
- 📋 **WT28** (deps: WT25) **Decoded frames ignore their colour tags, so Rec.709, Rec.2020 PQ/HLG and full/limited range look wrong** — Wrong range or matrix is the most common visible bug in video tools, and HDR sources are now ordinary phone footage. → §WT28
- 📋 **WT29** (deps: WT25, WT27) **Several 4K streams decoded at once (a dissolve, a picture-in-picture, a multicam) stall playback** — Real timelines overlap streams, and the decoder scheduling decides whether they play. → §WT29
- 📋 **WT30** (deps: WT24) **Media that changes on disk (a re-rendered VFX shot, a growing recording) is not noticed by the open project** — VFX versions are replaced in place constantly, and showing a stale frame causes wrong review decisions. → §WT30

## Block D — GPU compositing and playback engine

- 📋 **WT31** (deps: WT12, WT28) **Nothing turns a timeline position into a composited frame: no layer order, no blending, no transforms** — The viewer, the thumbnails of compounds and the exporter all need one renderer, or the export will differ from what was watched. → §WT31
- 📋 **WT32** (deps: WT31) **Layers cannot be scaled, positioned, cropped, rotated or blended with opacity and blend modes** — Picture-in-picture, titles over video and letterboxing are needed in nearly every edit. → §WT32
- 📋 **WT33** (deps: WT31) **Transitions between clips render as hard cuts** — A dissolve that plays as a cut misrepresents the editor's work in review and in export. → §WT33
- 📋 **WT34** (deps: WT31, WT29) **Playback has no master clock, so picture and sound drift apart and frames are dropped unpredictably** — Lip sync off by two frames is noticed by everyone, and smooth playback is the first impression of the app. → §WT34
- 📋 **WT35** (deps: WT31) **Scrubbing back over frames already seen decodes and composites them again** — Editors scrub the same few seconds over and over, and a cache turns that into instant feedback. → §WT35
- 📋 **WT36** (deps: WT31, WT4 ✅) (requires: hdr-display) **The viewer cannot show frames with the right display transform, zoom, safe areas or HDR output** — What the editor sees must match what the export produces, on SDR and on HDR monitors. → §WT36
- 📋 **WT37** (deps: WT31, WT35) **Playback resolution and quality cannot be lowered to keep real time on heavy timelines** — A laptop must play a 4K multi-layer timeline, and dropping resolution beats dropping frames. → §WT37
- 📋 **WT38** (deps: WT3 ✅, WT34) **A GPU driver reset or adapter change (laptop dock, eGPU) crashes or freezes the app** — TDRs happen during long sessions, and losing the window loses trust even when autosave keeps the edits. → §WT38
- 📋 **WT39** (deps: WT36) **There are no waveform, vectorscope, parade or histogram scopes to judge exposure and colour** — Legal levels and colour matching cannot be judged by eye on an uncalibrated laptop display. → §WT39

## Block E — Proxies, thumbnails, waveforms and background jobs

- 📋 **WT40** (deps: WT1 ✅) **Long work (proxies, analysis, renders) has no shared queue, priorities, progress or cancellation** — Proxies, AI analysis and export compete for the same GPU encoders and disks, and must not freeze the editor. → §WT40
- 📋 **WT41** (deps: WT40, WT25, WT27) **Heavy camera media (4K/8K HEVC 10-bit, RAW-derived ProRes) cannot be edited smoothly on a laptop** — Proxy editing is the standard answer, and generating proxies on the GPU encoder makes it nearly free. → §WT41
- 📋 **WT42** (deps: WT41, WT37) **Playback cannot switch between proxy and original media, per clip or globally, without relinking** — Editors cut on proxies and check or export on originals, and the switch must be one click with exact frames. → §WT42
- 📋 **WT43** (deps: WT41, WT24) **Users must decide clip by clip which media needs a proxy and when to create it** — Most people do not know which codecs are heavy, and the app can measure it. → §WT43
- 📋 **WT44** (deps: WT40, WT25) **Clip filmstrips and bin thumbnails are not generated or cached, so the timeline shows grey boxes** — Pictures are how editors find shots, and regenerating them on every open wastes minutes. → §WT44
- 📋 **WT45** (deps: WT40, WT27) **Audio clips show no waveform, and computing one blocks the editor for long files** — Sound editing and finding dialogue rely on seeing the waveform. → §WT45
- 📋 **WT46** (deps: WT42) **Proxies made on one machine cannot be reused by a colleague or after moving the project** — Shared storage teams create proxies once, and recreating them per machine wastes hours. → §WT46

## Block F — Timeline view: GPU drawing and navigation

- 📋 **WT47** (deps: WT12, WT4 ✅) **A timeline with thousands of clips cannot be drawn smoothly by the WPF visual tree** — A feature-length cut has 3,000 clips and dozens of tracks, and one visual per clip collapses WPF layout long before that. → §WT47
- 📋 **WT48** (deps: WT47) **The timeline cannot be zoomed from whole-program to single-frame or scrolled smoothly** — Editing alternates between the whole structure and frame-accurate detail many times a minute. → §WT48
- 📋 **WT49** (deps: WT47) **There is no time ruler, so positions cannot be read in timecode, frames, feet+frames or seconds** — Editors, assistants and sound people each read time in a different unit, and the ruler is where they look first. → §WT49
- 📋 **WT50** (deps: WT47, WT16) **Tracks cannot be added, removed, renamed, resized, reordered, locked, muted, soloed or hidden** — Track management is the frame every other editing action happens inside. → §WT50
- 📋 **WT51** (deps: WT49) **No playhead marks the current frame, and the viewer is not tied to a position in the timeline** — The playhead is the shared cursor between the viewer, the edit tools and the AI assistant. → §WT51
- 📋 **WT52** (deps: WT47) **Clips cannot be selected singly, by range, by marquee, by track forward or by type** — Every edit acts on a selection, and a weak selection model forces editors into many repeated clicks. → §WT52
- 📋 **WT53** (deps: WT52) **Dragged clips and the playhead do not snap to edit points, markers or the playhead** — Without snapping every edit risks one-frame gaps and flashes that are invisible until export. → §WT53
- 📋 **WT54** (deps: WT47, WT16) **Markers from the file are not shown, and new markers with colour, duration and notes cannot be added** — OTIO markers carry review notes and AI findings, and they are how this app talks back to the rest of a pipeline. → §WT54
- 📋 **WT55** (deps: WT47, WT44, WT45) **Clips all look alike: no thumbnails, waveform, name, speed badge, effect badge or offline state** — Editors navigate a cut by recognising pictures and shapes, not by reading names. → §WT55
- 📋 **WT56** (deps: WT47) **A nested Stack inside a track shows as an opaque block that cannot be opened or edited** — OTIO uses nested stacks for compound clips and multicam sequences, which are common in delivered timelines. → §WT56
- 💭 **WT57** (deps: WT48) **Nothing shows where in a long timeline the edits, problems or AI findings are concentrated** — A long program needs an at-a-glance map before anyone can decide where to spend attention. → §WT57
- 📋 **WT58** (deps: WT52, WT51) **The timeline cannot be used without a mouse or read by a screen reader** — Power editors work keyboard-only, and winwright needs automation peers to drive the timeline in cases. → §WT58

## Block G — Workspace, panels and viewers

- 📋 **WT59** (deps: WT1 ✅) (requires: second-monitor) **Panels cannot be docked, tabbed, floated onto a second monitor, or saved as named workspaces** — Editors arrange the screen per task (cut, sound, colour, review), and a fixed layout wastes a second monitor. → §WT59
- 📋 **WT60** (deps: WT59, WT36, WT34) **No program viewer shows the timeline at the playhead with transport controls** — The program viewer is where every edit is judged. → §WT60
- 📋 **WT61** (deps: WT60) **A media file cannot be opened in a source viewer to mark in and out before editing** — Choosing the part of a shot to use happens in the source viewer, before the timeline. → §WT61
- 📋 **WT62** (deps: WT59, WT22, WT24) **Imported media has nowhere to live: no bins, folders, list or grid views, search or metadata columns** — Projects have hundreds of files, and finding the right one quickly is a large share of editing time. → §WT62
- 📋 **WT63** (deps: WT59, WT52) **Clip, track, transition and timeline properties, including OTIO metadata, cannot be seen or edited** — OTIO carries rich metadata, and a viewer-editor must show and edit it faithfully. → §WT63
- 📋 **WT64** (deps: WT59, WT16) **Past edits cannot be seen as a list or jumped to** — Seeing what changed, and returning to a point before a bad idea, is faster than pressing undo twenty times. → §WT64
- 📋 **WT65** (deps: WT59, WT54) **Markers cannot be listed, filtered, edited in bulk or exported as review notes** — Review feedback arrives as dozens of markers, and working through them needs a list. → §WT65
- 📋 **WT66** (deps: WT1 ✅) **Actions can only be found in menus, so less common commands are hard to discover** — A searchable command palette makes the whole feature set reachable and is also where the AI assistant's commands surface. → §WT66
- 📋 **WT67** (deps: WT5 ✅, WT59) **Preferences (cache locations, GPU choice, autosave, proxies, AI models, keymap) cannot be changed in the app** — Defaults never fit every machine, and editing JSON files by hand is not acceptable for this audience. → §WT67
- 📋 **WT68** (deps: WT1 ✅) **Opening the app shows an empty window with no recent projects, templates or first steps** — The first minute decides whether someone keeps the app, and a welcome page with drag-a-file guidance makes it obvious. → §WT68
- 📋 **WT69** (deps: WT1 ✅) **Errors, finished jobs and warnings appear as modal dialogs or not at all** — Modal dialogs interrupt editing, and silent failures cost more. → §WT69

## Block H — Editing: tools, trims and drag and drop

- 📋 **WT70** (deps: WT53, WT16) **Clips cannot be dragged along or across tracks, and dropping one on another does not overwrite or insert** — Moving clips is the most frequent edit, and its overwrite versus insert behaviour decides whether the tool feels safe. → §WT70
- 📋 **WT71** (deps: WT52, WT16) **A clip cannot be split at the playhead or at a click, on one track or all tracks** — Splitting is the primitive behind most rough-cut work. → §WT71
- 📋 **WT72** (deps: WT70) **Clip heads and tails cannot be trimmed, and ripple, roll, slip and slide edits do not exist** — Trimming is where an edit is refined, and the four trim types are what separates an editor from a cutter. → §WT72
- 📋 **WT73** (deps: WT70, WT24) **Media cannot be dragged from the bin or from Windows Explorer onto the timeline or the viewer** — Drag and drop is how people expect to start an edit, and Explorer drops make the first minute of use obvious. → §WT73
- 📋 **WT74** (deps: WT70, WT61) **A source clip's marked range cannot be edited into the timeline with insert, overwrite, replace or fit-to-fill** — Three-point editing with the keyboard is the fastest way to assemble, and professional editors expect it. → §WT74
- 📋 **WT75** (deps: WT72) **A marked range cannot be lifted or extracted, and selected clips cannot be ripple deleted** — Removing material cleanly, with or without closing the gap, is half of editing. → §WT75
- 📋 **WT76** (deps: WT72, WT33) **Transitions cannot be added, resized or changed between two clips** — Dissolves and wipes are part of almost every delivered cut, and OTIO stores them explicitly. → §WT76
- 📋 **WT77** (deps: WT72) **Clip speed cannot be changed, reversed or frozen, and time remapping from other tools is ignored** — Speed changes are routine in every genre, and OTIO represents them with LinearTimeWarp and FreezeFrame effects. → §WT77
- 📋 **WT78** (deps: WT70) **Clips cannot be copied and pasted within or between timelines, or pasted as attributes** — Reusing material and effect settings across a cut is constant work. → §WT78
- 📋 **WT79** (deps: WT70) **Video and its audio cannot be linked or unlinked, and clips cannot be grouped** — Keeping sync between picture and sound is the editor's constant worry, and groups protect it. → §WT79
- 📋 **WT80** (deps: WT72) **Shortcuts cannot be customised, and editors coming from Premiere, Resolve or Avid must relearn everything** — Muscle memory is the biggest switching cost, and presets for the main editors remove it. → §WT80
- 💭 **WT81** (deps: WT56) **Several angles of the same event cannot be synced and cut live as a multicam clip** — Interviews, events and podcasts are multicam, and cutting them clip by clip is slow. → §WT81
- 📋 **WT82** (deps: WT72, WT79) **Insert and ripple edits on one track push other tracks out of sync with no way to protect them** — Sync locks are what keep music, dialogue and picture together during ripple edits. → §WT82

## Block I — Audio: playback, mixing and meters

- 📋 **WT83** (deps: WT27) **Audio cannot be played through a chosen output device with low latency** — The audio device is the master clock for playback, so its stability decides A/V sync. → §WT83
- 📋 **WT84** (deps: WT83, WT50) **Clip and track volume, pan and mute cannot be set, and there is no mixer** — Balancing dialogue, music and effects is required for any watchable export. → §WT84
- 📋 **WT85** (deps: WT84) **No level meters or loudness measurement show whether the mix is clipping or meets delivery specs** — Broadcast and streaming platforms reject or turn down mixes outside loudness targets. → §WT85
- 📋 **WT86** (deps: WT83, WT34) **Scrubbing the playhead is silent, so dialogue edits cannot be found by ear** — Editors find the start of a word by scrubbing audio, not by watching. → §WT86
- 📋 **WT87** (deps: WT84, WT76) **Audio clips cannot fade in or out, and cuts between audio clips click** — Hard audio cuts produce audible clicks that make an edit sound amateur. → §WT87
- 📋 **WT88** (deps: WT84, WT62) **Multichannel field recordings cannot be split into the channels an edit needs** — Interviews arrive as poly WAVs with lav, boom and mix on separate channels. → §WT88
- 💭 **WT89** (deps: WT83, WT50) **A voice-over cannot be recorded directly onto a track while the timeline plays** — Scratch narration during editing is common and otherwise needs a separate app. → §WT89

## Block J — Effects, titles, keyframes and colour

- 📋 **WT90** (deps: WT32, WT63) **Effects cannot be added, ordered, enabled or parameterised on clips, and OTIO effects are not interpreted** — Effects are how the editor shapes the picture, and they must survive a round trip through OTIO. → §WT90
- 📋 **WT91** (deps: WT90) **No parameter can change over time, so fades, moves and zooms cannot be animated** — Animated position, opacity and audio levels are needed in most edits. → §WT91
- 📋 **WT92** (deps: WT91) **No built-in effects exist for the everyday needs: blur, sharpen, crop, stabilise, mask, chroma key** — Editors expect a core set without hunting for plugins. → §WT92
- 📋 **WT93** (deps: WT91, WT39) **Clip colour cannot be corrected: no exposure, white balance, lift/gamma/gain, saturation or curves** — Matching shots and fixing exposure are needed even in edits that never reach a colourist. → §WT93
- 📋 **WT94** (deps: WT93, WT36) **Projects using ACES or a studio OpenColorIO config cannot use it for input, working and display transforms** — Studios that exchange OTIO usually run colour through OCIO, and ignoring it shows wrong colours in review. → §WT94
- 📋 **WT95** (deps: WT91) **Text titles, lower thirds and credits cannot be created** — Almost every delivered video has titles, and leaving the app to make them breaks the flow. → §WT95
- 📋 **WT96** (deps: WT95) **Solid colours, gradients, bars and tone, countdowns and placeholders cannot be generated** — Slates, leaders and placeholders for missing shots are routine editorial needs. → §WT96
- 📋 **WT97** (deps: WT95) **Subtitles and captions cannot be imported, edited, styled or exported** — Captions are required by platforms and law in many places, and the AI block produces them from speech. → §WT97
- 💭 **WT98** (deps: WT92) **Third parties cannot add effects or transitions without changing the app** — A shader-based plugin format lets the community and the AI block add looks without new releases. → §WT98

## Block K — Local AI platform: models, runtimes and hardware fit

- 📋 **WT99** (deps: WT3 ✅) **No inference runtime is wired in, so no model can run on the GPU or NPU of this PC** — Every AI feature needs one runtime choice shared across features, or each one ships its own engine and VRAM fights follow. → §WT99
- 📋 **WT100** (deps: WT99) **There is no catalog saying which models exist, their licence, size, hardware needs and download source** — People must know what they download, what it costs on disk and in VRAM, and whether its licence allows their use. → §WT100
- 📋 **WT101** (deps: WT100, WT40) **Models cannot be downloaded, resumed, verified, moved to another drive or removed from inside the app** — Models weigh from 50 MB to 10 GB, and an interrupted or corrupt download must never break a feature. → §WT101
- 📋 **WT102** (deps: WT101, WT67) **Nothing in the UI shows which AI features are available, which models they need and whether this PC can run them** — AI features must explain themselves before a 4 GB download, or people will not trust them. → §WT102
- 📋 **WT103** (deps: WT99) **Nothing guarantees that media, transcripts or prompts never leave the PC** — Editors work under NDA, and a local-only promise is only worth something if it is enforced and visible. → §WT103
- 📋 **WT104** (deps: WT22, WT99) **AI results (transcripts, shots, tags, embeddings) would be recomputed every time a project opens** — Analysis is expensive, and its results are what search, the assistant and the timeline all read. → §WT104
- 📋 **WT105** (deps: WT99, WT34) **AI inference running during playback fights the decoder and compositor for the GPU and causes dropped frames** — AI analysis is meant to run in the background while the user edits, not instead of editing. → §WT105

## Block L — AI-assisted editing

- 📋 **WT106** (deps: WT104, WT102, WT27) **Speech in the media cannot be transcribed with word timings, so dialogue cannot be searched or cut by text** — A transcript with word times is the base of text editing, captions, search and the assistant's understanding of the footage. → §WT106
- 📋 **WT107** (deps: WT106) **Transcripts do not say who is speaking** — Interviews and podcasts are edited by speaker, and speaker turns drive multicam switching and captions. → §WT107
- 📋 **WT108** (deps: WT106, WT75) **Footage cannot be edited by selecting and deleting words in its transcript** — Text-based editing is the fastest way to build an interview or podcast cut, and it is now an expected feature. → §WT108
- 📋 **WT109** (deps: WT108) **Filler words, long pauses and repeated takes must be found and cut by hand** — Cleaning a talking-head edit is hours of repetitive work that word timings make automatic. → §WT109
- 📋 **WT110** (deps: WT106, WT97) **Captions must be typed by hand even though a transcript exists** — Most social and accessibility deliveries need captions, and timing them is tedious. → §WT110
- 📋 **WT111** (deps: WT104, WT25) **Long recordings and delivered masters are not split into shots, so finding a cut point means scrubbing** — Shot boundaries let an editor re-cut a master and let the other AI tools reason about shots instead of frames. → §WT111
- 📋 **WT112** (deps: WT111) **Footage has no descriptive tags (shot size, subjects, location, motion), so bins cannot be filtered by content** — Logging footage by hand is a full-time assistant job that vision models now do well enough to help. → §WT112
- 📋 **WT113** (deps: WT112, WT106, WT62) **Footage cannot be searched by describing what is in it, like 'a woman laughing near a window'** — Natural-language search across hours of footage is the AI feature editors say saves the most time. → §WT113
- 📋 **WT114** (deps: WT99, WT16, WT66) **Editing intentions cannot be expressed in plain language and turned into reviewed timeline changes** — A local assistant that proposes edits as commands, not as a black box, can do tedious multi-step work while the editor keeps control. → §WT114
- 💭 **WT115** (deps: WT110, WT114) **Captions and transcripts cannot be translated into other languages** — Reaching other audiences needs subtitles in their language, and translation can run locally. → §WT115
- 📋 **WT116** (deps: WT114, WT113) **A first assembly from a script or a list of selects must be built clip by clip** — Assembling a rough cut from transcripts is mechanical work the assistant and search can do in minutes. → §WT116
- 💭 **WT117** (deps: WT113, WT108) **Finding B-roll that matches what is being said requires browsing the whole library** — Covering interviews with relevant visuals is a constant need, and transcript plus image search can suggest it. → §WT117
- 📋 **WT118** (deps: WT112, WT91) **A 16:9 edit cannot be reframed to 9:16 or 1:1 with the subject kept in frame** — Every cut now ships vertical versions for social platforms, and reframing by hand is keyframe drudgery. → §WT118
- 📋 **WT119** (deps: WT84, WT99) **Noisy dialogue, hum and reverb cannot be cleaned inside the app** — Most location audio needs cleanup, and modern denoisers run in real time on a laptop. → §WT119
- 📋 **WT120** (deps: WT119, WT106) **Music cannot be separated from dialogue, and background music is not ducked under speech automatically** — Ducking music under voice is a step in nearly every video, and stem separation rescues mixed recordings. → §WT120
- 📋 **WT121** (deps: WT104, WT54) **Cuts cannot be placed on the beats of a music track** — Montages cut to music are common, and beat markers make them fast and precise. → §WT121
- 📋 **WT122** (deps: WT77, WT99) **Slow motion from normal-rate footage stutters, because frames are only repeated or blended** — Smooth slow motion is a common request and optical-flow models make it possible from ordinary footage. → §WT122
- 📋 **WT123** (deps: WT99, WT40) **Low-resolution archive or phone clips look soft in a 4K timeline** — Mixed-resolution timelines are common, and Revelei already proves Real-ESRGAN runs well on this hardware. → §WT123
- 💭 **WT124** (deps: WT92, WT99) **A person cannot be separated from the background without a green screen** — Background replacement and text-behind-subject effects are popular and now feasible locally. → §WT124
- 📋 **WT125** (deps: WT112, WT92) **Faces and licence plates cannot be blurred automatically for privacy** — News, documentary and corporate videos must hide bystanders, and tracking them by hand takes hours. → §WT125
- 📋 **WT126** (deps: WT111, WT19) **Flash frames, black frames, frozen frames, out-of-focus shots and jump cuts are found only by watching** — A technical QC pass before delivery catches embarrassing errors that a tired editor misses. → §WT126
- 📋 **WT127** (deps: WT114, WT21) **Chapters, a description and a summary of a cut, or of what changed between versions, must be written by hand** — Publishing needs chapters and descriptions, and review needs a readable change summary. → §WT127
- 💭 **WT128** (deps: WT93, WT112) **Shots from different cameras cannot be matched in colour automatically** — Multicam and mixed-camera edits need consistent colour, and a first automatic match saves most of the work. → §WT128
- 💭 **WT129** (deps: WT81, WT107) **Multicam podcasts must be cut angle by angle even though the speaker is known at every moment** — Speaker diarisation can produce a watchable first cut of a multicam conversation automatically. → §WT129
- 💭 **WT130** (deps: WT99, WT84) **A scratch narration cannot be produced from text while the real voice-over is not recorded yet** — Editors time a cut against a narration long before the final recording exists. → §WT130
- 📋 **WT131** (deps: WT27, WT79) **Separately recorded audio cannot be synced to camera audio automatically** — Double-system sound is standard on productions, and clapping by hand is slow. → §WT131

## Block M — Video export and interchange

- 📋 **WT132** (deps: WT23) (requires: three-gpu-vendors) **No decision says which encoders write H.264, HEVC, AV1, ProRes and DNxHR, or how hardware encoders are used** — Hardware encoders make export fast but differ per vendor, and pro intermediates need FFmpeg, so the matrix must be measured. → §WT132
- 📋 **WT133** (deps: WT132, WT31, WT84) **A timeline cannot be rendered to a video file at all** — Exporting a video is the end goal of the application, and it must equal what the viewer showed. → §WT133
- 📋 **WT134** (deps: WT133) **Users must know codecs and bitrates to export, with no presets for YouTube, Instagram, TikTok or mastering** — Most people want 'good for YouTube', and professionals want exact masters; presets serve both. → §WT134
- 📋 **WT135** (deps: WT134, WT40) **Several exports (versions, aspect ratios, masters) cannot be queued and left to run** — Deliveries are batches: master, web, vertical and captions, often overnight. → §WT135
- 📋 **WT136** (deps: WT133, WT36) (requires: hdr-display) **HDR timelines cannot be exported as HDR10 or HLG with correct metadata** — Phones record HDR now, and an SDR-only export flattens footage people expect to look bright. → §WT136
- 💭 **WT137** (deps: WT133) **Exporting an unchanged section re-encodes it, wasting time and quality** — Long-GOP sources cut without effects can be passed through, making some exports ten times faster. → §WT137
- 📋 **WT138** (deps: WT15, WT14, WT133) **The edited timeline cannot be handed back to other tools as OTIO, EDL or FCPXML with its media** — OTIO is an interchange format first, and round-tripping to Resolve, Premiere or a finishing house is the professional use. → §WT138
- 📋 **WT139** (deps: WT133) **Single frames, contact sheets and short animated previews cannot be exported** — Stills for thumbnails, review sheets and GIF/WebP previews are daily needs around an edit. → §WT139
- 📋 **WT140** (deps: WT133, WT85) **Nobody checks an export after it is written, so a broken or out-of-spec file is delivered** — A corrupt or wrong-loudness delivery is costly, and checking it automatically costs seconds. → §WT140

## Block N — Performance, delivery and the public face

- 📋 **WT141** (deps: WT34, WT47) (requires: three-gpu-vendors) **No measured budgets say how fast opening, scrubbing, playback and export must be, or on which machines** — Performance claims are only meaningful against named machines and numbers, and regressions hide without them. → §WT141
- 📋 **WT142** (deps: WT141, WT35) **Memory and VRAM use grow without bounds on long sessions with many media files** — Editors leave the app open all day, and a slow leak shows up as a crash in the afternoon. → §WT142
- 📋 **WT143** (deps: WT1 ✅, WT133) **There is no installer, file association or thumbnail preview for .otio files in Explorer** — People install from a single download, and seeing a timeline's thumbnail in Explorer makes the file type feel native. → §WT143
- 📋 **WT144** (deps: WT143) **Users are not told about new versions and cannot update from inside the app** — Media apps ship decoder and driver workarounds often, and stale versions cause support load. → §WT144
- 📋 **WT145** (deps: WT143, visibility VS58) (requires: store-account) **Every first download meets a SmartScreen warning, and the Store, where creative apps are looked for, has no WinTimeline** — The Store signs the MSIX itself, and claude-tray tests that path first, so this follows what that test learns. → §WT145
- 📋 **WT146** (deps: WT133) **There is no README or site saying what WinTimeline does, for whom, and with which hardware** — A visitor decides in seconds whether the app is for them, and the sibling apps show the same family promise. → §WT146
- 📋 **WT147** (deps: WT133, WT15) **Timelines cannot be validated, converted, rendered or analysed from the command line or a script** — Pipelines (render farms, dailies, CI for editorial) drive OTIO headless, and the CLI also powers the soak and bench tests. → §WT147
- 💭 **WT148** (deps: WT147, WT114) **Repetitive pipeline tasks inside an open project cannot be automated by users** — OTIO users are often pipeline TDs who script everything, and the command pipeline makes safe scripting possible. → §WT148
- 💭 **WT149** (deps: WT1 ✅) **UI strings are not centralised, so a later translation or terminology review would touch every view** — The siblings keep English everywhere on purpose, but centralised strings still keep terminology consistent across a large UI. → §WT149

## Done when — Block A

- **The solution builds warning-free from a clean clone** CI builds with
  TreatWarningsAsErrors on a fresh runner and runs the architecture tests.
- **Every UI task ships with a winwright case that passed on the VMware guest** A screen
  is proven by a case in cases\*.cases.json driving the real window through
  run-tests-vm.cmd, never on the operator's desk; winwright friction is filed in its own
  roadmap (prefix WW).

## Done when — Block B

- **Every OTIO corpus file round-trips without losing a key** The OTIO repository's
  sample_data and the adapter fixtures are loaded and saved in CI and compared to the
  input; any dropped key or UnknownSchema fails the build.
- **Ranges and timecode agree with the reference library** A CI step runs Python
  opentimelineio over the same files and compares every item's range_in_parent and
  timecode string with the app's model.

## Done when — Block C

- **Every decoded frame is the frame the timeline asked for** The burnt-in frame counter
  test runs over the whole codec matrix with random seeks in CI and fails on any
  off-by-one.

## Done when — Block D

- **The exported frame equals the frame the viewer showed** A test renders fixture
  timelines through the playback path at full quality and through the export path, and
  compares frames before encoding within 1/1024.
- **Picture and sound stay within one millisecond over ten minutes** The loopback A/V
  sync fixture runs in the performance suite on the reference machine before each
  release.

## Done when — Block E

- **A proxy always has the same frame count and timecode as its source** A test
  generates proxies for every fixture and compares frame counts and start timecode; any
  mismatch fails.
- **Background work never drops a playback frame** The performance suite plays the
  stress timeline while a proxy batch and an analysis job run, and the dropped frame
  counter must stay at zero.

## Done when — Block F

- **The timeline holds the display refresh rate at 3,000 clips** The frame-time overlay
  records scroll and zoom over the stress fixture on the reference machine; the 99th
  percentile frame stays under one refresh interval.
- **Every visible clip is reachable by UI Automation** A winwright case locates a clip
  by name in the stress fixture, selects it and reads its range through the automation
  tree.

## Done when — Block G

- **Every panel is drivable by a winwright case** Each panel's main actions have a case
  in cases\*.cases.json, run on the VMware guest through winwright before the block
  closes.

## Done when — Block H

- **No edit ever produces overlapping items or a range outside available media** A
  property test applies random tool gestures to fixtures and checks the document
  invariants after each, across thousands of runs in CI.
- **Every editing gesture is one undo step** Cases perform each gesture, press Ctrl+Z
  once and compare the document to its state before the gesture.

## Done when — Block I

- **A mix exports sample-exact to what the meters showed** The fixture mix exported to
  WAV is compared with an offline reference render in CI.

## Done when — Block J

- **Every effect, title and grade survives an OTIO round trip** A test saves fixtures
  with every built-in effect to .otio, reloads them and compares parameters and rendered
  frames.

## Done when — Block K

- **Every AI feature works offline after its model is installed** Cases run each feature
  on the VM with the network adapter disabled after installing its model.
- **No AI feature appears as a dead control** A test enumerates the AI commands with no
  models installed and checks each one offers its model install instead of failing.

## Done when — Block L

- **Every AI edit is previewed before it changes the document** Cases run each AI
  feature that edits the timeline and assert the document is unchanged until Apply, and
  that one undo reverts the whole batch.
- **Each AI feature reports its measured quality on fixtures** An evaluation suite with
  labelled fixtures runs each feature and records its metric; a regression below the
  recorded value fails the run.

## Done when — Block M

- **A timeline exports to a playable MP4 that matches the viewer** The export fixture is
  rendered in CI with software encoders and compared frame by frame and sample by sample
  with the viewer path.
- **Hardware export works on all three GPU vendors** The encode matrix runs on an
  NVIDIA, an Intel and an AMD machine before each release, recorded in the release
  notes.

## Done when — Block N

- **Budgets hold on all three reference machines** The bench results for the release are
  attached to its notes and none exceeds its budget.
- **A clean VM goes from installer to an exported video in five minutes** A scripted
  case installs, opens a fixture .otio, exports with the YouTube preset and verifies the
  file.

## Non-goals

- **Builds for macOS, Linux, web or mobile** WinTimeline is Windows-only so it can build
  on Direct3D, Media Foundation, Windows ML and the WPF stack shared with Revelei and
  Modelado.
- **A WinUI 3 user interface** Revelei, Modelado, claude-tray and winwright's in-app
  package are WPF with the Fluent ThemeMode, so WPF reuses their design, GPU surface and
  tests as they are.
- **Cloud inference or external AI APIs** Every model runs on this PC; the network is
  used only to download catalog models and updates, which is the promise the whole
  product family makes.
- **Models committed to the repository** Weights are fetched by the model manager into
  the model store and verified by hash, never stored in git or required to build.
- **A node-based compositor or VFX package** Layers, effects, keyframes and masks cover
  editorial needs; node graphs belong to Nuke and Fusion, which receive the timeline
  through OTIO.
- **A grading suite to rival a dedicated colour application** Primaries, curves, LUTs
  and OCIO cover matching and review; secondary grading with tracked windows is a
  finishing job handed off through OTIO.
- **Camera RAW decoding (BRAW, R3D, ARRIRAW)** Each needs a vendor SDK with its own
  licence; such footage arrives as ProRes or DNxHR transcodes, which the app reads and
  proxies.
- **Real-time collaborative editing or cloud project sync** Collaboration happens by
  exchanging .otio and .otioz files, which keeps the app local and the format the shared
  truth.
- **Hosting native OFX or VST plugins** Native plugins run arbitrary code in the render
  loop and crash it; effects extend through shader manifests that cannot take the editor
  down.
- **Python embedded in the core application** The core stays .NET and native; OTIO
  adapters that exist only in Python run through an optional out-of-process bridge.
- **A Portuguese or multilingual interface** All code, UI text, docs and cases are in
  English, as in the sibling apps; a translation layer would double every string the
  cases assert on.

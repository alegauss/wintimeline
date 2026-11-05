# Roadmap (active backlog)

## Priority

- WT23

## Block A — Project foundation

- 📋 **WT153** (deps: WT4 ✅) (requires: second-monitor) **The surfaces draw on the UI thread at its pace, and nobody measured the HwndHost against D3DImage or composition** — Playback needs the display's refresh with the UI thread out of the loop, and the host choice decides latency; split out of WT4. → §WT153
- 📋 **WT154** (deps: WT143) **A crash in native code leaves no dump, since .NET fails fast past every in-process handler** — Decoder and driver crashes are the ones a log cannot explain, and only a dump shows where they happened; split out of WT7. → §WT154

## Block B — OTIO document model and file formats

- 📋 **WT159** (deps: WT18 ✅, WT24, WT22 ✅) **Media that was renamed as well as moved is not found, and a relink rule is forgotten on the next open** — Renames happen in every conform, and repeating the same relink each time a project opens is friction; split out of WT18. → §WT159
- 📋 **WT161** (deps: WT17 ✅, WT20 ✅) **Edits to a file of several timelines are not journaled, so a crash loses them** — The recovery journal addresses commands from one timeline; collections need paths that name the timeline too. → §WT161
- 📋 **WT162** (deps: WT21 ✅) **A comparison cannot be exported as a change-list EDL or as markers for another tool** — Online and sound editors receive changes as a change list or markers, and retyping the list is where conform errors come from. → §WT162
- 📋 **WT163** (deps: WT21 ✅) **The changes between two versions show only as a list, not as colour bands on the timeline** — A reviewer reads a cut by its shape; bands on the clips that moved, trimmed or were replaced show where the edits fall. → §WT163
- 📋 **WT164** (deps: WT22 ✅) **A layout change or tab choice made without saving the document is lost on close** — The sidecar is written only with a save, so a reviewer who only looked around reopens to the default layout. → §WT164

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

- 📋 **WT31** (deps: WT12 ✅, WT28) **Nothing turns a timeline position into a composited frame: no layer order, no blending, no transforms** — The viewer, the thumbnails of compounds and the exporter all need one renderer, or the export will differ from what was watched. → §WT31
- 📋 **WT32** (deps: WT31) **Layers cannot be scaled, positioned, cropped, rotated or blended with opacity and blend modes** — Picture-in-picture, titles over video and letterboxing are needed in nearly every edit. → §WT32
- 📋 **WT33** (deps: WT31) **Transitions between clips render as hard cuts** — A dissolve that plays as a cut misrepresents the editor's work in review and in export. → §WT33
- 📋 **WT34** (deps: WT31, WT29) **Playback has no master clock, so picture and sound drift apart and frames are dropped unpredictably** — Lip sync off by two frames is noticed by everyone, and smooth playback is the first impression of the app. → §WT34
- 📋 **WT35** (deps: WT31) **Scrubbing back over frames already seen decodes and composites them again** — Editors scrub the same few seconds over and over, and a cache turns that into instant feedback. → §WT35
- 📋 **WT36** (deps: WT31, WT4 ✅) (requires: hdr-display) **The viewer cannot show frames with the right display transform, zoom, safe areas or HDR output** — What the editor sees must match what the export produces, on SDR and on HDR monitors. → §WT36
- 📋 **WT37** (deps: WT31, WT35) **Playback resolution and quality cannot be lowered to keep real time on heavy timelines** — A laptop must play a 4K multi-layer timeline, and dropping resolution beats dropping frames. → §WT37
- 📋 **WT38** (deps: WT3 ✅, WT34) **A GPU driver reset or adapter change (laptop dock, eGPU) crashes or freezes the app** — TDRs happen during long sessions, and losing the window loses trust even when autosave keeps the edits. → §WT38
- 📋 **WT39** (deps: WT36) **There are no waveform, vectorscope, parade or histogram scopes to judge exposure and colour** — Legal levels and colour matching cannot be judged by eye on an uncalibrated laptop display. → §WT39

## Block E — Proxies, thumbnails, waveforms and background jobs

- ⏳ **WT40** (deps: WT1 ✅, WT34) **Long work (proxies, analysis, renders) has no shared queue, priorities, progress or cancellation** — Still needed: a Jobs panel, taskbar progress (ITaskbarList3), OS background priority when throttled, and proof playback drops no frames with jobs running. → §WT40
- 📋 **WT41** (deps: WT40 ⏳, WT25, WT27) **Heavy camera media (4K/8K HEVC 10-bit, RAW-derived ProRes) cannot be edited smoothly on a laptop** — Proxy editing is the standard answer, and generating proxies on the GPU encoder makes it nearly free. → §WT41
- 📋 **WT42** (deps: WT41, WT37) **Playback cannot switch between proxy and original media, per clip or globally, without relinking** — Editors cut on proxies and check or export on originals, and the switch must be one click with exact frames. → §WT42
- 📋 **WT43** (deps: WT41, WT24) **Users must decide clip by clip which media needs a proxy and when to create it** — Most people do not know which codecs are heavy, and the app can measure it. → §WT43
- 📋 **WT44** (deps: WT40 ⏳, WT25) **Clip filmstrips and bin thumbnails are not generated or cached, so the timeline shows grey boxes** — Pictures are how editors find shots, and regenerating them on every open wastes minutes. → §WT44
- 📋 **WT45** (deps: WT40 ⏳, WT27) **Audio clips show no waveform, and computing one blocks the editor for long files** — Sound editing and finding dialogue rely on seeing the waveform. → §WT45
- 📋 **WT46** (deps: WT42) **Proxies made on one machine cannot be reused by a colleague or after moving the project** — Shared storage teams create proxies once, and recreating them per machine wastes hours. → §WT46

## Block F — Timeline view: GPU drawing and navigation

- 📋 **WT55** (deps: WT47 ✅, WT44, WT45) **Clips all look alike: no thumbnails, waveform, name, speed badge, effect badge or offline state** — Editors navigate a cut by recognising pictures and shapes, not by reading names. → §WT55
- ⏳ **WT58** (deps: WT52 ✅, WT51 ✅, WT70 ✅, WT72) **The timeline cannot be used without a mouse or read by a screen reader** — Trimming and moving the focused clip by keys waits on WT70 and WT72, and an Accessibility Insights pass over the panel is still to run. → §WT58
- 📋 **WT168** (deps: WT48 ✅) **With hundreds of clips in view, one UI Automation query of the timeline takes seconds** — Every query rebuilds a peer per visible clip, so cases time out and a screen reader stalls exactly when the whole cut is shown. → §WT168
- 📋 **WT169** (deps: WT50 ✅) **Tracks cannot be resized, coloured, reordered by dragging their header, or targeted for insert** — A dense timeline needs tall lanes where the work is and short ones elsewhere, and editors sort and target tracks constantly. → §WT169
- 📋 **WT170** (deps: WT52 ✅) **The selection cannot hold edit points, keep linked audio and video together, or find clips by name, colour or tag** — Trims act on a clip's head or tail, and a selection that splits linked pairs breaks sync on every edit. → §WT170
- 📋 **WT171** (deps: WT54 ✅) **Markers cannot be dragged to a new time on the ruler or their lane** — A note placed a few frames off has to be deleted and added again, and AI findings land where their analysis put them. → §WT171
- 💭 **WT173** (deps: WT57 ✅) **The overview strip cannot show where silence or unaccepted AI suggestions are** — Dead air and pending suggestions are the next things an editor hunts for in a long programme, once something finds them. → §WT173

## Block G — Workspace, panels and viewers

- 📋 **WT59** (deps: WT1 ✅) (requires: second-monitor) **Panels cannot be docked, tabbed, floated onto a second monitor, or saved as named workspaces** — Editors arrange the screen per task (cut, sound, colour, review), and a fixed layout wastes a second monitor. → §WT59
- 📋 **WT60** (deps: WT59, WT36, WT34) **No program viewer shows the timeline at the playhead with transport controls** — The program viewer is where every edit is judged. → §WT60
- 📋 **WT61** (deps: WT60) **A media file cannot be opened in a source viewer to mark in and out before editing** — Choosing the part of a shot to use happens in the source viewer, before the timeline. → §WT61
- 📋 **WT62** (deps: WT59, WT22 ✅, WT24) **Imported media has nowhere to live: no bins, folders, list or grid views, search or metadata columns** — Projects have hundreds of files, and finding the right one quickly is a large share of editing time. → §WT62
- 📋 **WT63** (deps: WT59, WT52 ✅) **Clip, track, transition and timeline properties, including OTIO metadata, cannot be seen or edited** — OTIO carries rich metadata, and a viewer-editor must show and edit it faithfully. → §WT63
- 📋 **WT64** (deps: WT59, WT16 ✅) **Past edits cannot be seen as a list or jumped to** — Seeing what changed, and returning to a point before a bad idea, is faster than pressing undo twenty times. → §WT64
- 📋 **WT65** (deps: WT59, WT54 ✅) **Markers cannot be listed, filtered, edited in bulk or exported as review notes** — Review feedback arrives as dozens of markers, and working through them needs a list. → §WT65
- 📋 **WT67** (deps: WT5 ✅, WT59) **Preferences (cache locations, GPU choice, autosave, proxies, AI models, keymap) cannot be changed in the app** — Defaults never fit every machine, and editing JSON files by hand is not acceptable for this audience. → §WT67
- ⏳ **WT68** (deps: WT1 ✅, WT44) **Opening the app shows an empty window with no recent projects, templates or first steps** — Still needed: new timelines from presets (resolution, rate, colour space), sample timelines, thumbnails beside recent files, and the .otio association with its thumbnail handler. → §WT68
- ⏳ **WT69** (deps: WT1 ✅, WT41) **Errors, finished jobs and warnings appear as modal dialogs or not at all** — Still needed: toasts when minimised, VRAM, dropped frames, proxies and model state in the status bar, and a failing proxy job's Retry shown working in a UI case. → §WT69

## Block H — Editing: tools, trims and drag and drop

- 📋 **WT72** (deps: WT70 ✅, WT170) **Clip heads and tails cannot be trimmed, and ripple, roll, slip and slide edits do not exist** — Trimming is where an edit is refined, and the four trim types are what separates an editor from a cutter. → §WT72
- 📋 **WT73** (deps: WT70 ✅, WT24) **Media cannot be dragged from the bin or from Windows Explorer onto the timeline or the viewer** — Drag and drop is how people expect to start an edit, and Explorer drops make the first minute of use obvious. → §WT73
- 📋 **WT74** (deps: WT70 ✅, WT61) **A source clip's marked range cannot be edited into the timeline with insert, overwrite, replace or fit-to-fill** — Three-point editing with the keyboard is the fastest way to assemble, and professional editors expect it. → §WT74
- 📋 **WT75** (deps: WT72) **A marked range cannot be lifted or extracted, and selected clips cannot be ripple deleted** — Removing material cleanly, with or without closing the gap, is half of editing. → §WT75
- 📋 **WT76** (deps: WT72, WT33) **Transitions cannot be added, resized or changed between two clips** — Dissolves and wipes are part of almost every delivered cut, and OTIO stores them explicitly. → §WT76
- 📋 **WT77** (deps: WT72) **Clip speed cannot be changed, reversed or frozen, and time remapping from other tools is ignored** — Speed changes are routine in every genre, and OTIO represents them with LinearTimeWarp and FreezeFrame effects. → §WT77
- 📋 **WT78** (deps: WT70 ✅) **Clips cannot be copied and pasted within or between timelines, or pasted as attributes** — Reusing material and effect settings across a cut is constant work. → §WT78
- 📋 **WT79** (deps: WT70 ✅) **Video and its audio cannot be linked or unlinked, and clips cannot be grouped** — Keeping sync between picture and sound is the editor's constant worry, and groups protect it. → §WT79
- 📋 **WT80** (deps: WT72) **Shortcuts cannot be customised, and editors coming from Premiere, Resolve or Avid must relearn everything** — Muscle memory is the biggest switching cost, and presets for the main editors remove it. → §WT80
- 💭 **WT81** (deps: WT56 ✅) **Several angles of the same event cannot be synced and cut live as a multicam clip** — Interviews, events and podcasts are multicam, and cutting them clip by clip is slow. → §WT81
- 📋 **WT82** (deps: WT72, WT79) **Insert and ripple edits on one track push other tracks out of sync with no way to protect them** — Sync locks are what keep music, dialogue and picture together during ripple edits. → §WT82
- 📋 **WT174** (deps: WT70 ✅, winwright WW604) **Insert, swap and duplicate drags of clips have no UI case, only model tests of their edits** — The gesture that picks the edit from the key held is what an editor uses, and nothing yet proves it end to end. → §WT174

## Block I — Audio: playback, mixing and meters

- 📋 **WT83** (deps: WT27) **Audio cannot be played through a chosen output device with low latency** — The audio device is the master clock for playback, so its stability decides A/V sync. → §WT83
- 📋 **WT84** (deps: WT83, WT50 ✅) **Clip and track volume, pan and mute cannot be set, and there is no mixer** — Balancing dialogue, music and effects is required for any watchable export. → §WT84
- 📋 **WT85** (deps: WT84) **No level meters or loudness measurement show whether the mix is clipping or meets delivery specs** — Broadcast and streaming platforms reject or turn down mixes outside loudness targets. → §WT85
- 📋 **WT86** (deps: WT83, WT34) **Scrubbing the playhead is silent, so dialogue edits cannot be found by ear** — Editors find the start of a word by scrubbing audio, not by watching. → §WT86
- 📋 **WT87** (deps: WT84, WT76) **Audio clips cannot fade in or out, and cuts between audio clips click** — Hard audio cuts produce audible clicks that make an edit sound amateur. → §WT87
- 📋 **WT88** (deps: WT84, WT62) **Multichannel field recordings cannot be split into the channels an edit needs** — Interviews arrive as poly WAVs with lav, boom and mix on separate channels. → §WT88
- 💭 **WT89** (deps: WT83, WT50 ✅) **A voice-over cannot be recorded directly onto a track while the timeline plays** — Scratch narration during editing is common and otherwise needs a separate app. → §WT89

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

- 📋 **WT101** (deps: WT100 ✅, WT40 ⏳) **Models cannot be downloaded, resumed, verified, moved to another drive or removed from inside the app** — Models weigh from 50 MB to 10 GB, and an interrupted or corrupt download must never break a feature. → §WT101
- 📋 **WT102** (deps: WT101, WT67) **Nothing in the UI shows which AI features are available, which models they need and whether this PC can run them** — AI features must explain themselves before a 4 GB download, or people will not trust them. → §WT102
- ⏳ **WT103** (deps: WT99 ✅, WT106) **Nothing guarantees that media, transcripts or prompts never leave the PC** — Still needed: the AI activity view and air-gapped switch in the app, and UI cases showing each feature run (from WT106 on) recorded in the activity log. → §WT103
- 📋 **WT105** (deps: WT99 ✅, WT34) **AI inference running during playback fights the decoder and compositor for the GPU and causes dropped frames** — AI analysis is meant to run in the background while the user edits, not instead of editing. → §WT105
- 📋 **WT166** (deps: WT100 ✅) **The language model and fast transcription have no native engine, so GGUF models cannot run** — llama.cpp and whisper.cpp out of process are the fastest local engines for them, and a native crash there must never take the editor down. → §WT166

## Block L — AI-assisted editing

- 📋 **WT106** (deps: WT104 ✅, WT102, WT27) **Speech in the media cannot be transcribed with word timings, so dialogue cannot be searched or cut by text** — A transcript with word times is the base of text editing, captions, search and the assistant's understanding of the footage. → §WT106
- 📋 **WT107** (deps: WT106) **Transcripts do not say who is speaking** — Interviews and podcasts are edited by speaker, and speaker turns drive multicam switching and captions. → §WT107
- 📋 **WT108** (deps: WT106, WT75) **Footage cannot be edited by selecting and deleting words in its transcript** — Text-based editing is the fastest way to build an interview or podcast cut, and it is now an expected feature. → §WT108
- 📋 **WT109** (deps: WT108) **Filler words, long pauses and repeated takes must be found and cut by hand** — Cleaning a talking-head edit is hours of repetitive work that word timings make automatic. → §WT109
- 📋 **WT110** (deps: WT106, WT97) **Captions must be typed by hand even though a transcript exists** — Most social and accessibility deliveries need captions, and timing them is tedious. → §WT110
- 📋 **WT111** (deps: WT104 ✅, WT25) **Long recordings and delivered masters are not split into shots, so finding a cut point means scrubbing** — Shot boundaries let an editor re-cut a master and let the other AI tools reason about shots instead of frames. → §WT111
- 📋 **WT112** (deps: WT111) **Footage has no descriptive tags (shot size, subjects, location, motion), so bins cannot be filtered by content** — Logging footage by hand is a full-time assistant job that vision models now do well enough to help. → §WT112
- 📋 **WT113** (deps: WT112, WT106, WT62) **Footage cannot be searched by describing what is in it, like 'a woman laughing near a window'** — Natural-language search across hours of footage is the AI feature editors say saves the most time. → §WT113
- ⏳ **WT114** (deps: WT99 ✅, WT16 ✅, WT66 ✅, WT166) **Editing intentions cannot be expressed in plain language and turned into reviewed timeline changes** — Still needed: replies from the LLM (WT166) under the schema, a preview with Apply, and 25 of 30 scripted requests valid. → §WT114
- 💭 **WT115** (deps: WT110, WT114 ⏳) **Captions and transcripts cannot be translated into other languages** — Reaching other audiences needs subtitles in their language, and translation can run locally. → §WT115
- 📋 **WT116** (deps: WT114 ⏳, WT113) **A first assembly from a script or a list of selects must be built clip by clip** — Assembling a rough cut from transcripts is mechanical work the assistant and search can do in minutes. → §WT116
- 💭 **WT117** (deps: WT113, WT108) **Finding B-roll that matches what is being said requires browsing the whole library** — Covering interviews with relevant visuals is a constant need, and transcript plus image search can suggest it. → §WT117
- 📋 **WT118** (deps: WT112, WT91) **A 16:9 edit cannot be reframed to 9:16 or 1:1 with the subject kept in frame** — Every cut now ships vertical versions for social platforms, and reframing by hand is keyframe drudgery. → §WT118
- 📋 **WT119** (deps: WT84, WT99 ✅) **Noisy dialogue, hum and reverb cannot be cleaned inside the app** — Most location audio needs cleanup, and modern denoisers run in real time on a laptop. → §WT119
- 📋 **WT120** (deps: WT119, WT106) **Music cannot be separated from dialogue, and background music is not ducked under speech automatically** — Ducking music under voice is a step in nearly every video, and stem separation rescues mixed recordings. → §WT120
- 📋 **WT121** (deps: WT104 ✅, WT54 ✅) **Cuts cannot be placed on the beats of a music track** — Montages cut to music are common, and beat markers make them fast and precise. → §WT121
- 📋 **WT122** (deps: WT77, WT99 ✅) **Slow motion from normal-rate footage stutters, because frames are only repeated or blended** — Smooth slow motion is a common request and optical-flow models make it possible from ordinary footage. → §WT122
- 📋 **WT123** (deps: WT99 ✅, WT40 ⏳) **Low-resolution archive or phone clips look soft in a 4K timeline** — Mixed-resolution timelines are common, and Revelei already proves Real-ESRGAN runs well on this hardware. → §WT123
- 💭 **WT124** (deps: WT92, WT99 ✅) **A person cannot be separated from the background without a green screen** — Background replacement and text-behind-subject effects are popular and now feasible locally. → §WT124
- 📋 **WT125** (deps: WT112, WT92) **Faces and licence plates cannot be blurred automatically for privacy** — News, documentary and corporate videos must hide bystanders, and tracking them by hand takes hours. → §WT125
- 📋 **WT126** (deps: WT111, WT19 ✅) **Flash frames, black frames, frozen frames, out-of-focus shots and jump cuts are found only by watching** — A technical QC pass before delivery catches embarrassing errors that a tired editor misses. → §WT126
- 📋 **WT127** (deps: WT114 ⏳, WT21 ✅) **Chapters, a description and a summary of a cut, or of what changed between versions, must be written by hand** — Publishing needs chapters and descriptions, and review needs a readable change summary. → §WT127
- 💭 **WT128** (deps: WT93, WT112) **Shots from different cameras cannot be matched in colour automatically** — Multicam and mixed-camera edits need consistent colour, and a first automatic match saves most of the work. → §WT128
- 💭 **WT129** (deps: WT81, WT107) **Multicam podcasts must be cut angle by angle even though the speaker is known at every moment** — Speaker diarisation can produce a watchable first cut of a multicam conversation automatically. → §WT129
- 💭 **WT130** (deps: WT99 ✅, WT84) **A scratch narration cannot be produced from text while the real voice-over is not recorded yet** — Editors time a cut against a narration long before the final recording exists. → §WT130
- 📋 **WT131** (deps: WT27, WT79) **Separately recorded audio cannot be synced to camera audio automatically** — Double-system sound is standard on productions, and clapping by hand is slow. → §WT131

## Block M — Video export and interchange

- 📋 **WT132** (deps: WT23) (requires: three-gpu-vendors) **No decision says which encoders write H.264, HEVC, AV1, ProRes and DNxHR, or how hardware encoders are used** — Hardware encoders make export fast but differ per vendor, and pro intermediates need FFmpeg, so the matrix must be measured. → §WT132
- 📋 **WT133** (deps: WT132, WT31, WT84) **A timeline cannot be rendered to a video file at all** — Exporting a video is the end goal of the application, and it must equal what the viewer showed. → §WT133
- 📋 **WT134** (deps: WT133) **Users must know codecs and bitrates to export, with no presets for YouTube, Instagram, TikTok or mastering** — Most people want 'good for YouTube', and professionals want exact masters; presets serve both. → §WT134
- 📋 **WT135** (deps: WT134, WT40 ⏳) **Several exports (versions, aspect ratios, masters) cannot be queued and left to run** — Deliveries are batches: master, web, vertical and captions, often overnight. → §WT135
- 📋 **WT136** (deps: WT133, WT36) (requires: hdr-display) **HDR timelines cannot be exported as HDR10 or HLG with correct metadata** — Phones record HDR now, and an SDR-only export flattens footage people expect to look bright. → §WT136
- 💭 **WT137** (deps: WT133) **Exporting an unchanged section re-encodes it, wasting time and quality** — Long-GOP sources cut without effects can be passed through, making some exports ten times faster. → §WT137
- 📋 **WT138** (deps: WT15 ✅, WT14 ✅, WT133) **The edited timeline cannot be handed back to other tools as OTIO, EDL or FCPXML with its media** — OTIO is an interchange format first, and round-tripping to Resolve, Premiere or a finishing house is the professional use. → §WT138
- 📋 **WT139** (deps: WT133) **Single frames, contact sheets and short animated previews cannot be exported** — Stills for thumbnails, review sheets and GIF/WebP previews are daily needs around an edit. → §WT139
- 📋 **WT140** (deps: WT133, WT85) **Nobody checks an export after it is written, so a broken or out-of-spec file is delivered** — A corrupt or wrong-loudness delivery is costly, and checking it automatically costs seconds. → §WT140

## Block N — Performance, delivery and the public face

- 📋 **WT141** (deps: WT34, WT47 ✅) (requires: three-gpu-vendors) **No measured budgets say how fast opening, scrubbing, playback and export must be, or on which machines** — Performance claims are only meaningful against named machines and numbers, and regressions hide without them. → §WT141
- 📋 **WT142** (deps: WT141, WT35) **Memory and VRAM use grow without bounds on long sessions with many media files** — Editors leave the app open all day, and a slow leak shows up as a crash in the afternoon. → §WT142
- 📋 **WT143** (deps: WT1 ✅, WT133) **There is no installer, file association or thumbnail preview for .otio files in Explorer** — People install from a single download, and seeing a timeline's thumbnail in Explorer makes the file type feel native. → §WT143
- 📋 **WT144** (deps: WT143) **Users are not told about new versions and cannot update from inside the app** — Media apps ship decoder and driver workarounds often, and stale versions cause support load. → §WT144
- 📋 **WT145** (deps: WT143, visibility VS58) (requires: store-account) **Every first download meets a SmartScreen warning, and the Store, where creative apps are looked for, has no WinTimeline** — The Store signs the MSIX itself, and claude-tray tests that path first, so this follows what that test learns. → §WT145
- 📋 **WT146** (deps: WT133) **There is no README or site saying what WinTimeline does, for whom, and with which hardware** — A visitor decides in seconds whether the app is for them, and the sibling apps show the same family promise. → §WT146
- 📋 **WT147** (deps: WT133, WT15 ✅) **Timelines cannot be validated, converted, rendered or analysed from the command line or a script** — Pipelines (render farms, dailies, CI for editorial) drive OTIO headless, and the CLI also powers the soak and bench tests. → §WT147
- 💭 **WT148** (deps: WT147, WT114 ⏳) **Repetitive pipeline tasks inside an open project cannot be automated by users** — OTIO users are often pipeline TDs who script everything, and the command pipeline makes safe scripting possible. → §WT148

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

## Done when — WT9

- **The checks are green on GitHub and required on main** After a push, the tests
  workflow's build-and-test and roadmap-lint jobs pass on GitHub, and branch protection
  on main lists both as required checks.

## Done when — WT10

- **A fresh session runs a case through the winwright plugin** A Claude Code session
  started in this repository after the marketplace is trusted lists the winwright plugin
  and runs one case from cases\window.cases.json with it.

## Done when — WT155

- **The tests workflow is green on a GitHub runner** The first push after this fix shows
  the build-and-test job passing, with the hardware render tests skipped for want of a
  GPU and the test-results artifact uploaded.

## Done when — WT40

- **A Jobs panel and the taskbar show every job's state and progress** A UI case queues
  test jobs, then reads each one's state and percentage in the panel, pauses and resumes
  one there, and checks that the taskbar progress matches the scheduler's overall
  progress.
- **Playback with 200 background jobs running drops no frames** Once WT34's clock
  exists, a minute of playback with the scheduler throttled and 200 proxy jobs queued
  reports zero dropped frames, and the jobs' threads run at background priority while it
  plays.

## Done when — WT68

- **New timelines start from presets and samples are a click away** The welcome page
  offers presets (1080p25, 2160p23.976 and others with their colour space) and opens
  bundled sample timelines; a UI case creates a 2160p23.976 timeline and reads its rate
  back.
- **Recent files show thumbnails and Explorer opens .otio in the app** Each recent file
  shows a frame from its timeline, and once Block N's installer associates .otio, .otioz
  and .otiod, the jump list lists them and a double click in Explorer opens the app.

## Done when — WT69

- **A failing proxy job's InfoBar Retry works in a UI case** Once WT41 makes proxies, a
  UI case points a proxy at media that fails, reads the error InfoBar, fixes the media
  and presses Retry, then reads the completion notice. While the app is minimised, the
  same completion arrives as a Windows toast.
- **The status bar shows VRAM, dropped frames, proxies and model state** VRAM comes from
  DXGI QueryVideoMemoryInfo, dropped frames from WT34's clock, active proxies from WT42
  and model state from Block L; a UI case reads each item while a timeline plays.

## Done when — WT100

- **The app refreshes a signed catalog from the project's releases** A release carries
  models.json with a detached signature; the app verifies it against a public key it
  ships with, takes it only when its catalog_version is higher, and keeps the shipped
  catalog when the signature or the download fails. A test feeds it a tampered file and
  sees it refused.

## Done when — WT103

- **The app shows the AI activity log and an air-gapped switch** Help > AI activity
  lists AiActivity's records newest first, and a setting turns downloads off; a UI case
  runs transcription (WT106) on a fixture, reads its record in the view, then flips
  air-gapped and sees a model download refused with the notice saying why.

## Done when — WT114

- **25 of 30 scripted requests become valid batches the user previews** With WT166's
  llama.cpp host and the measured model, an eval of 30 requests on fixtures (pauses,
  markers at mentions, a 30-second version) yields valid proposals for at least 25; the
  palette's ? opens a preview with the diff overlay and Apply, and a UI case applies one
  and undoes it.

## Done when — WT58

- **A clip is trimmed and moved with keys only** A UI case focuses a clip with Tab,
  trims its tail and moves it a few frames using keys alone, then reads its new range,
  and an Accessibility Insights (AxeWindows) run over the panel reports no errors.

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

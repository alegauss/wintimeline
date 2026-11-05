# Set aside

## Block A — Project foundation

- ⏸ **WT9** (deps: WT8 ✅) **No CI builds the solution or runs the headless tests on each push** — set aside (Waits for the author to push and mark the build-and-test and roadmap-lint checks required on main.): The workflow has never run on GitHub, and its checks are not yet required on pull requests. → §WT9
- ⏸ **WT10** (deps: WT8 ✅) **The project skill describes another repository's npm gates and RG prefix, and the winwright plugin is not enabled** — set aside (Needs a new session that loads the winwright plugin.): No session has yet loaded the winwright plugin here and run a case through it. → §WT10
- ⏸ **WT155** (deps: WT3 ✅) **A GPU-less desk's Basic Render Driver is taken for hardware, so CI's render tests fail and a VM skips WARP** — set aside (Needs the author to push so CI runs it.): The fix has not yet run on a GitHub runner. → §WT155

## Block B — OTIO document model and file formats

## Block C — Media probing and GPU decoding

## Block D — GPU compositing and playback engine

## Block E — Proxies, thumbnails, waveforms and background jobs

## Block F — Timeline view: GPU drawing and navigation

- ⏸ **WT167** (deps: WT47 ✅) **Nobody has measured the 3,000-clip timeline scrolling at refresh rate on the reference laptop** — set aside (Needs a person on the reference laptop.): The draw-time budget only counts on the hardware it is promised for, and neither WARP nor this desk's GPU is that machine. → §WT167
- ⏸ **WT172** (deps: WT54 ✅) **Markers from a DaVinci Resolve export have never been checked against the frames and colours Resolve shows** — set aside (It needs a person with Resolve.): Resolve is the editor most likely to hand this app an OTIO file, and its marker colours live in its own metadata. → §WT172
- ⏸ **WT168** (deps: WT48 ✅) **With hundreds of clips in view, one UI Automation query of the timeline takes seconds** — set aside (Waits on choosing a native UIA provider.): Every query rebuilds a peer per visible clip, so cases time out and a screen reader stalls exactly when the whole cut is shown. → §WT168

## Block G — Workspace, panels and viewers

## Block H — Editing: tools, trims and drag and drop

## Block I — Audio: playback, mixing and meters

## Block J — Effects, titles, keyframes and colour

## Block K — Local AI platform: models, runtimes and hardware fit

- ⏸ **WT100** (deps: WT99 ✅) **There is no catalog saying which models exist, their licence, size, hardware needs and download source** — set aside (Waits on a signing key from the maintainer.): Still needed: a signed catalog refreshed from the project's GitHub releases. → §WT100

## Block L — AI-assisted editing

## Block M — Video export and interchange

## Block N — Performance, delivery and the public face


# Command line

WinTimeline starts like any Windows app with no arguments. These flags exist so an automated UI
case can reach a state without a file dialog, and they work for anyone.

```
WinTimeline.exe [--open <file>] [--models <folder>] [--adapter <index-or-name>|warp]
                [--log-level trace|debug|info|warning|error]
```

Each flag takes its value after a space or an equals sign (`--log-level debug` or
`--log-level=debug`) and may be given once. An unknown flag, a missing value or a value outside
the list is refused: WinTimeline says which one in a message box and exits with code 2.

| Flag | What it does |
| --- | --- |
| `--open <file>` | Opens this project or media file at start instead of the empty window. |
| `--models <folder>` | Reads the AI models from this folder instead of the default one. |
| `--adapter <index-or-name>\|warp` | Runs on this GPU instead of the one WinTimeline picks, which is the first hardware GPU in Windows' high-performance order (the discrete one on a hybrid laptop). Give its index in that order or a part of its name (`1`, `NVIDIA`); `warp` runs on the software rasteriser. A value that matches no GPU, or more than one, is refused with the list of GPUs this PC has. The status bar names the GPU in use. |
| `--log-level trace\|debug\|info\|warning\|error` | How much the log records; `info` unless given. |

The flags are parsed from the first version; what each one changes arrives with the feature it
belongs to. `--adapter` takes effect now; `--open`, `--models` and `--log-level` arrive with
opening files, the model library and the log.

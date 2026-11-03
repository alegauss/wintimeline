# Command line

WinTimeline starts like any Windows app with no arguments. These flags exist so an automated UI
case can reach a state without a file dialog, and they work for anyone.

```
WinTimeline.exe [--open <file>] [--models <folder>] [--adapter <index-or-name>]
                [--log-level trace|debug|info|warning|error]
```

Each flag takes its value after a space or an equals sign (`--log-level debug` or
`--log-level=debug`) and may be given once. An unknown flag, a missing value or a value outside
the list is refused: WinTimeline says which one in a message box and exits with code 2.

| Flag | What it does |
| --- | --- |
| `--open <file>` | Opens this project or media file at start instead of the empty window. |
| `--models <folder>` | Reads the AI models from this folder instead of the default one. |
| `--adapter <index-or-name>` | Runs on this GPU, given by its index or by a part of its name (`1`, `NVIDIA`), instead of the one WinTimeline picks. |
| `--log-level trace\|debug\|info\|warning\|error` | How much the log records; `info` unless given. |

The flags are parsed from the first version; what each one changes arrives with the feature it
belongs to (opening files, the model library, the GPU device, the log).

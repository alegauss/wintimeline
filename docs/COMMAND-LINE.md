# Command line

WinTimeline starts like any Windows app with no arguments. These flags exist so an automated UI
case can reach a state without a file dialog, and they work for anyone.

```
WinTimeline.exe [--open <file>] [--models <folder>] [--adapter <index-or-name>|warp]
                [--log-level trace|debug|info|warning|error] [--crash-test managed|native]
```

Each flag takes its value after a space or an equals sign (`--log-level debug` or
`--log-level=debug`) and may be given once. An unknown flag, a missing value or a value outside
the list is refused: WinTimeline says which one in a message box and exits with code 2.

| Flag | What it does |
| --- | --- |
| `--open <file>` | Opens this OpenTimelineIO file (.otio), or bundle with its media (.otioz, or an .otiod folder), at start instead of the empty window. A bundle's media is extracted only when it is needed, under `bundles` in the data folder. A file that is missing or not a timeline is named in the status bar and the window stays empty. |
| `--models <folder>` | Reads the AI models from this folder instead of the default one. |
| `--adapter <index-or-name>\|warp` | Runs on this GPU instead of the one WinTimeline picks, which is the first hardware GPU in Windows' high-performance order (the discrete one on a hybrid laptop). Give its index in that order or a part of its name (`1`, `NVIDIA`); `warp` runs on the software rasteriser. A value that matches no GPU, or more than one, is refused with the list of GPUs this PC has. The status bar names the GPU in use. |
| `--log-level trace\|debug\|info\|warning\|error` | How much the log records in `logs\wintimeline.jsonl` under the data folder; `info` unless given. |
| `--crash-test managed\|native` | Crashes on purpose once the window is up, to test the crash handling: `managed` throws an error the app catches and offers to restart from; `native` faults inside native code, which ends the process, and the next start says the last run crashed. |

## Where WinTimeline keeps its settings

Settings live in `settings.json` under `%LOCALAPPDATA%\WinTimeline`. Set the `WINTIMELINE_DATA`
environment variable to a folder to keep them there instead, as a portable copy or a test does.
A settings file that cannot be read never stops a start: WinTimeline uses the defaults, keeps the
file as `settings.json.broken`, and says so in the status bar. When `--adapter` is not given, the
GPU saved in the settings is used; a saved GPU that is no longer in the PC is passed over with a
note in the status bar.

## When each flag takes effect

The flags are parsed from the first version; what each one changes arrives with the feature it
belongs to. `--open`, `--adapter`, `--log-level` and `--crash-test` take effect now; `--models`
arrives with the model library.

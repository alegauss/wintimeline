# OTIO spike: a C# port or the C++ reference library (WT11)

**Verdict: WinTimeline reads and writes .otio with a managed C# port of the schema.** The C++
library stays out of the app and serves as the test oracle.

Both paths were measured on the same files, on one desk (Intel Core i7-14700, .NET 10, MSVC
14.51, OTIO 0.18.1):

- **Native**: OTIO's C++ core built with CMake, and a C ABI shim (`native/otio_shim.cpp`, five
  functions) called through `LibraryImport` and a `SafeHandle`. This is how the app would have
  to call it.
- **Managed**: `OtioSpike/Port/`, a hand-written C# model of the core schemas. It reads with
  `System.Text.Json` and writes JSON laid out the way the reference's rapidjson writer lays it
  out.

## Fidelity: the port's output against the reference's

`OtioSpike compare` loads each file both ways, writes it back both ways and compares the two
outputs. The files are the 13 `.otio` files in OTIO's `tests/sample_data` and three generated
stress files (`generate_stress.py`).

| Files | Byte-equal output | Equal output | Clip count and duration agree |
| --- | --- | --- | --- |
| 11 of 13 corpus files, all 3 stress files | yes | yes | yes |
| `big_int.otio`, `effects.otio` | no | yes, numbers within 3 ulp | yes |

The two files that differ are cases where the reference is wrong. It parses without rapidjson's
`kParseFullPrecisionFlag`, so some doubles move by an ulp or more as they are read:
`170141183460469231731687303715884105728` (2^127) comes back as `1.7014118346046935e38`, and
`1.1899998188018799` comes back as `1.18999981880188`. The port keeps the file's exact value.

The unknown schemas in `nested-unknown.otio` (`MyStudio.Note.3`, and a `MyStudio.Reply.1` nested
inside it) come back unchanged from both paths. Both write them with sorted keys, because the
reference's `AnyDictionary` is a `std::map`.

## Speed and memory

`OtioSpike time native|managed`, one path per process. A load includes walking every clip, and
each figure is the median of 11 runs after a warm-up. Memory is what the loaded file keeps after
a full collection: the growth in private bytes on the native side, and live managed bytes on the
managed side.

| File | Clips | Native load | Managed load | Native write | Managed write | Native held | Managed held |
| --- | --- | --- | --- | --- | --- | --- | --- |
| effects.otio (356 KB) | 37 | 5.6 ms | 11.1 ms | 2.0 ms | 7.8 ms | 1.4 MB | 0.5 MB |
| premiere_example.otio | 13 | 1.9 ms | 2.9 ms | 0.4 ms | 1.8 ms | 0.3 MB | 0.2 MB |
| feature-3000.otio (8.7 MB) | 3,000 | 109 ms | 68 ms | 32 ms | 104 ms | 25 MB | 6.7 MB |
| feature-10000.otio (29 MB) | 10,000 | 391 ms | 238 ms | 114 ms | 257 ms | 88 MB | 22.5 MB |

On a feature-length timeline the port loads about 1.6 times faster and holds about a quarter of
the memory. The reference writes 2 to 3 times faster. The spike writer builds its output in a
`StringBuilder` through closures, and a writer that streams to a `Utf8JsonWriter`-style buffer is
the obvious fix. A save runs in the background in any case.

## Why not the native core

- **Every operation costs a C function and a P/Invoke.** C# cannot call C++ classes,
  `Retainer<T>` or `std::string`, so each thing the editor asks (children, ranges, metadata,
  every setter) needs its own `extern "C"` function and its own marshalling. The spike's five
  functions only read, count, measure and write.
- **Two object graphs.** The editor needs change notifications and undo (WT12, WT16). With the
  native core, every model object would need a managed wrapper kept in step with a C++ object
  that has its own reference count.
- **Toolchain.** The native path needs Visual Studio's C++ workload, CMake, Ninja and two git
  submodules on every desk and in CI. The core built in 22 s here, and the result ships three
  DLLs plus the VC++ runtime.
- **AOT and trimming.** The port uses no reflection: `JsonDocument` and explicit constructors.

## What the port costs, and what is left

- **The edit algorithms are C++ only.** `algo/editAlgorithm.cpp` (835 lines: overwrite, insert,
  slice, slip, slide, ripple, roll, fill, remove) is rewritten in C# by the editing lines that
  own each operation (WT70, WT72, WT75). The reference library is the oracle their tests compare
  against.
- **Schema upgrades are kept by hand.** 0.18.1 registers two upgrade functions (Clip 1 to 2,
  Marker 1 to 2) and four aliases (Sequence, Filler, SerializeableCollection). A new OTIO
  release means reading its `typeRegistry.cpp`.
- **Not yet ported**: `ImageSequenceReference` and `TimeTransform` inside items. Nothing in the
  corpus exercises them. They belong to the model (WT12).
- **System.Text.Json refuses the bare `NaN`, `Inf` and `Infinity` that rapidjson accepts**
  (`big_int.otio` has them). The port rewrites them in one pass over the bytes before parsing.
- **The stress files are generated, not real.** No real feature timeline could be committed.
  They are written by the reference library itself and seeded, so every run measures the same
  bytes.

## Re-running it

```
python -m venv .cache\venv
.cache\venv\Scripts\pip install opentimelineio==0.18.1 cmake ninja
git clone --depth 1 --branch v0.18.1 https://github.com/AcademySoftwareFoundation/OpenTimelineIO.git .cache\otio
git -C .cache\otio submodule update --init --depth 1 src/deps/rapidjson src/deps/Imath
.cache\venv\Scripts\python spike\otio\generate_stress.py .cache\otio-stress
set PATH=%CD%\.cache\venv\Scripts;%PATH%
spike\otio\native\build-shim.cmd
dotnet build spike\otio\OtioSpike -c Release
spike\otio\OtioSpike\bin\Release\net10.0\OtioSpike.exe compare .cache\otio\tests\sample_data\*.otio .cache\otio-stress\*.otio
spike\otio\OtioSpike\bin\Release\net10.0\OtioSpike.exe time managed .cache\otio-stress\feature-10000.otio
```

Run `compare` with each path spelled out in PowerShell, which does not expand wildcards for an
exe.

# Sidecar storage spike (WT22)

Where should the project sidecar keep its heaviest state? An embedding index of tens of thousands of vectors is the worst case. This spike writes and reads 50,000 vectors of 384 float32 (73.2 MiB of raw floats) three ways:

```
dotnet run -c Release --project spike/sidecar/SidecarSpike -- [count] [dimensions]
```

Measured on 2026-11-04 on the development desktop (Release build; best of three reads):

| Store                            | Write   | Read    | On disk   |
|----------------------------------|---------|---------|-----------|
| One JSON file of float arrays    | 2330 ms | 1463 ms | 195.6 MiB |
| JSON index + raw `.f32` beside it | 373 ms  | 72 ms   | 74.2 MiB  |
| SQLite, one BLOB per vector (WAL) | 2300 ms | 301 ms  | 99.2 MiB  |

**Decision: a folder, `cut.otio.wtproj/`, of JSON documents, with vectors as raw float32 next to a JSON index.**

- **Speed and size.** The JSON-plus-raw-floats store reads 4× faster than SQLite and 20× faster than JSON, and is the smallest on disk.
- **No dependencies.** It needs only the BCL, so it lives in `WinTimeline.Model`, which the architecture tests keep free of packages. SQLite would bring a native library, and the `e_sqlite3` that `Microsoft.Data.Sqlite` 10.0.0 bundles carries a high-severity advisory (GHSA-2m69-gcr7-jv3q).
- **Readable.** Each file is readable and diffable on its own.
- **Crash safety.** A file is written beside its target and moved over it, so a crash mid-save leaves the previous file. The vectors are written before their index, and a load refuses vectors that disagree with the index's count.

What SQLite would have added is queries and partial updates. Neither is needed yet: an embedding index is recomputed whole when its model changes. If vector search needs an on-disk index later, it can be a separate file in the same folder.

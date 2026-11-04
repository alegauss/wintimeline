// WT22: how long the sidecar takes to write and read back an embedding index, three ways.
//   dotnet run -c Release --project spike/sidecar/SidecarSpike -- [count] [dimensions]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

int count = args.Length > 0 ? int.Parse(args[0]) : 50_000;
int dims = args.Length > 1 ? int.Parse(args[1]) : 384;
string root = Path.Combine(Path.GetTempPath(), "wt-sidecar-spike");
if (Directory.Exists(root))
{
    Directory.Delete(root, recursive: true);
}

Directory.CreateDirectory(root);
Random random = new(22);
string[] keys = [.. Enumerable.Range(0, count).Select(i => $"clip{i:00000}@{i * 24}")];
float[][] vectors = [.. Enumerable.Range(0, count).Select(_ => Enumerable.Range(0, dims).Select(_ => (float)random.NextDouble()).ToArray())];
Console.WriteLine($"{count} vectors of {dims} float32 ({count * dims * 4 / 1048576.0:F1} MiB raw)");

Measure("json", Path.Combine(root, "vectors.json"),
    path => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new { keys, vectors })),
    path =>
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("vectors").GetArrayLength();
    });

Measure("json+f32", Path.Combine(root, "vectors"),
    path =>
    {
        File.WriteAllBytes(path + ".json", JsonSerializer.SerializeToUtf8Bytes(new { dims, keys }));
        using FileStream f = File.Create(path + ".f32");
        foreach (float[] v in vectors)
        {
            f.Write(MemoryMarshal.AsBytes(v.AsSpan()));
        }
    },
    path =>
    {
        using JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(path + ".json"));
        int n = index.RootElement.GetProperty("keys").GetArrayLength();
        byte[] raw = File.ReadAllBytes(path + ".f32");
        float[] all = MemoryMarshal.Cast<byte, float>(raw).ToArray();
        return all.Length / dims == n ? n : -1;
    });

Measure("sqlite", Path.Combine(root, "vectors.db"),
    path =>
    {
        using SqliteConnection db = new($"Data Source={path};Pooling=False");
        db.Open();
        using (SqliteCommand create = db.CreateCommand())
        {
            create.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE v (key TEXT PRIMARY KEY, vec BLOB NOT NULL);";
            create.ExecuteNonQuery();
        }

        using SqliteTransaction tx = db.BeginTransaction();
        using SqliteCommand insert = db.CreateCommand();
        insert.CommandText = "INSERT INTO v VALUES ($k, $v)";
        SqliteParameter k = insert.Parameters.Add("$k", SqliteType.Text);
        SqliteParameter b = insert.Parameters.Add("$v", SqliteType.Blob);
        for (int i = 0; i < count; i++)
        {
            k.Value = keys[i];
            b.Value = MemoryMarshal.AsBytes(vectors[i].AsSpan()).ToArray();
            insert.ExecuteNonQuery();
        }

        tx.Commit();
        using SqliteCommand checkpoint = db.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        checkpoint.ExecuteNonQuery();
    },
    path =>
    {
        using SqliteConnection db = new($"Data Source={path};Mode=ReadOnly;Pooling=False");
        db.Open();
        using SqliteCommand select = db.CreateCommand();
        select.CommandText = "SELECT key, vec FROM v";
        using SqliteDataReader reader = select.ExecuteReader();
        int n = 0;
        while (reader.Read())
        {
            _ = reader.GetString(0);
            _ = MemoryMarshal.Cast<byte, float>((byte[])reader.GetValue(1)).ToArray();
            n++;
        }

        return n;
    });

void Measure(string name, string path, Action<string> write, Func<string, int> read)
{
    Stopwatch w = Stopwatch.StartNew();
    write(path);
    w.Stop();
    long bytes = Directory.EnumerateFiles(root, Path.GetFileName(path) + "*").Sum(f => new FileInfo(f).Length);
    double best = double.MaxValue;
    int got = 0;
    for (int run = 0; run < 3; run++)
    {
        Stopwatch r = Stopwatch.StartNew();
        got = read(path);
        best = Math.Min(best, r.Elapsed.TotalMilliseconds);
    }

    Console.WriteLine($"{name,-9} write {w.Elapsed.TotalMilliseconds,7:F0} ms  read {best,7:F0} ms  size {bytes / 1048576.0,7:F1} MiB  ({got} read back)");
}

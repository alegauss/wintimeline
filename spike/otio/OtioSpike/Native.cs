using System.Runtime.InteropServices;

namespace OtioSpike;

/// <summary>A timeline the C++ library holds, released through the shim when disposed.</summary>
internal sealed class NativeTimeline : SafeHandle
{
    public NativeTimeline()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>Reads a .otio file through otio_shim.dll.</summary>
    public static NativeTimeline Read(string path)
    {
        NativeTimeline timeline = Shim.ReadFile(path, out IntPtr error);
        if (timeline.IsInvalid)
        {
            string message = Marshal.PtrToStringUTF8(error) ?? "unknown error";
            Shim.FreeString(error);
            timeline.Dispose();
            throw new InvalidDataException($"{path}: {message}");
        }

        return timeline;
    }

    public long CountClips() => Shim.CountClips(this);

    public double DurationSeconds() => Shim.DurationSeconds(this);

    public string ToJson()
    {
        IntPtr text = Shim.ToJson(this, 4);
        try
        {
            return Marshal.PtrToStringUTF8(text)!;
        }
        finally
        {
            Shim.FreeString(text);
        }
    }

    protected override bool ReleaseHandle()
    {
        Shim.Release(handle);
        return true;
    }
}

/// <summary>The C ABI of ../native/otio_shim.cpp: one line here per extern "C" function there.</summary>
internal static partial class Shim
{
    private const string Library = "otio_shim";

    [LibraryImport(Library, EntryPoint = "otio_read_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeTimeline ReadFile(string path, out IntPtr error);

    [LibraryImport(Library, EntryPoint = "otio_count_clips")]
    internal static partial long CountClips(NativeTimeline timeline);

    [LibraryImport(Library, EntryPoint = "otio_duration_seconds")]
    internal static partial double DurationSeconds(NativeTimeline timeline);

    [LibraryImport(Library, EntryPoint = "otio_to_json")]
    internal static partial IntPtr ToJson(NativeTimeline timeline, int indent);

    [LibraryImport(Library, EntryPoint = "otio_free_string")]
    internal static partial void FreeString(IntPtr text);

    [LibraryImport(Library, EntryPoint = "otio_release")]
    internal static partial void Release(IntPtr handle);
}

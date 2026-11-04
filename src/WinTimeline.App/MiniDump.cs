using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinTimeline.App;

/// <summary>
/// Writes a minidump of this process (WT7), for a managed exception that is about to end it.
///
/// <para>A native crash cannot be dumped from inside: .NET fail-fasts on an access violation in
/// native code, which skips every handler and unhandled-exception filter, and the runtime's own
/// createdump only reads its switches from the environment the process started with. Windows
/// Error Reporting keeps local dumps only under HKLM, so the installer registers that (WT143).</para>
/// </summary>
public static class MiniDump
{
    private const uint MiniDumpWithThreadInfo = 0x1000;
    private const uint MiniDumpWithIndirectlyReferencedMemory = 0x40;

    /// <summary>Writes a dump into a folder now.</summary>
    /// <param name="folder">Where; created when missing.</param>
    /// <returns>The dump's path, or null when none could be written.</returns>
    public static string? Write(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"wintimeline-{Environment.ProcessId}-{DateTime.Now:yyyyMMdd-HHmmss}.dmp"));
        try
        {
            Directory.CreateDirectory(folder);
            using FileStream file = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using Process self = Process.GetCurrentProcess();
            if (MiniDumpWriteDump(self.Handle, (uint)Environment.ProcessId, file.SafeFileHandle, MiniDumpWithThreadInfo | MiniDumpWithIndirectlyReferencedMemory, 0, 0, 0))
            {
                return path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        File.Delete(path);
        return null;
    }

    /// <summary>
    /// Faults inside native code, as a decoder or a driver would: what <c>--crash-test native</c>
    /// does. Windows' fault dialog is turned off first, for this deliberate crash only, so the
    /// process ends at once instead of waiting on a desk for somebody to close the dialog.
    /// </summary>
    public static void Fault()
    {
        const uint SemNoGpFaultErrorBox = 0x0002;
        _ = SetErrorMode(SemNoGpFaultErrorBox); // the previous mode is not needed: the process is about to end

        memset(0, 0, 16);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(nint process, uint processId, SafeFileHandle file, uint dumpType, nint exception, nint userStream, nint callback);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
#pragma warning disable SA1300, IDE1006 // The C runtime's own name.
    private static extern nint memset(nint destination, int value, nuint count);
#pragma warning restore SA1300, IDE1006
}

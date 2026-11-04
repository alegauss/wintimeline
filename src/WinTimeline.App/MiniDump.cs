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
    private const uint MiniDumpNormal = 0;

    /// <summary>Writes a dump into a folder now.</summary>
    /// <param name="folder">Where; created when missing.</param>
    /// <returns>The dump's path, or null when none could be written.</returns>
    public static string? Write(string folder) => Write(folder, out _);

    /// <summary>
    /// Writes a dump into a folder now, saying why when it could not. The richer dump (thread info
    /// and the memory pointers reach) is tried first; a process dumping itself can fail it with a
    /// partial copy, as the VMware guest did, so a plain minidump is tried before giving up.
    /// </summary>
    /// <param name="folder">Where; created when missing.</param>
    /// <param name="error">Why no dump was written, or null when one was.</param>
    /// <returns>The dump's path, or null when none could be written.</returns>
    public static string? Write(string folder, out string? error)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"wintimeline-{Environment.ProcessId}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.dmp"));
        List<string> failures = [];
        try
        {
            Directory.CreateDirectory(folder);
            using Process self = Process.GetCurrentProcess();
            foreach (uint type in (ReadOnlySpan<uint>)[MiniDumpWithThreadInfo | MiniDumpWithIndirectlyReferencedMemory, MiniDumpNormal])
            {
                using (FileStream file = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    if (MiniDumpWriteDump(self.Handle, (uint)Environment.ProcessId, file.SafeFileHandle, type, 0, 0, 0))
                    {
                        error = null;
                        return path;
                    }

                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"type 0x{type:X}: {Marshal.GetLastPInvokeErrorMessage()} (0x{Marshal.GetLastPInvokeError():X8})"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(ex.Message);
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add("the empty file could not be removed: " + ex.Message);
        }

        error = string.Join("; ", failures);
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

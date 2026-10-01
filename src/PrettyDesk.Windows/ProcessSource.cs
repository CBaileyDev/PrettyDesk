using PrettyDesk.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace PrettyDesk.Windows;

/// <summary>
/// Process enumeration through a Toolhelp snapshot (FR-DET-2). A snapshot needs no process handle at all, so it cannot
/// disturb anti-cheat (FR-DET-3).
/// </summary>
public sealed class ProcessSource : IProcessSource
{
    public unsafe IReadOnlyList<ProcessInfo> Snapshot()
    {
        var processes = new List<ProcessInfo>(256);
        var snapshot = PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsNull || (nint)snapshot.Value == -1)
        {
            return processes;
        }

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            if (!PInvoke.Process32FirstW(snapshot, &entry))
            {
                return processes;
            }

            do
            {
                processes.Add(new ProcessInfo((int)entry.th32ProcessID, ExeName(entry), (int)entry.th32ParentProcessID));
            }
            while (PInvoke.Process32NextW(snapshot, &entry));
        }
        finally
        {
            PInvoke.CloseHandle(snapshot);
        }

        return processes;
    }

    private static string ExeName(in PROCESSENTRY32W entry)
    {
        var span = entry.szExeFile.AsSpan();
        var end = span.IndexOf('\0');
        return new string(end >= 0 ? span[..end] : span);
    }
}

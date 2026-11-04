using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptEditor;

internal static class ProcessTree
{
    private const int MaxDepth = 16;
    private const uint SnapshotProcesses = 0x2;

    /// <summary>
    /// Returns the IDs of the running processes that launched <paramref name="processId" />, nearest first. The walk
    /// stops at a parent that has exited or whose ID was reused by a process started after its child.
    /// </summary>
    internal static List<int> Ancestors(int processId)
    {
        var ancestors = new List<int>();
        var parents = ParentIds();
        var child = processId;
        var childStart = StartTime(child);
        while (ancestors.Count < MaxDepth && parents.TryGetValue(child, out var parent)
               && parent != 0 && parent != processId && !ancestors.Contains(parent))
        {
            var parentStart = StartTime(parent);
            if (parentStart is null || childStart is not null && parentStart > childStart)
                break;
            ancestors.Add(parent);
            child = parent;
            childStart = parentStart;
        }
        return ancestors;
    }

    private static Dictionary<int, int> ParentIds()
    {
        var parents = new Dictionary<int, int>();
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid)
            return parents;
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
            parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        return parents;
    }

    private static DateTime? StartTime(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception
                                      or NotSupportedException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);
}

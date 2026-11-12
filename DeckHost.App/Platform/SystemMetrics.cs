using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeckHost.App.Platform;

/// <summary>
/// System readings for the System Info action. CPU usage is measured between calls, so the
/// first reading after start is 0. Windows, Linux and macOS are supported; elsewhere values are null.
/// </summary>
internal static class SystemMetrics
{
    private static readonly Lock CpuLock = new();
    private static (ulong Idle, ulong Total)? s_lastCpuSample;
    private static double s_lastCpuUsage;
    private static long s_lastCpuSampleTicks;

    /// <summary>CPU usage 0..1 across all cores.</summary>
    public static double? CpuUsage()
    {
        lock (CpuLock)
        {
            // Several keys can show CPU at once; sample at most twice a second.
            if (s_lastCpuSample is not null && Environment.TickCount64 - s_lastCpuSampleTicks < 500)
            {
                return s_lastCpuUsage;
            }

            var sample = ReadCpuTimes();
            if (sample is null)
            {
                return null;
            }

            if (s_lastCpuSample is { } previous)
            {
                var total = sample.Value.Total - previous.Total;
                var idle = sample.Value.Idle - previous.Idle;
                s_lastCpuUsage = total == 0 ? s_lastCpuUsage : Math.Clamp(1d - ((double)idle / total), 0d, 1d);
            }

            s_lastCpuSample = sample;
            s_lastCpuSampleTicks = Environment.TickCount64;
            return s_lastCpuUsage;
        }
    }

    /// <summary>(used, total) physical memory in bytes.</summary>
    public static (ulong Used, ulong Total)? Memory()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (status.TotalPhysical - status.AvailablePhysical, status.TotalPhysical) : null;
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            ulong ReadKb(string[] lines, string name) =>
                lines.Where(line => line.StartsWith(name + ":", StringComparison.Ordinal))
                    .Select(line => ulong.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture))
                    .FirstOrDefault();

            var lines = File.ReadAllLines("/proc/meminfo");
            var total = ReadKb(lines, "MemTotal") * 1024;
            var available = ReadKb(lines, "MemAvailable") * 1024;
            return total == 0 ? null : (total - available, total);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacNative.Memory();
        }

        return null;
    }

    public static TimeSpan Uptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>(free, total) bytes of the drive that holds <paramref name="path"/>.</summary>
    public static (long Free, long Total)? Disk(string path)
    {
        try
        {
            var root = Path.GetPathRoot(string.IsNullOrWhiteSpace(path) ? Environment.SystemDirectory : path);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? (drive.AvailableFreeSpace, drive.TotalSize) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return bytes.ToString(bytes >= 100 || unit == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static (ulong Idle, ulong Total)? ReadCpuTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            // Kernel time includes idle time.
            return GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel + user) : null;
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/stat"))
        {
            var fields = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .Select(value => ulong.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            var idle = fields[3] + (fields.Length > 4 ? fields[4] : 0);
            return (idle, fields.Aggregate(0UL, (sum, value) => sum + value));
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacNative.CpuTicks();
        }

        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

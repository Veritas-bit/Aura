using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public class SystemStats
{
    public double Cpu;      // %
    public double Ram;      // %
    public double Gpu;      // % (3D-движки, best-effort)
    public double Disk;     // %
    public double RamUsedGb;
    public double RamTotalGb;
    public bool HasBattery;
    public int BatteryPercent;   // -1 = неизвестно
    public bool BatteryCharging;
    public bool GpuAvailable;
    public double CpuTemp = -1;    // °C, -1 = недоступно
    public double GpuTemp = -1;    // °C, -1 = недоступно
    public double DiskFill;        // % заполненности системного диска
    public double Ping = -1;       // мс до 1.1.1.1, -1 = недоступно
    public double PingLoss;        // % потерь за скользящее окно
}

/// <summary>
/// Мониторинг CPU/RAM/GPU/Disk через PDH (английские имена счётчиков —
/// не зависит от языка системы) + батарея через GetSystemPowerStatus.
/// </summary>
public sealed class SystemMonitorService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private IntPtr _query = IntPtr.Zero;
    private IntPtr _cpuCounter = IntPtr.Zero;
    private IntPtr _diskCounter = IntPtr.Zero;
    private IntPtr _gpuCounter = IntPtr.Zero;
    private int _gpuFailCount;
    private int _slowTick;
    private bool _tempSupported = true;

    public SystemStats Stats { get; } = new();
    public event Action<SystemStats>? Updated;

    public SystemMonitorService()
    {
        InitPdh();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Poll();
        Poll();
        _timer.Start();
    }

    // ---------- PDH ----------

    private void InitPdh()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
            PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpuCounter);
            PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Disk Time", IntPtr.Zero, out _diskCounter);
            // GPU: все 3D-движки (шаблон с маской)
            if (PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpuCounter) != 0)
                _gpuCounter = IntPtr.Zero;
            PdhCollectQueryData(_query); // первый сбор — базовая точка
        }
        catch { /* PDH недоступен — показываем нули */ }
    }

    private void Poll()
    {
        // CPU / Disk / GPU
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            Stats.Cpu = Clamp(ReadCounter(_cpuCounter));
            Stats.Disk = Clamp(ReadCounter(_diskCounter));
            var gpu = ReadCounterArraySum(_gpuCounter);
            if (gpu >= 0) { Stats.Gpu = Clamp(gpu); Stats.GpuAvailable = true; _gpuFailCount = 0; }
            else if (++_gpuFailCount > 5) Stats.GpuAvailable = false;
        }

        // RAM
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            Stats.RamTotalGb = Math.Round(mem.ullTotalPhys / 1073741824.0, 1);
            Stats.RamUsedGb = Math.Round((mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0, 1);
            Stats.Ram = 100.0 * (mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys;
        }

        // Батарея
        if (GetSystemPowerStatus(out var ps))
        {
            Stats.HasBattery = (ps.BatteryFlag & 128) == 0;          // аккумулятор есть
            Stats.BatteryPercent = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : -1; // 255 = неизвестно
            Stats.BatteryCharging = (ps.BatteryFlag & 8) != 0 || ps.ACLineStatus == 1;
        }

        // Медленные метрики (температуры, заполненность диска, сеть) — раз в 5 секунд
        if (++_slowTick % 5 == 1)
        {
            PollTemperatures();
            PollGpuTemp();
            PollDiskFill();
            PollPing();
        }

        Updated?.Invoke(Stats);
    }

    /// <summary>Температура GPU через nvidia-smi (если установлен драйвер NVIDIA).</summary>
    private bool _nvsmiChecked;
    private string? _nvsmiPath;

    private void PollGpuTemp()
    {
        if (!_nvsmiChecked)
        {
            _nvsmiChecked = true;
            _nvsmiPath = FindOnPath("nvidia-smi.exe");
        }
        if (_nvsmiPath == null) { Stats.GpuTemp = -1; return; }
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                _nvsmiPath, "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(1500);
            var line = output.Split('\n')[0].Trim();
            Stats.GpuTemp = double.TryParse(line, out var t) ? t : -1;
        }
        catch { Stats.GpuTemp = -1; }
    }

    private static string? FindOnPath(string exe)
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var full = System.IO.Path.Combine(dir.Trim(), exe);
                if (System.IO.File.Exists(full)) return full;
            }
            // стандартное место драйвера NVIDIA
            var sys = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", exe);
            if (System.IO.File.Exists(sys)) return sys;
        }
        catch { }
        return null;
    }

    /// <summary>Ping до 1.1.1.1 и процент потерь за последние 20 проб.</summary>
    private readonly Queue<bool> _pingWindow = new();
    private bool _pingBusy;

    private async void PollPing()
    {
        if (_pingBusy) return;
        _pingBusy = true;
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1200);
            bool ok = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            _pingWindow.Enqueue(ok);
            while (_pingWindow.Count > 20) _pingWindow.Dequeue();
            Stats.Ping = ok ? reply.RoundtripTime : -1;
            int lost = 0;
            foreach (var r in _pingWindow) if (!r) lost++;
            Stats.PingLoss = Math.Round(100.0 * lost / _pingWindow.Count);
        }
        catch
        {
            Stats.Ping = -1;
            _pingWindow.Enqueue(false);
            while (_pingWindow.Count > 20) _pingWindow.Dequeue();
        }
        finally { _pingBusy = false; }
    }

    /// <summary>Температуры через ACPI-термозоны (root\wmi). Не везде поддерживается.</summary>
    private void PollTemperatures()
    {
        if (!_tempSupported) return;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double sum = 0; int n = 0;
            foreach (var obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] is uint raw)
                {
                    sum += raw / 10.0 - 273.15;
                    n++;
                }
            }
            Stats.CpuTemp = n > 0 ? Math.Round(sum / n) : -1;
        }
        catch
        {
            Stats.CpuTemp = -1;
            _tempSupported = false; // не дёргаем WMI каждый цикл, если недоступно
        }
    }

    /// <summary>Заполненность системного диска.</summary>
    private void PollDiskFill()
    {
        try
        {
            var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);
            if (drive.TotalSize > 0)
                Stats.DiskFill = Math.Round(100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize);
        }
        catch { }
    }

    private static double Clamp(double v) => double.IsNaN(v) || v < 0 ? 0 : Math.Min(100, Math.Round(v));

    private static double ReadCounter(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return 0;
        return PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var v) == 0 && v.CStatus == 0
            ? v.doubleValue : 0;
    }

    private double ReadCounterArraySum(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return -1;
        uint size = 0, count = 0;
        uint rc = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
        if (rc != PDH_MORE_DATA || size == 0) return -1;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            rc = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buffer);
            if (rc != 0 || count == 0) return -1;
            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.FmtValue.CStatus == 0) sum += item.FmtValue.doubleValue;
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }

    // ---------- P/Invoke ----------

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public int CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("pdh.dll")] private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr hQuery);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr ItemBuffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr hQuery);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
}

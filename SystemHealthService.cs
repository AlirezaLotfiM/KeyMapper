using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace KeyMapper
{
    public sealed record HardwareHealthInfo(
        int CpuUsagePercent,
        int RamUsagePercent,
        long UsedRamMb,
        long TotalRamMb,
        string TopProcessName,
        long TopProcessRamMb,
        int BatteryPercent,
        bool IsCharging);

    public sealed class SystemHealthService
    {
        private static readonly Lazy<SystemHealthService> LazyInstance = new(() => new SystemHealthService());
        public static SystemHealthService Instance => LazyInstance.Value;

        private readonly DispatcherTimer _monitorTimer;
        private DateTime _lastHourlyChime = DateTime.MinValue;
        private bool _hasWarnedLowBattery = false;
        private bool _hasWarnedHighRam = false;

        public event Action<string>? OnSystemWarning;
        public event Action<int>? OnHourlyChime; // Passes current hour (e.g. 14 for 2 PM)

        private SystemHealthService()
        {
            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();
        }

        public HardwareHealthInfo GetCurrentHealth()
        {
            int ramPercent = 0;
            long usedRamMb = 0;
            long totalRamMb = 0;

            try
            {
                var memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    ramPercent = (int)memStatus.dwMemoryLoad;
                    totalRamMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                    usedRamMb = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024 * 1024));
                }
            }
            catch { }

            // Find top RAM consuming process safely
            string topProcName = "Apps";
            long topProcRamMb = 0;
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id <= 4) continue; // Skip System idle & System process
                        long bytes = p.WorkingSet64;
                        if (bytes > topProcRamMb * 1024 * 1024)
                        {
                            topProcRamMb = bytes / (1024 * 1024);
                            topProcName = p.ProcessName;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Battery Status using native Win32 API
            int batteryPercent = 100;
            bool isCharging = true;
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    if (status.BatteryLifePercent <= 100)
                    {
                        batteryPercent = status.BatteryLifePercent;
                    }
                    isCharging = status.ACLineStatus == 1;
                }
            }
            catch { }

            return new HardwareHealthInfo(
                CpuUsagePercent: 0,
                RamUsagePercent: ramPercent,
                UsedRamMb: usedRamMb,
                TotalRamMb: totalRamMb,
                TopProcessName: topProcName,
                TopProcessRamMb: topProcRamMb,
                BatteryPercent: batteryPercent,
                IsCharging: isCharging);
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var health = GetCurrentHealth();
                    DateTime now = DateTime.Now;

                    // 1. Hourly Chime Check (at minute 00)
                    if (now.Minute == 0 && (now - _lastHourlyChime).TotalMinutes >= 45)
                    {
                        _lastHourlyChime = now;
                        OnHourlyChime?.Invoke(now.Hour);
                    }

                    // 2. High RAM Warning (> 88%)
                    if (health.RamUsagePercent >= 88)
                    {
                        if (!_hasWarnedHighRam)
                        {
                            _hasWarnedHighRam = true;
                            OnSystemWarning?.Invoke($"⚠️ High Memory Usage ({health.RamUsagePercent}%)! {health.TopProcessName} is using {health.TopProcessRamMb} MB RAM.");
                        }
                    }
                    else
                    {
                        _hasWarnedHighRam = false;
                    }

                    // 3. Low Battery Warning (< 15% when unplugged)
                    if (!health.IsCharging && health.BatteryPercent <= 15 && health.BatteryPercent > 0)
                    {
                        if (!_hasWarnedLowBattery)
                        {
                            _hasWarnedLowBattery = true;
                            OnSystemWarning?.Invoke($"🔋 Battery Low ({health.BatteryPercent}%)! Please plug in your charger.");
                        }
                    }
                    else if (health.IsCharging)
                    {
                        _hasWarnedLowBattery = false;
                    }
                }
                catch { }
            });
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
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

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }
    }
}

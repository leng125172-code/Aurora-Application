using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AuroraStruct3D.Services;

/// <summary>
/// 跨平台系统指标采集器（CPU、内存、网络 I/O、NPU/GPU）。
/// 注册为单例服务，每次调用时计算增量以获取速率。
/// </summary>
public sealed class SystemMetricsCollector : IDisposable
{
    // ── CPU 状态（上一次采样快照）─────────────────────────────────────────────
    private long _prevCpuIdle;
    private long _prevCpuTotal;

    // ── 网络 I/O 状态（上一次采样快照）──────────────────────────────────────
    private long _prevBytesSent;
    private long _prevBytesReceived;
    private DateTime _prevNetworkTime = DateTime.UtcNow;

    // ── Windows GPU PDH 查询状态 ──────────────────────────────────────────────
    private IntPtr _gpuQuery;
    private IntPtr _gpuCounter;
    private bool _gpuPdhReady; // true = PDH 初始化成功且基准已采集
    private bool _gpuPdhFailed; // true = 初始化失败，不再重试

    // ── Windows NPU PDH 查询状态（Intel AI Boost，Meteor Lake+）───────────────
    private IntPtr _npuWinQuery;
    private IntPtr _npuWinCounter;
    private bool _npuWinReady;
    private bool _npuWinFailed;

    // ── Windows P/Invoke（kernel32）──────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out long lpIdleTime,
        out long lpKernelTime,
        out long lpUserTime
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        internal uint DwLength;
        internal uint DwMemoryLoad;
        internal ulong UllTotalPhys;
        internal ulong UllAvailPhys;
        internal ulong UllTotalPageFile;
        internal ulong UllAvailPageFile;
        internal ulong UllTotalVirtual;
        internal ulong UllAvailVirtual;
        internal ulong UllAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    // ── Windows P/Invoke（pdh.dll：性能数据助手）─────────────────────────────
    private const uint PdhErrSuccess = 0;
    private const uint PdhFmtDouble = 0x00000200;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(
        string? dataSource,
        UIntPtr userData,
        out IntPtr phQuery
    );

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr hQuery,
        string szFullCounterPath,
        UIntPtr dwUserData,
        out IntPtr phCounter
    );

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr hCounter,
        uint dwFormat,
        ref uint lpdwBufferSize,
        out uint lpdwItemCount,
        IntPtr ItemBuffer
    );

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr hQuery);

    /// <summary>
    /// PDH 计数器值项（64 位：szName=8B, CStatus=4B, padding=4B, doubleValue=8B，共 24B）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItemW
    {
        public IntPtr SzName;
        public uint CStatus;
        private readonly uint _reserved;
        public double DoubleValue;
    }

    // ── 公共 API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 采集一次所有指标并返回聚合快照。
    /// 注意：CPU 和网络速率依赖前后两次调用的时间差，首次调用返回 0。
    /// </summary>
    public SystemMetricsSnapshot Collect()
    {
        double cpu = GetCpuUsage();
        var (memPercent, memUsed, memTotal) = GetMemoryUsage();
        var (sendRate, recvRate, totalSent, totalRecv) = GetNetworkStats();
        double npu = GetNpuUsage();
        double gpu = GetGpuUsage();

        return new SystemMetricsSnapshot
        {
            CpuPercent = cpu,
            MemoryPercent = memPercent,
            MemoryUsedBytes = memUsed,
            MemoryTotalBytes = memTotal,
            NetworkSendRateBytes = sendRate,
            NetworkReceiveRateBytes = recvRate,
            NetworkTotalSentBytes = totalSent,
            NetworkTotalReceivedBytes = totalRecv,
            NpuPercent = npu,
            GpuPercent = gpu,
        };
    }

    // ── CPU ───────────────────────────────────────────────────────────────────

    private double GetCpuUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetLinuxCpuUsage();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsCpuUsage();
        return -1;
    }

    private double GetLinuxCpuUsage()
    {
        try
        {
            string[] lines = File.ReadAllLines("/proc/stat");
            string[] parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // user nice system idle iowait irq softirq
            long user = long.Parse(parts[1]);
            long nice = long.Parse(parts[2]);
            long system = long.Parse(parts[3]);
            long idle = long.Parse(parts[4]);
            long iowait = parts.Length > 5 ? long.Parse(parts[5]) : 0;
            long irq = parts.Length > 6 ? long.Parse(parts[6]) : 0;
            long softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;

            long totalIdle = idle + iowait;
            long total = user + nice + system + idle + iowait + irq + softirq;

            long diffIdle = totalIdle - _prevCpuIdle;
            long diffTotal = total - _prevCpuTotal;

            _prevCpuIdle = totalIdle;
            _prevCpuTotal = total;

            if (diffTotal == 0)
                return 0;
            return Math.Round((1.0 - (double)diffIdle / diffTotal) * 100, 1);
        }
        catch
        {
            return -1;
        }
    }

    private double GetWindowsCpuUsage()
    {
        try
        {
            if (!GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
                return -1;

            long total = kernelTime + userTime;
            long diffIdle = idleTime - _prevCpuIdle;
            long diffTotal = total - _prevCpuTotal;

            _prevCpuIdle = idleTime;
            _prevCpuTotal = total;

            if (diffTotal == 0)
                return 0;
            return Math.Round((1.0 - (double)diffIdle / diffTotal) * 100, 1);
        }
        catch
        {
            return -1;
        }
    }

    // ── 内存 ──────────────────────────────────────────────────────────────────

    private (double percent, long used, long total) GetMemoryUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetLinuxMemoryUsage();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsMemoryUsage();
        return (-1, 0, 0);
    }

    private (double, long, long) GetLinuxMemoryUsage()
    {
        try
        {
            long memTotal = 0,
                memAvailable = 0;
            foreach (string line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    memTotal =
                        long.Parse(
                            line.Split(':')[1].Trim().Split(' ')[0],
                            System.Globalization.CultureInfo.InvariantCulture
                        ) * 1024;
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    memAvailable =
                        long.Parse(
                            line.Split(':')[1].Trim().Split(' ')[0],
                            System.Globalization.CultureInfo.InvariantCulture
                        ) * 1024;
            }
            if (memTotal == 0)
                return (-1, 0, 0);
            long used = memTotal - memAvailable;
            return (Math.Round((double)used / memTotal * 100, 1), used, memTotal);
        }
        catch
        {
            return (-1, 0, 0);
        }
    }

    private (double, long, long) GetWindowsMemoryUsage()
    {
        try
        {
            var status = new MemoryStatusEx { DwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
                return (-1, 0, 0);
            long total = (long)status.UllTotalPhys;
            long available = (long)status.UllAvailPhys;
            long used = total - available;
            return (Math.Round((double)used / total * 100, 1), used, total);
        }
        catch
        {
            return (-1, 0, 0);
        }
    }

    // ── 网络 I/O ──────────────────────────────────────────────────────────────

    private (long sendRate, long recvRate, long totalSent, long totalRecv) GetNetworkStats()
    {
        try
        {
            long sent = 0,
                received = 0;
            foreach (
                NetworkInterface nic in NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(n =>
                        n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    )
            )
            {
                IPv4InterfaceStatistics stats = nic.GetIPv4Statistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }

            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _prevNetworkTime).TotalSeconds;

            long sendRate = elapsed > 0 ? (long)Math.Max(0, (sent - _prevBytesSent) / elapsed) : 0;
            long recvRate =
                elapsed > 0 ? (long)Math.Max(0, (received - _prevBytesReceived) / elapsed) : 0;

            _prevBytesSent = sent;
            _prevBytesReceived = received;
            _prevNetworkTime = now;

            return (sendRate, recvRate, sent, received);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    // ── NPU ───────────────────────────────────────────────────────────────────

    private double GetNpuUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetLinuxRk3588NpuUsage();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsNpuUsage();
        return -1;
    }

    /// <summary>
    /// 读取 RK3588 NPU 使用率（/sys/kernel/debug/rknpu/load）。
    /// 返回多核均值，-1 表示不支持或读取失败。
    /// </summary>
    private static double GetLinuxRk3588NpuUsage()
    {
        try
        {
            const string path = "/sys/kernel/debug/rknpu/load";
            if (!File.Exists(path))
                return -1;
            string content = File.ReadAllText(path);
            // 格式示例：NPU load:  Core0: 12%, Core1:  8%, Core2:  5%,
            MatchCollection matches = Regex.Matches(content, @"(\d+)%");
            if (matches.Count == 0)
                return -1;
            double sum = matches.Cast<Match>().Sum(m => double.Parse(m.Groups[1].Value));
            return Math.Round(sum / matches.Count, 1);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Windows Intel AI Boost NPU 使用率（Meteor Lake+）。
    /// 通过 PDH 计数器 "\NPU Engine(*)\Utilization Percentage" 读取；
    /// 若系统无 Intel NPU，则在首次尝试失败后永久返回 -1。
    /// </summary>
    private double GetWindowsNpuUsage()
    {
        if (_npuWinFailed)
            return -1;
        try
        {
            if (!_npuWinReady)
            {
                uint s = PdhOpenQuery(null, UIntPtr.Zero, out _npuWinQuery);
                if (s != PdhErrSuccess)
                {
                    _npuWinFailed = true;
                    return -1;
                }
                s = PdhAddEnglishCounterW(
                    _npuWinQuery,
                    @"\NPU Engine(*)\Utilization Percentage",
                    UIntPtr.Zero,
                    out _npuWinCounter
                );
                if (s != PdhErrSuccess)
                {
                    // 此系统无 Intel NPU，关闭查询并标记不再重试
                    PdhCloseQuery(_npuWinQuery);
                    _npuWinQuery = IntPtr.Zero;
                    _npuWinFailed = true;
                    return -1;
                }
                PdhCollectQueryData(_npuWinQuery); // 基准采集
                _npuWinReady = true;
                return 0; // 首次调用返回 0（NPU 存在但尚无差分数据）
            }
            PdhCollectQueryData(_npuWinQuery);
            return QueryPdhCounterSum(_npuWinCounter);
        }
        catch
        {
            _npuWinFailed = true;
            return -1;
        }
    }

    // ── GPU ───────────────────────────────────────────────────────────────────

    private double GetGpuUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsGpuUsage();
        return -1;
    }

    /// <summary>
    /// Windows GPU 3D 引擎使用率（支持 Intel/AMD/NVIDIA）。
    /// 通过 PDH 计数器 "\GPU Engine(*engtype_3D*)\Utilization Percentage" 读取。
    /// 适用于 Windows 10 20H1（2004）及更新版本。
    /// </summary>
    private double GetWindowsGpuUsage()
    {
        if (_gpuPdhFailed)
            return -1;
        try
        {
            if (!_gpuPdhReady)
            {
                uint s = PdhOpenQuery(null, UIntPtr.Zero, out _gpuQuery);
                if (s != PdhErrSuccess)
                {
                    _gpuPdhFailed = true;
                    return -1;
                }
                s = PdhAddEnglishCounterW(
                    _gpuQuery,
                    @"\GPU Engine(*engtype_3D*)\Utilization Percentage",
                    UIntPtr.Zero,
                    out _gpuCounter
                );
                if (s != PdhErrSuccess)
                {
                    PdhCloseQuery(_gpuQuery);
                    _gpuQuery = IntPtr.Zero;
                    _gpuPdhFailed = true;
                    return -1;
                }
                PdhCollectQueryData(_gpuQuery); // 基准采集
                _gpuPdhReady = true;
                return 0; // 首次调用返回 0（GPU 存在但尚无差分数据）
            }
            PdhCollectQueryData(_gpuQuery);
            return QueryPdhCounterSum(_gpuCounter);
        }
        catch
        {
            _gpuPdhFailed = true;
            return -1;
        }
    }

    // ── PDH 辅助 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 读取 PDH 计数器数组并对所有有效实例求和，结果夹在 0~100 之间。
    /// GPU Engine 各进程/引擎实例之和即为总 GPU 3D 引擎负载。
    /// </summary>
    private static double QueryPdhCounterSum(IntPtr hCounter)
    {
        uint bufSize = 0;
        uint itemCount = 0;
        // 第一次调用以获取所需缓冲区大小（预期返回 PDH_MORE_DATA）
        PdhGetFormattedCounterArrayW(
            hCounter,
            PdhFmtDouble,
            ref bufSize,
            out itemCount,
            IntPtr.Zero
        );
        if (bufSize == 0)
            return 0;

        IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            uint status = PdhGetFormattedCounterArrayW(
                hCounter,
                PdhFmtDouble,
                ref bufSize,
                out itemCount,
                buf
            );
            if (status != PdhErrSuccess || itemCount == 0)
                return 0;

            int itemSize = Marshal.SizeOf<PdhFmtCounterValueItemW>();
            double sum = 0;
            for (int i = 0; i < (int)itemCount; i++)
            {
                PdhFmtCounterValueItemW item = Marshal.PtrToStructure<PdhFmtCounterValueItemW>(
                    buf + i * itemSize
                );
                if (item.CStatus == 0)
                    sum += item.DoubleValue;
            }
            return Math.Round(Math.Min(100.0, sum), 1);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        if (_gpuQuery != IntPtr.Zero)
        {
            PdhCloseQuery(_gpuQuery);
            _gpuQuery = IntPtr.Zero;
        }
        if (_npuWinQuery != IntPtr.Zero)
        {
            PdhCloseQuery(_npuWinQuery);
            _npuWinQuery = IntPtr.Zero;
        }
    }
}

/// <summary>
/// 系统指标快照（一次采样的结果）。
/// </summary>
public sealed class SystemMetricsSnapshot
{
    /// <summary>CPU 使用率（0-100），-1 表示不支持</summary>
    public double CpuPercent { get; init; }

    /// <summary>内存使用率（0-100），-1 表示不支持</summary>
    public double MemoryPercent { get; init; }

    /// <summary>已用内存（字节）</summary>
    public long MemoryUsedBytes { get; init; }

    /// <summary>总内存（字节）</summary>
    public long MemoryTotalBytes { get; init; }

    /// <summary>网络发送速率（字节/秒）</summary>
    public long NetworkSendRateBytes { get; init; }

    /// <summary>网络接收速率（字节/秒）</summary>
    public long NetworkReceiveRateBytes { get; init; }

    /// <summary>网络累计发送总量（字节）</summary>
    public long NetworkTotalSentBytes { get; init; }

    /// <summary>网络累计接收总量（字节）</summary>
    public long NetworkTotalReceivedBytes { get; init; }

    /// <summary>NPU 使用率（0-100），-1 表示不支持（仅 Linux/RK3588）</summary>
    public double NpuPercent { get; init; }

    /// <summary>GPU 使用率（0-100），-1 表示不支持</summary>
    public double GpuPercent { get; init; }
}

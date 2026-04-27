namespace AuroraStruct3D.Controllers
{
    /// <summary>
    /// 服务器状态仪表盘接口
    /// </summary>
    [Route("api/dashboard")]
    public class DashboardController : AbpController
    {
        private static readonly DateTime _startTime = DateTime.UtcNow;

        /// <summary>
        /// 获取服务器状态信息
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - _startTime;

            // CPU 使用率（进程级近似）
            double cpuUsage = 0;
            try
            {
                cpuUsage = process.TotalProcessorTime.TotalMilliseconds /
                           (Environment.ProcessorCount * uptime.TotalMilliseconds) * 100;
                cpuUsage = Math.Min(cpuUsage, 100);
            }
            catch { /* 忽略 */ }

            // 内存信息
            var gcInfo = GC.GetGCMemoryInfo();
            var totalMemoryMB = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
            var usedMemoryMB = process.WorkingSet64 / (1024.0 * 1024.0);

            // 磁盘信息
            double totalDiskMB = 0, usedDiskMB = 0;
            try
            {
                var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.RootDirectory.FullName == Path.GetPathRoot(AppContext.BaseDirectory));
                if (drive != null)
                {
                    totalDiskMB = drive.TotalSize / (1024.0 * 1024.0);
                    usedDiskMB = (drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024.0);
                }
            }
            catch { /* 忽略 */ }

            // Health Check
            string healthStatus = "Healthy";

            // 运行时长格式化
            string uptimeStr = uptime.Days > 0
                ? $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
                : $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";

            return Ok(new
            {
                cpuUsage = Math.Round(cpuUsage, 1),
                totalMemoryMB = Math.Round(totalMemoryMB, 0),
                usedMemoryMB = Math.Round(usedMemoryMB, 0),
                totalDiskMB = Math.Round(totalDiskMB, 0),
                usedDiskMB = Math.Round(usedDiskMB, 0),
                osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                machineName = Environment.MachineName,
                frameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                uptime = uptimeStr,
                healthStatus,
                processId = process.Id,
                threadCount = process.Threads.Count
            });
        }
    }
}

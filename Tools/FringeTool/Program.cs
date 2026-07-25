using CommandLine;

namespace FringeTool;

public class Options
{
    [Option('c', "count", Required = false, HelpText = "条纹图像数量（每方向）")]
    public int ImageCount { get; set; }

    [Option('p', "period", Required = false, HelpText = "条纹周期数")]
    public int PeriodCount { get; set; }

    [Option('s', "shift", Required = false, HelpText = "相移量")]
    public int PhaseShift { get; set; }

    [Option('w', "width", Default = 1280, HelpText = "投影仪宽度像素")]
    public int WidthPixels { get; set; }

    [Option('H', "height", Default = 720, HelpText = "投影仪高度像素")]
    public int HeightPixels { get; set; }

    [Option('t', "type", Default = "wb", HelpText = "条纹类型：wb(白黑) / bw(黑白)")]
    public string FringeType { get; set; } = "wb";

    [Option('o', "output", HelpText = "保存条纹图像到指定目录")]
    public string? OutputDirectory { get; set; }

    [Option('d', "download", Default = false, HelpText = "下载条纹到投影仪（需要USB HID连接）")]
    public bool DownloadToProjector { get; set; }

    [Option("vid", Default = "0x0483", HelpText = "HID厂商ID（支持十进制或十六进制，如 1155 或 0x0483）")]
    public string VendorIdStr { get; set; } = "0x0483";

    [Option("pid", Default = "0x5750", HelpText = "HID产品ID（支持十进制或十六进制，如 22352 或 0x5750）")]
    public string ProductIdStr { get; set; } = "0x5750";

    public int VendorId => ParseHexInt(VendorIdStr);
    public int ProductId => ParseHexInt(ProductIdStr);

    private static int ParseHexInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(value.Substring(2), 16);
        }
        return int.Parse(value);
    }

    [Option('n', "index", Default = 0, HelpText = "HID设备索引")]
    public int DeviceIndex { get; set; }

    [Option('l', "list", Default = false, HelpText = "列出可用的HID设备")]
    public bool ListDevices { get; set; }

    [Option('g', "getinfo", Default = false, HelpText = "获取投影仪信息")]
    public bool GetInfo { get; set; }

    [Option('f', "fill", Default = true, HelpText = "横条纹帧是否填充空白区域（560字节），使用 --fill false 或 --no-fill 不填充")]
    public bool FillHorizontalPadding { get; set; }

    [Option("no-fill", Default = false, HelpText = "不填充横条纹帧的空白区域（等同于 --fill false）")]
    public bool NoFillHorizontalPadding { get; set; }

    public bool IsUtilityMode => ListDevices || GetInfo;

    public bool IsGenerateMode => !IsUtilityMode;
}

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("  投影仪条纹生成与下载工具");
        Console.WriteLine("========================================");
        Console.WriteLine();

        await Parser.Default.ParseArguments<Options>(args)
            .WithParsedAsync(RunAsync);
    }

    static async Task RunAsync(Options options)
    {
        try
        {
            if (options.ListDevices)
            {
                await ListHidDevices(options.VendorId, options.ProductId);
                return;
            }

            if (options.GetInfo)
            {
                await GetProjectorInfo(options.VendorId, options.ProductId, options.DeviceIndex);
                return;
            }

            if (options.ImageCount <= 0)
            {
                Console.WriteLine("错误: --count 必须大于 0");
                return;
            }

            if (options.PeriodCount <= 0)
            {
                Console.WriteLine("错误: --period 必须大于 0");
                return;
            }

            if (options.PhaseShift <= 0)
            {
                Console.WriteLine("错误: --shift 必须大于 0");
                return;
            }

            if (options.NoFillHorizontalPadding)
            {
                options.FillHorizontalPadding = false;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 生成条纹参数:");
            Console.WriteLine($"  - 图像数量: {options.ImageCount}（每方向）");
            Console.WriteLine($"  - 周期数: {options.PeriodCount}");
            Console.WriteLine($"  - 相移量: {options.PhaseShift}");
            Console.WriteLine($"  - 分辨率: {options.WidthPixels} x {options.HeightPixels}");
            Console.WriteLine($"  - 条纹类型: {options.FringeType}");
            Console.WriteLine();

            byte[][] fringeImages = FringeGenerator.GenerateFringePatterns(
                options.WidthPixels,
                options.HeightPixels,
                options.PeriodCount,
                options.PhaseShift,
                options.ImageCount,
                options.FringeType
            );

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 条纹生成完成，共 {fringeImages.Length} 幅图像");
            Console.WriteLine($"  - 横条纹: {fringeImages.Length / 2} 幅");
            Console.WriteLine($"  - 竖条纹: {fringeImages.Length / 2} 幅");
            Console.WriteLine();

            if (!string.IsNullOrEmpty(options.OutputDirectory))
            {
                FringeGenerator.SaveFringePatternsToFiles(
                    fringeImages,
                    options.OutputDirectory,
                    options.WidthPixels,
                    options.HeightPixels
                );
                Console.WriteLine();
            }

            if (options.DownloadToProjector)
            {
                await DownloadFringeToProjector(
                    fringeImages,
                    options.WidthPixels,
                    options.HeightPixels,
                    options.VendorId,
                    options.ProductId,
                    options.DeviceIndex,
                    options.FillHorizontalPadding
                );
            }

            Console.WriteLine();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 操作完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static async Task ListHidDevices(int vendorId, int productId)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 搜索 HID 设备: VID=0x{vendorId:X4} PID=0x{productId:X4}");
        Console.WriteLine();

        using var client = new HidProjectorClient(vendorId, productId);
        int count = HidProjectorClient.GetDeviceCount(vendorId, productId);

        if (count == 0)
        {
            Console.WriteLine("未找到匹配的HID设备");
            return;
        }

        Console.WriteLine($"找到 {count} 台设备:");
        Console.WriteLine();
        for (int i = 0; i < count; i++)
        {
            Console.WriteLine($"  索引 {i}: 使用 --index {i} 选择此设备");
        }
    }

    static async Task GetProjectorInfo(int vendorId, int productId, int deviceIndex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 连接投影仪: VID=0x{vendorId:X4} PID=0x{productId:X4} 索引={deviceIndex}");
        Console.WriteLine();

        using var client = new HidProjectorClient(vendorId, productId, deviceIndex);
        await client.ConnectAsync();

        try
        {
            string? version = await client.SendCommandAndReadAsync(TjProjectorCommands.ReadVersion);
            Console.WriteLine($"固件版本: {version ?? "未知"}");

            string? pixelMode = await client.SendCommandAndReadAsync(TjProjectorCommands.ReadPixelMode);
            Console.WriteLine($"像素模式: {pixelMode ?? "未知"}");

            await client.SendCommandAsync(TjProjectorCommands.LedOn);
            await Task.Delay(100);

            string? ledStatus = await client.SendCommandAndReadAsync("L\r\n");
            Console.WriteLine($"LED状态: {ledStatus ?? "未知"}");
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    static async Task DownloadFringeToProjector(byte[][] frames, int widthPixels, int heightPixels, int vendorId, int productId, int deviceIndex, bool fillHorizontalPadding = true)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 开始下载条纹到投影仪...");
        Console.WriteLine();

        using var client = new HidProjectorClient(vendorId, productId, deviceIndex);
        await client.ConnectAsync();

        try
        {
            int imageCount = frames.Length;
            int frameStride = widthPixels;
            int effectiveStride = fillHorizontalPadding ? frameStride : heightPixels;
            int totalWrites = imageCount * effectiveStride;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 写入参数:");
            Console.WriteLine($"  - 图像数量: {imageCount}");
            Console.WriteLine($"  - 帧宽度: {frameStride}");
            Console.WriteLine($"  - 有效宽度: {effectiveStride}");
            Console.WriteLine($"  - 填充空白: {(fillHorizontalPadding ? "是" : "否")}");
            Console.WriteLine($"  - 总写入量: {totalWrites}");
            Console.WriteLine();

            const string NewLine = "\r\n";
            const int WriteDelayMs = 25;
            const int MaxEraseRetries = 5;

            string mfCmd = BuildFringeOrientationCommand(imageCount, 0);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 设置条纹方向(MF): {mfCmd.TrimEnd('\r', '\n')}");
            await client.SendCommandAndReadAsync(mfCmd);
            await Task.Delay(50);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 保存条纹参数(Ms)...");
            await client.SendCommandAndReadAsync(TjProjectorCommands.SaveFringeParams + NewLine);
            await Task.Delay(100);

            string maCmd = $"{TjProjectorCommands.SetImageRepeatPrefix}59 {imageCount - 1} 0 0{NewLine}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 设置重复参数(MA): {maCmd.TrimEnd('\r', '\n')}");
            await client.SendCommandAndReadAsync(maCmd);
            await Task.Delay(100);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 保存参数(MS)...");
            await client.SendCommandAndReadAsync(TjProjectorCommands.SaveParams + NewLine);
            await Task.Delay(100);

            string mbCmd = $"{TjProjectorCommands.SetImageCountPrefix}{imageCount - 1}{NewLine}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 设置图像数量(MB): {mbCmd.TrimEnd('\r', '\n')}");
            await client.SendCommandAndReadAsync(mbCmd);
            await Task.Delay(500);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 保存参数(MS)...");
            await client.SendCommandAndReadAsync(TjProjectorCommands.SaveParams + NewLine);
            await Task.Delay(100);

            Console.WriteLine();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 擦除Flash...");

            async Task<bool> SendFeAndWaitAsync(int attemptNum)
            {
                for (int attempt = 0; attempt < MaxEraseRetries; attempt++)
                {
                    Console.WriteLine($"  清空接收区...");
                    await client.DrainInputBufferAsync();
                    string? eraseReply = await client.SendCommandAndReadAsync(TjProjectorCommands.EraseFlash + NewLine);
                    Console.WriteLine($"  擦除尝试 {attemptNum}-{attempt + 1}: {eraseReply ?? "无响应"}");

                    if (eraseReply != null && eraseReply.Trim().StartsWith(TjProjectorCommands.FlashEraseOk, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    await Task.Delay(500);
                }
                return false;
            }

            if (!await SendFeAndWaitAsync(1))
                throw new InvalidOperationException("第一次FE擦除失败");

            Console.WriteLine("  等待5秒...");
            await Task.Delay(5000);

            if (!await SendFeAndWaitAsync(2))
                throw new InvalidOperationException("第二次FE擦除失败");

            Console.WriteLine("  等待5秒...");
            await Task.Delay(5000);

            Console.WriteLine();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 写入条纹数据...");

            int lastReportedProgress = 0;
            int writtenCount = 0;
            int globalCol = 0;

            for (int frameIdx = 0; frameIdx < imageCount; frameIdx++)
            {
                byte[] frame = frames[frameIdx];
                bool isHorizontal = frameIdx % 2 == 0;

                int writeStride = fillHorizontalPadding ? frameStride : frame.Length;
                byte[] frameData;

                if (fillHorizontalPadding && frame.Length < frameStride)
                {
                    frameData = new byte[frameStride];
                    Array.Fill(frameData, (byte)255);
                    Array.Copy(frame, frameData, frame.Length);
                }
                else
                {
                    frameData = frame;
                }

                int endCol = globalCol + writeStride - 1;
                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] 写入帧 {frameIdx:D2} ({(isHorizontal ? "横条纹" : "竖条纹")}) | 地址范围: FW{globalCol}-FW{endCol} | 写入长度: {writeStride} | {(fillHorizontalPadding ? "已填充" : "未填充")}"
                );

                for (int colIdx = 0; colIdx < writeStride; colIdx++)
                {
                    byte pixelValue = frameData[colIdx];
                    string fwCmd = $"{TjProjectorCommands.WriteFlashPixelPrefix}{globalCol} {pixelValue}{NewLine}";
                    await client.SendCommandAsync(fwCmd);
                    await Task.Delay(WriteDelayMs);

                    if (globalCol % 512 == 0)
                    {
                        int frameColInFrame = colIdx + 1;
                        string colorDesc = pixelValue == 0 ? "黑色" : (pixelValue == 255 ? "白色" : $"灰度{pixelValue}");
                        string pixelType = colIdx < frame.Length ? "有效像素" : "填充像素";
                        Console.WriteLine($"    FW{globalCol} {pixelValue} -> 帧{frameIdx:D2}, 列{frameColInFrame}/{writeStride}, {colorDesc}, {pixelType}");
                    }

                    globalCol++;
                    writtenCount++;

                    if (globalCol > 0 && globalCol % 256 == 0)
                    {
                        await client.ReadResponseAsync();
                    }

                    int currentProgress = (int)(writtenCount * 100L / totalWrites);
                    if (currentProgress > lastReportedProgress)
                    {
                        lastReportedProgress = currentProgress;
                        Console.Write($"\r进度: {currentProgress}% ({writtenCount}/{totalWrites})");
                    }
                }
            }

            Console.WriteLine();
            await Task.Delay(500);

            Console.WriteLine();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 条纹下载完成!");
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    static string BuildFringeOrientationCommand(int imageCount, int blockIndex)
    {
        byte[] bits = BuildAlternatingFringeOrientationBits(imageCount);
        return $"{TjProjectorCommands.SetFringeOrientationBitmapPrefix}{blockIndex} {bits[0]} {bits[1]} {bits[2]} {bits[3]}\r\n";
    }

    static byte[] BuildAlternatingFringeOrientationBits(int imageCount)
    {
        const int maxImages = 128;
        byte[] bits = new byte[16];
        int usableCount = Math.Clamp(imageCount, 0, maxImages);

        for (int frame = 0; frame < usableCount; frame++)
        {
            bool isHorizontalFrame = frame % 2 == 0;
            if (!isHorizontalFrame)
                continue;

            int byteIndex = frame / 8;
            int bitPosition = frame % 8;
            bits[byteIndex] = (byte)(bits[byteIndex] | (1 << bitPosition));
        }

        return bits;
    }
}
namespace FringeTool;

public static class FringeGenerator
{
    public static byte[][] GenerateFringePatterns(int widthPixels, int heightPixels, int periodCount, int phaseShift, int imageCount, string fringeType = "wb")
    {
        if (widthPixels <= 0 || heightPixels <= 0)
            throw new ArgumentException("条纹分辨率无效");

        if (widthPixels % periodCount != 0)
            throw new ArgumentException("条纹周期数必须能整除投影宽度像素数");

        if (heightPixels % periodCount != 0)
            throw new ArgumentException("条纹周期数必须能整除投影高度像素数");

        if (phaseShift <= 0 || phaseShift >= periodCount)
            throw new ArgumentException("相移量必须大于 0 且小于周期数");

        int horizontalStripeWidth = widthPixels / periodCount;
        int verticalStripeHeight = heightPixels / periodCount;

        byte firstColor = fringeType.Equals("wb", StringComparison.OrdinalIgnoreCase)
            ? (byte)255
            : (byte)0;
        byte secondColor = (byte)(255 - firstColor);

        int totalFrameCount = imageCount * 2;
        byte[][] images = new byte[totalFrameCount][];

        for (int i = 0; i < totalFrameCount; i++)
        {
            bool isHorizontalFrame = i % 2 == 0;
            int directionIndex = i / 2;
            int offset = directionIndex * phaseShift;

            if (isHorizontalFrame)
            {
                byte[] pixels = new byte[heightPixels];
                for (int y = 0; y < heightPixels; y++)
                {
                    int shifted = (y + offset) % heightPixels;
                    int stripeIdx = (shifted / verticalStripeHeight) % 2;
                    pixels[y] = stripeIdx == 0 ? firstColor : secondColor;
                }
                images[i] = pixels;
            }
            else
            {
                byte[] pixels = new byte[widthPixels];
                for (int x = 0; x < widthPixels; x++)
                {
                    int shifted = (x + offset) % widthPixels;
                    int stripeIdx = (shifted / horizontalStripeWidth) % 2;
                    pixels[x] = stripeIdx == 0 ? firstColor : secondColor;
                }
                images[i] = pixels;
            }
        }

        return images;
    }

    public static void SaveFringePatternsToFiles(byte[][] images, string outputDirectory, int widthPixels, int heightPixels)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        for (int i = 0; i < images.Length; i++)
        {
            bool isHorizontal = i % 2 == 0;
            string filename = $"fringe_{i:D2}_{(isHorizontal ? "H" : "V")}.raw";
            string path = Path.Combine(outputDirectory, filename);
            File.WriteAllBytes(path, images[i]);
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 条纹图像已保存到目录: {outputDirectory}");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 共生成 {images.Length} 幅图像");
    }
}
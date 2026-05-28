// KTECH 结构布局探针：通过反射 + Marshal.OffsetOf 输出每个字段在 wire 上的实际偏移
// 用于确认 Aurora 编解码器 (KtechStructCodec) 的偏移计算与 Demo C# 互通
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KtechStructSizeProbe;

internal static class Program
{
    /// <summary>支持的字段类型 → 字节数（仅用于报告，对齐由 Marshal.OffsetOf 给出真值）</summary>
    private static readonly Dictionary<Type, int> TypeSizes = new()
    {
        { typeof(byte), 1 },
        { typeof(sbyte), 1 },
        { typeof(ushort), 2 },
        { typeof(short), 2 },
        { typeof(uint), 4 },
        { typeof(int), 4 },
        { typeof(ulong), 8 },
        { typeof(long), 8 },
    };

    private static int Main(string[] args)
    {
        StringBuilder report = new();
        report.AppendLine("# KTECH Struct Layout Report");
        report.AppendLine();
        report.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(
            $"运行时: .NET {Environment.Version}, OS={RuntimeInformation.OSDescription}"
        );
        report.AppendLine();

        AppendStruct<saveCalibMsMfMh_struct>(report, "saveCalibMsMfMh_struct (MS/MF/MH 通用标定)");
        AppendStruct<saveCalibMg_struct>(report, "saveCalibMg_struct (MG/MG_E 标定)");
        AppendStruct<saveSetting_t>(report, "saveSetting_t (全型号设置)");

        string outPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "layout-report.txt"
        );
        outPath = Path.GetFullPath(outPath);
        File.WriteAllText(outPath, report.ToString(), Encoding.UTF8);

        // 同时打印到控制台
        Console.OutputEncoding = Encoding.UTF8;
        Console.Write(report.ToString());
        Console.WriteLine();
        Console.WriteLine($"报告已写入: {outPath}");
        return 0;
    }

    /// <summary>反射一个结构体，输出每个字段的偏移与大小</summary>
    private static void AppendStruct<T>(StringBuilder sb, string title)
        where T : struct
    {
        Type t = typeof(T);
        int size = Marshal.SizeOf<T>();

        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine($"- CLR Type: `{t.FullName}`");
        sb.AppendLine($"- `Marshal.SizeOf<T>()` = **{size} 字节**");
        sb.AppendLine();
        sb.AppendLine("| # | FieldName | Type | Offset (dec) | Offset (hex) | Size |");
        sb.AppendLine("|---|-----------|------|--------------|--------------|------|");

        FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
        int prevEnd = 0;
        int paddingTotal = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo f = fields[i];
            int offset = (int)Marshal.OffsetOf<T>(f.Name);
            int fSize = TypeSizes.TryGetValue(f.FieldType, out int s)
                ? s
                : Marshal.SizeOf(f.FieldType);

            // 检测填充
            if (offset > prevEnd && i > 0)
            {
                int pad = offset - prevEnd;
                paddingTotal += pad;
                sb.AppendLine($"|   | *(padding)* | — | {prevEnd} | 0x{prevEnd:X2} | {pad} |");
            }

            sb.AppendLine(
                $"| {i + 1} | `{f.Name}` | `{TypeName(f.FieldType)}` | {offset} | 0x{offset:X2} | {fSize} |"
            );
            prevEnd = offset + fSize;
        }

        // 尾部填充
        if (size > prevEnd)
        {
            int pad = size - prevEnd;
            paddingTotal += pad;
            sb.AppendLine($"|   | *(tail padding)* | — | {prevEnd} | 0x{prevEnd:X2} | {pad} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**有效字段总字节**: {size - paddingTotal}，**填充字节**: {paddingTotal}");
        sb.AppendLine();
    }

    private static string TypeName(Type t) =>
        t.Name switch
        {
            "Byte" => "u8",
            "SByte" => "s8",
            "UInt16" => "u16",
            "Int16" => "s16",
            "UInt32" => "u32",
            "Int32" => "s32",
            "UInt64" => "u64",
            "Int64" => "s64",
            _ => t.Name,
        };
}

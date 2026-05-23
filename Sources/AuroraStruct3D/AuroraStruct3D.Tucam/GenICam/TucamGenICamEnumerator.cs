using System.Globalization;
using System.Runtime.InteropServices;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Tucam.GenICam;

/// <summary>
/// 通过 SDK 动态枚举相机内部 GenICam NodeMap，
/// 移植自 Tools/TucamGenICamTest/GenICamNodeDumper.cs，去掉控制台/文件输出。
/// </summary>
public static class TucamGenICamEnumerator
{
    /// <summary>SDK 成功返回码（TUCamRet.Success）</summary>
    private const int TUCAM_SUCCESS = 1;

    /// <summary>TU_CAMERA_XML 域</summary>
    public const int TU_CAMERA_XML = 0;

    /// <summary>TU_CAMERABASE_XML 域</summary>
    public const int TU_CAMERABASE_XML = 3;

    /// <summary>单次枚举的最大节点数（防御性上限）</summary>
    private const int MaxNodeCount = 4096;

    /// <summary>
    /// 在指定相机句柄上枚举 GenICam 节点。
    /// 会先在 TU_CAMERA_XML 和 TU_CAMERABASE_XML 上分别枚举并合并结果。
    /// 每个 XML 域内部使用 seed 三选（"Root" / "" / null）策略，取节点数最多的一次。
    /// </summary>
    /// <param name="handle">已打开的相机句柄</param>
    /// <param name="logger">日志（可空）</param>
    /// <returns>合并后的节点列表（按 XmlScope + 原始 Index 顺序）</returns>
    public static GenICamNodeMap Enumerate(IntPtr handle, ILogger? logger = null)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("相机句柄无效", nameof(handle));
        }

        List<GenICamNodeMeta> all = new();
        HashSet<string> seenNames = new(StringComparer.Ordinal);

        foreach (int xml in new[] { TU_CAMERA_XML, TU_CAMERABASE_XML })
        {
            IReadOnlyList<GenICamNodeMeta> best = EnumerateBest(handle, xml, logger);
            foreach (GenICamNodeMeta node in best)
            {
                if (seenNames.Add(node.NodeName))
                {
                    all.Add(node);
                }
            }
        }

        Dictionary<string, GenICamNodeMeta> byName = all.ToDictionary(
            n => n.NodeName,
            StringComparer.Ordinal
        );

        return new GenICamNodeMap
        {
            EnumeratedAt = DateTime.UtcNow,
            Nodes = all,
            NodesByName = byName,
        };
    }

    /// <summary>seed 三选最佳：以节点数最多者为准</summary>
    private static IReadOnlyList<GenICamNodeMeta> EnumerateBest(
        IntPtr handle,
        int xml,
        ILogger? logger
    )
    {
        string?[] seeds = ["Root", string.Empty, null];
        IReadOnlyList<GenICamNodeMeta> best = Array.Empty<GenICamNodeMeta>();
        string bestSeed = "<none>";

        foreach (string? seed in seeds)
        {
            IReadOnlyList<GenICamNodeMeta> nodes = EnumerateBySeed(handle, xml, seed);
            if (nodes.Count > best.Count)
            {
                best = nodes;
                bestSeed =
                    seed is null ? "<null>"
                    : seed.Length == 0 ? "<empty>"
                    : seed;
            }
        }

        logger?.LogInformation(
            "[Cameras] GenICam Enumerate xml={Xml} bestSeed={Seed} count={Count}",
            xml,
            bestSeed,
            best.Count
        );

        return best;
    }

    private static IReadOnlyList<GenICamNodeMeta> EnumerateBySeed(
        IntPtr handle,
        int xml,
        string? seed
    )
    {
        List<GenICamNodeMeta> nodes = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        string? cursor = seed;

        for (int index = 0; index < MaxNodeCount; index++)
        {
            IntPtr pName = cursor is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(cursor);
            try
            {
                TucamElement element = default;
                TUCamRet ret = TUCamNative.TUCAM_GenICam_ElementAttrNext(
                    handle,
                    ref element,
                    pName,
                    xml
                );
                if ((int)ret != TUCAM_SUCCESS)
                {
                    break;
                }

                GenICamNodeMeta? node = CreateNodeMeta(handle, xml, index, element);
                if (node is null || string.IsNullOrWhiteSpace(node.NodeName))
                {
                    break;
                }

                if (!seen.Add(node.NodeName))
                {
                    break;
                }

                nodes.Add(node);
                cursor = node.NodeName;
            }
            finally
            {
                if (pName != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pName);
                }
            }
        }

        return nodes;
    }

    private static GenICamNodeMeta? CreateNodeMeta(
        IntPtr handle,
        int xml,
        int index,
        TucamElement element
    )
    {
        string nodeName = ReadAnsi(element.pName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return null;
        }

        IReadOnlyList<GenICamEnumEntry> enumEntries = ReadEnumEntries(element);
        string? currentValue = ReadCurrentValue(handle, xml, nodeName, element, enumEntries);

        return new GenICamNodeMeta
        {
            Index = index,
            XmlScope = xml,
            Level = element.Level,
            NodeName = nodeName,
            DisplayName = SanitizeString(ReadAnsi(element.pDisplayName)) ?? string.Empty,
            Type = element.Type,
            Access = element.Access,
            Visibility = element.Visibility,
            Representation = element.Representation,
            Unit = SanitizeString(ReadAnsi(element.pUnit)),
            Description = SanitizeString(ReadAnsi(element.pDesc)),
            IsLocked = element.IsLocked != 0,
            IntMin = element.uValue.IntValue.nMin,
            IntMax = element.uValue.IntValue.nMax,
            IntStep = element.uValue.IntValue.nStep,
            FloatMin = element.uValue.FloatValue.dbMin,
            FloatMax = element.uValue.FloatValue.dbMax,
            FloatStep = element.uValue.FloatValue.dbStep,
            CurrentValue = currentValue,
            EnumEntries = enumEntries,
            PollingTime = element.PollingTime,
            DisplayPrecision = element.DisplayPrecision,
        };
    }

    private static IReadOnlyList<GenICamEnumEntry> ReadEnumEntries(TucamElement element)
    {
        if (element.Type != TuElemType.Enumeration || element.pEntries == IntPtr.Zero)
        {
            return Array.Empty<GenICamEnumEntry>();
        }

        long min = element.uValue.IntValue.nMin;
        long max = element.uValue.IntValue.nMax;
        if (max < min || max - min + 1 <= 0 || max - min + 1 > 512)
        {
            return Array.Empty<GenICamEnumEntry>();
        }

        int count = (int)(max - min + 1);
        List<GenICamEnumEntry> entries = new(count);
        for (int i = 0; i < count; i++)
        {
            IntPtr entryPointer = Marshal.ReadIntPtr(element.pEntries, i * IntPtr.Size);
            if (entryPointer == IntPtr.Zero)
            {
                continue;
            }

            string? symbolic = SanitizeString(ReadAnsi(entryPointer));
            if (string.IsNullOrWhiteSpace(symbolic))
            {
                continue;
            }

            entries.Add(
                new GenICamEnumEntry
                {
                    Value = min + i,
                    Symbolic = symbolic,
                    DisplayName = symbolic,
                    IsAvailable = true,
                }
            );
        }

        return entries;
    }

    private static string? ReadCurrentValue(
        IntPtr handle,
        int xml,
        string nodeName,
        TucamElement attrElement,
        IReadOnlyList<GenICamEnumEntry> enumEntries
    )
    {
        // Command / Category / Port 没有当前值
        if (attrElement.Type is TuElemType.Command or TuElemType.Category or TuElemType.Port)
        {
            return null;
        }

        // 不可读访问模式直接跳过
        if (attrElement.Access is not (TuAccessMode.ReadOnly or TuAccessMode.ReadWrite))
        {
            return null;
        }

        if (attrElement.Type == TuElemType.String)
        {
            return ReadStringValueByAttr(handle, xml, nodeName);
        }

        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement current = default;
            current.pName = pName;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref current, xml);
            if ((int)ret != TUCAM_SUCCESS)
            {
                return null;
            }

            return attrElement.Type switch
            {
                TuElemType.Float => current.uValue.FloatValue.dbVal.ToString(
                    "G",
                    CultureInfo.InvariantCulture
                ),
                _ => current.uValue.IntValue.nVal.ToString(CultureInfo.InvariantCulture),
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static string? ReadStringValueByAttr(IntPtr handle, int xml, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement element = default;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_ElementAttr(handle, ref element, pName, xml);
            if ((int)ret != TUCAM_SUCCESS)
            {
                return null;
            }

            return SanitizeString(ReadAnsi(element.pTransfer));
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static string? ReadAnsi(IntPtr pointer)
    {
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(pointer);
    }

    /// <summary>剔除控制字符（保留 \t \r \n）</summary>
    private static string? SanitizeString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        char[] buffer = new char[value.Length];
        int length = 0;
        foreach (char ch in value)
        {
            if (char.IsControl(ch) && ch != '\t' && ch != '\r' && ch != '\n')
            {
                continue;
            }

            buffer[length++] = ch;
        }

        return length == value.Length ? value : new string(buffer, 0, length);
    }
}

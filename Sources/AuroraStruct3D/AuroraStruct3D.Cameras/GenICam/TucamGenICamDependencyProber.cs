using System.Globalization;
using System.Runtime.InteropServices;
using AuroraStruct3D.Cameras.Tucam.Interop;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Cameras.Tucam.GenICam;

/// <summary>
/// 选择器依赖探测：枚举每个 Selector/Mode 节点的所有选项，
/// 切换值后再次枚举，对比快照得到该选择器影响的下游节点集合。
/// 探测结束后通过 try/finally 恢复选择器原值，避免污染相机状态。
/// </summary>
public static class TucamGenICamDependencyProber
{
    private const int TUCAM_SUCCESS = 1;

    /// <summary>
    /// 探测整个 NodeMap 的选择器依赖。
    /// </summary>
    /// <param name="handle">已打开的相机句柄</param>
    /// <param name="baselineMap">基线 NodeMap（一般为 OpenCamera 后立即枚举的结果）</param>
    /// <param name="logger">日志（可空）</param>
    public static GenICamDependencyGraph Probe(
        IntPtr handle,
        GenICamNodeMap baselineMap,
        ILogger? logger = null
    )
    {
        List<GenICamDependencyEdge> edges = new();

        // 仅在 TU_CAMERA_XML 域探测选择器（TU_CAMERABASE_XML 一般是相机不变属性）
        const int xml = TucamGenICamEnumerator.TU_CAMERA_XML;

        List<GenICamNodeMeta> selectors = baselineMap
            .Nodes.Where(n =>
                n.XmlScope == xml && IsSelectorCandidate(n) && n.EnumEntries.Count > 1
            )
            .OrderBy(n => n.NodeName, StringComparer.Ordinal)
            .ToList();

        logger?.LogInformation(
            "[Cameras] GenICam DependencyProber 候选选择器 {Count} 个",
            selectors.Count
        );

        foreach (GenICamNodeMeta selector in selectors)
        {
            ProbeSelector(handle, xml, baselineMap.Nodes, selector, edges, logger);
        }

        Dictionary<string, IReadOnlyList<string>> affected = edges
            .GroupBy(e => e.SelectorNode, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyList<string>)
                        g.Select(e => e.AffectedNode).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal
            );

        return new GenICamDependencyGraph
        {
            ProbedAt = DateTime.UtcNow,
            Edges = edges,
            AffectedBySelector = affected,
        };
    }

    private static void ProbeSelector(
        IntPtr handle,
        int xml,
        IReadOnlyList<GenICamNodeMeta> baseline,
        GenICamNodeMeta selector,
        List<GenICamDependencyEdge> sink,
        ILogger? logger
    )
    {
        if (!TryGetIntegerValue(handle, xml, selector.NodeName, out long originalValue))
        {
            logger?.LogDebug("[Cameras] Selector {Node} 无法读取原值，跳过", selector.NodeName);
            return;
        }

        try
        {
            foreach (GenICamEnumEntry option in selector.EnumEntries)
            {
                if (option.Value == originalValue)
                {
                    continue;
                }

                TUCamRet setRet = SetIntegerValueRaw(handle, xml, selector.NodeName, option.Value);
                if ((int)setRet != TUCAM_SUCCESS)
                {
                    continue;
                }

                GenICamNodeMap snapshot = TucamGenICamEnumerator.Enumerate(handle, logger: null);
                CompareNodes(baseline, snapshot.Nodes, selector, option, sink);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[Cameras] Selector {Node} 探测异常", selector.NodeName);
        }
        finally
        {
            SetIntegerValueRaw(handle, xml, selector.NodeName, originalValue);
        }
    }

    private static void CompareNodes(
        IReadOnlyList<GenICamNodeMeta> baseline,
        IReadOnlyList<GenICamNodeMeta> snapshot,
        GenICamNodeMeta selector,
        GenICamEnumEntry option,
        List<GenICamDependencyEdge> sink
    )
    {
        Dictionary<string, GenICamNodeMeta> beforeMap = baseline.ToDictionary(
            n => n.NodeName,
            StringComparer.Ordinal
        );

        foreach (GenICamNodeMeta after in snapshot)
        {
            if (string.Equals(after.NodeName, selector.NodeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!beforeMap.TryGetValue(after.NodeName, out GenICamNodeMeta? before))
            {
                sink.Add(MakeEdge(selector, option, after.NodeName, "新增节点"));
                continue;
            }

            List<string> changedFields = new();

            if (before.Access != after.Access)
            {
                changedFields.Add($"Access {before.Access}->{after.Access}");
            }

            if (before.Visibility != after.Visibility)
            {
                changedFields.Add($"Visibility {before.Visibility}->{after.Visibility}");
            }

            if (!AreEquivalentValues(before.CurrentValue, after.CurrentValue))
            {
                changedFields.Add("Value");
            }

            if (
                before.IntMin != after.IntMin
                || before.IntMax != after.IntMax
                || before.IntStep != after.IntStep
            )
            {
                changedFields.Add("IntRange");
            }

            if (
                !DoubleEquals(before.FloatMin, after.FloatMin)
                || !DoubleEquals(before.FloatMax, after.FloatMax)
                || !DoubleEquals(before.FloatStep, after.FloatStep)
            )
            {
                changedFields.Add("FloatRange");
            }

            if (before.EnumEntries.Count != after.EnumEntries.Count)
            {
                changedFields.Add("EnumEntries");
            }

            if (changedFields.Count == 0)
            {
                continue;
            }

            // 仅 Value 变化、且节点名以 Counter 结尾，视为运行态波动，忽略
            if (
                changedFields.Count == 1
                && changedFields[0] == "Value"
                && after.NodeName.EndsWith("Counter", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            // 提取受影响节点的新 Access 状态（供前端免重枚举更新 Access）
            string? newAccess = before.Access != after.Access ? after.Access.ToString() : null;

            sink.Add(
                MakeEdge(
                    selector,
                    option,
                    after.NodeName,
                    string.Join("; ", changedFields),
                    newAccess
                )
            );
        }
    }

    private static GenICamDependencyEdge MakeEdge(
        GenICamNodeMeta selector,
        GenICamEnumEntry option,
        string affected,
        string summary,
        string? newAccess = null
    )
    {
        return new GenICamDependencyEdge
        {
            SelectorNode = selector.NodeName,
            OptionValue = option.Value,
            OptionLabel = option.Symbolic,
            AffectedNode = affected,
            ChangeSummary = summary,
            NewAccess = newAccess,
        };
    }

    private static bool IsSelectorCandidate(GenICamNodeMeta n)
    {
        // Option B：放宽到所有可写枚举节点，确保 ExposureAuto/GainAuto/BalanceWhiteAuto 等
        // 非 *Selector/*Mode 命名也能被探测进依赖图（噪声由 IsVolatileValueOnlyChange 过滤）。
        return n.Type == TuElemType.Enumeration && n.Access == TuAccessMode.ReadWrite;
    }

    private static bool AreEquivalentValues(string? before, string? after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return true;
        }

        if (
            double.TryParse(before, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
            && double.TryParse(
                after,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double b
            )
        )
        {
            return DoubleEquals(a, b);
        }

        return false;
    }

    private static bool DoubleEquals(double a, double b)
    {
        double tol = Math.Max(0.000001, Math.Max(Math.Abs(a), Math.Abs(b)) * 0.000001);
        return Math.Abs(a - b) <= tol;
    }

    private static bool TryGetIntegerValue(IntPtr handle, int xml, string nodeName, out long value)
    {
        value = 0;
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement element = default;
            element.pName = pName;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref element, xml);
            if ((int)ret != TUCAM_SUCCESS)
            {
                return false;
            }

            value = element.uValue.IntValue.nVal;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static TUCamRet SetIntegerValueRaw(IntPtr handle, int xml, string nodeName, long value)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement element = default;
            element.pName = pName;
            TUCamRet getRet = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref element, xml);
            if ((int)getRet != TUCAM_SUCCESS)
            {
                return getRet;
            }

            element.pName = pName;
            element.uValue.IntValue.nVal = value;
            return TUCamNative.TUCAM_GenICam_SetElementValue(handle, ref element, xml);
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }
}

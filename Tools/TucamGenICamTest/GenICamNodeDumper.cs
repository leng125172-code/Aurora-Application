using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TucamGenICamTest;

/// <summary>
/// 从 TUCam DLL 直接枚举并打印相机内部 GenICam NodeMap。
/// </summary>
internal static class GenICamNodeDumper
{
    private const int TUCAM_SUCCESS = 1;
    private const int TU_CAMERA_XML = 0;
    private const int TU_CAMERABASE_XML = 3;
    private const int MaxNodeCount = 4096;

    private sealed class GenICamNodeInfo
    {
        public int Index { get; init; }

        public int Xml { get; init; }

        public byte Level { get; init; }

        public string NodeName { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public string Access { get; init; } = string.Empty;

        public string Visibility { get; init; } = string.Empty;

        public string Representation { get; init; } = string.Empty;

        public string CurrentValue { get; init; } = string.Empty;

        public string Range { get; init; } = string.Empty;

        public string Unit { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string EnumEntries { get; init; } = string.Empty;

        public bool IsLocked { get; init; }
    }

    private sealed class EnumOption
    {
        public long Value { get; init; }

        public string Label { get; init; } = string.Empty;

        public override string ToString()
        {
            return $"{Value}:{Label}";
        }
    }

    private sealed class ProbeChange
    {
        public string SelectorNode { get; init; } = string.Empty;

        public long OptionValue { get; init; }

        public string OptionLabel { get; init; } = string.Empty;

        public string AffectedNode { get; init; } = string.Empty;

        public string ChangeSummary { get; init; } = string.Empty;

        public string AccessBefore { get; init; } = string.Empty;

        public string AccessAfter { get; init; } = string.Empty;

        public string VisibilityBefore { get; init; } = string.Empty;

        public string VisibilityAfter { get; init; } = string.Empty;

        public string ValueBefore { get; init; } = string.Empty;

        public string ValueAfter { get; init; } = string.Empty;

        public string RangeBefore { get; init; } = string.Empty;

        public string RangeAfter { get; init; } = string.Empty;

        public string EnumEntriesBefore { get; init; } = string.Empty;

        public string EnumEntriesAfter { get; init; } = string.Empty;

        public string ToDisplayString()
        {
            return $"* {AffectedNode}: {ChangeSummary}";
        }
    }

    /// <summary>
    /// 打印相机内部 GenICam 节点列表。
    /// </summary>
    public static void DumpAll(IntPtr handle)
    {
        Console.WriteLine();
        Console.WriteLine("─── GenICam NodeMap 动态枚举（直接读取 DLL / 相机内部 XML） ─────────");
        DumpXml(handle, TU_CAMERA_XML, "TU_CAMERA_XML(0)");
        DumpXml(handle, TU_CAMERABASE_XML, "TU_CAMERABASE_XML(3)");
    }

    /// <summary>
    /// 通过切换选择器枚举值，探测节点访问权限、可见性和值的动态变化。
    /// </summary>
    public static void ProbeSelectorDependencies(IntPtr handle)
    {
        Console.WriteLine();
        Console.WriteLine("─── GenICam 选择器依赖探测（会临时写入枚举选择器并恢复原值） ─────────");
        ProbeSelectorDependencies(handle, TU_CAMERA_XML, "TU_CAMERA_XML(0)");
    }

    private static void DumpXml(IntPtr handle, int xml, string xmlName)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {xmlName} ===");

        string?[] seeds = [null, string.Empty, "Root"];
        List<GenICamNodeInfo> bestNodes = [];
        string bestSeed = "<none>";
        List<string> attemptLogs = [];

        foreach (string? seed in seeds)
        {
            List<GenICamNodeInfo> nodes = EnumerateBySeed(handle, xml, seed, out int firstRet);
            string seedLabel;
            if (seed is null)
            {
                seedLabel = "<null>";
            }
            else if (seed.Length == 0)
            {
                seedLabel = "<empty>";
            }
            else
            {
                seedLabel = seed;
            }
            attemptLogs.Add($"  seed={seedLabel, -7} firstRet=0x{firstRet:X8} count={nodes.Count}");
            if (nodes.Count > bestNodes.Count)
            {
                bestNodes = nodes;
                bestSeed = seedLabel;
            }
        }

        foreach (string log in attemptLogs)
        {
            Console.WriteLine(log);
        }

        if (bestNodes.Count == 0)
        {
            Console.WriteLine(
                "  未能通过 ElementAttrNext 枚举到节点。请确认当前 SDK/DLL 是否支持该接口，或相机是否已完成 GenICam XML 加载。"
            );
            return;
        }

        Console.WriteLine($"  采用 seed={bestSeed}，枚举到 {bestNodes.Count} 个节点。");
        PrintSummary(bestNodes);
        PrintSelectorCandidates(bestNodes);

        string dumpDirectory = Path.Combine(AppContext.BaseDirectory, "GenICamDumps");
        Directory.CreateDirectory(dumpDirectory);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string xmlFileName = xmlName.Replace("(", "_").Replace(")", string.Empty);
        string textPath = Path.Combine(
            dumpDirectory,
            $"genicam_nodes_{xmlFileName}_{timestamp}.txt"
        );
        string csvPath = Path.Combine(
            dumpDirectory,
            $"genicam_nodes_{xmlFileName}_{timestamp}.csv"
        );

        using StreamWriter textWriter = new(textPath, false);
        using StreamWriter csvWriter = new(csvPath, false);
        textWriter.WriteLine($"GenICam NodeMap Dump - {xmlName}");
        textWriter.WriteLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        textWriter.WriteLine($"节点数: {bestNodes.Count}");
        textWriter.WriteLine();
        csvWriter.WriteLine(
            Csv(
                "Index",
                "Xml",
                "Level",
                "NodeName",
                "DisplayName",
                "Type",
                "Access",
                "Visibility",
                "Representation",
                "CurrentValue",
                "Range",
                "Unit",
                "IsLocked",
                "EnumEntries",
                "Description"
            )
        );

        Console.WriteLine();
        Console.WriteLine("  节点明细：");
        foreach (GenICamNodeInfo node in bestNodes)
        {
            string indent = new(' ', Math.Min(node.Level, (byte)20) * 2);
            string line =
                $"  {node.Index:D4} {indent}{node.NodeName}"
                + $"  Type={node.Type} Access={node.Access} Visibility={node.Visibility}"
                + $" Value={FormatInline(node.CurrentValue)} Range={FormatInline(node.Range)} Unit={FormatInline(node.Unit)}";
            Console.WriteLine(line);
            textWriter.WriteLine(line);

            if (!string.IsNullOrWhiteSpace(node.DisplayName))
            {
                textWriter.WriteLine($"       DisplayName: {ToPrintable(node.DisplayName)}");
            }
            if (!string.IsNullOrWhiteSpace(node.EnumEntries))
            {
                textWriter.WriteLine($"       EnumEntries: {ToPrintable(node.EnumEntries)}");
            }
            if (!string.IsNullOrWhiteSpace(node.Description))
            {
                textWriter.WriteLine($"       Description: {ToPrintable(node.Description)}");
            }

            csvWriter.WriteLine(
                Csv(
                    node.Index.ToString(CultureInfo.InvariantCulture),
                    node.Xml.ToString(CultureInfo.InvariantCulture),
                    node.Level.ToString(CultureInfo.InvariantCulture),
                    node.NodeName,
                    node.DisplayName,
                    node.Type,
                    node.Access,
                    node.Visibility,
                    node.Representation,
                    node.CurrentValue,
                    node.Range,
                    node.Unit,
                    node.IsLocked ? "1" : "0",
                    node.EnumEntries,
                    node.Description
                )
            );
        }

        Console.WriteLine($"  已保存 TXT: {textPath}");
        Console.WriteLine($"  已保存 CSV: {csvPath}");
    }

    private static void ProbeSelectorDependencies(IntPtr handle, int xml, string xmlName)
    {
        List<GenICamNodeInfo> baseline = EnumerateBest(handle, xml, out string seedLabel);
        if (baseline.Count == 0)
        {
            Console.WriteLine($"  {xmlName}: 未枚举到节点，跳过依赖探测。");
            return;
        }

        List<GenICamNodeInfo> selectors = baseline
            .Where(IsSelectorCandidate)
            .Where(node => ParseEnumOptions(node.EnumEntries).Count > 1)
            .OrderBy(node => node.NodeName, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"  {xmlName}: seed={seedLabel}，候选选择器 {selectors.Count} 个。");
        if (selectors.Count == 0)
        {
            return;
        }

        string dumpDirectory = Path.Combine(AppContext.BaseDirectory, "GenICamDumps");
        Directory.CreateDirectory(dumpDirectory);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string textPath = Path.Combine(dumpDirectory, $"genicam_selector_probe_{timestamp}.txt");
        string jsonPath = Path.Combine(dumpDirectory, $"genicam_selector_probe_{timestamp}.json");
        string csvPath = Path.Combine(dumpDirectory, $"genicam_selector_probe_{timestamp}.csv");
        List<ProbeChange> allChanges = [];

        using StreamWriter writer = new(textPath, false);
        writer.WriteLine($"GenICam Selector Probe - {xmlName}");
        writer.WriteLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"基础节点数: {baseline.Count}");
        writer.WriteLine();

        foreach (GenICamNodeInfo selector in selectors)
        {
            ProbeSelector(handle, xml, baseline, selector, writer, allChanges);
        }

        WriteProbeJson(jsonPath, allChanges);
        WriteProbeCsv(csvPath, allChanges);

        Console.WriteLine($"  选择器依赖探测已保存: {textPath}");
        Console.WriteLine($"  结构化 JSON: {jsonPath}");
        Console.WriteLine($"  结构化 CSV: {csvPath}");
    }

    private static List<GenICamNodeInfo> EnumerateBest(IntPtr handle, int xml, out string bestSeed)
    {
        string?[] seeds = ["Root", string.Empty, null];
        List<GenICamNodeInfo> bestNodes = [];
        bestSeed = "<none>";

        foreach (string? seed in seeds)
        {
            List<GenICamNodeInfo> nodes = EnumerateBySeed(handle, xml, seed, out _);
            if (nodes.Count <= bestNodes.Count)
            {
                continue;
            }

            bestNodes = nodes;
            bestSeed =
                seed is null ? "<null>"
                : seed.Length == 0 ? "<empty>"
                : seed;
        }

        return bestNodes;
    }

    private static void ProbeSelector(
        IntPtr handle,
        int xml,
        IReadOnlyList<GenICamNodeInfo> baseline,
        GenICamNodeInfo selector,
        TextWriter writer,
        List<ProbeChange> allChanges
    )
    {
        List<EnumOption> options = ParseEnumOptions(selector.EnumEntries);
        if (!TryGetIntegerValue(handle, xml, selector.NodeName, out long originalValue))
        {
            string message = $"  [{selector.NodeName}] 无法读取原值，跳过。";
            Console.WriteLine(message);
            writer.WriteLine(message);
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            $"  [{selector.NodeName}] 原值={originalValue}，选项={string.Join(" | ", options)}"
        );
        writer.WriteLine(
            $"[{selector.NodeName}] 原值={originalValue}，选项={string.Join(" | ", options)}"
        );

        try
        {
            foreach (EnumOption option in options)
            {
                int setRet = SetIntegerValue(handle, xml, selector.NodeName, option.Value);
                if (setRet != TUCAM_SUCCESS)
                {
                    string message = $"    set {option} 失败 ret=0x{setRet:X8}";
                    Console.WriteLine(message);
                    writer.WriteLine(message);
                    continue;
                }

                List<GenICamNodeInfo> snapshot = EnumerateBest(handle, xml, out _);
                List<ProbeChange> changes = CompareNodes(baseline, snapshot, selector, option);
                allChanges.AddRange(changes);
                Console.WriteLine($"    set {option}: 变化 {changes.Count} 项");
                writer.WriteLine($"  set {option}: 变化 {changes.Count} 项");
                WriteChanges(writer, changes, int.MaxValue);
                WriteChanges(Console.Out, changes, 20);
            }
        }
        finally
        {
            int restoreRet = SetIntegerValue(handle, xml, selector.NodeName, originalValue);
            Console.WriteLine(
                $"    恢复 {selector.NodeName}={originalValue}: ret=0x{restoreRet:X8}"
            );
            writer.WriteLine($"  恢复 {selector.NodeName}={originalValue}: ret=0x{restoreRet:X8}");
            writer.WriteLine();
        }
    }

    private static List<ProbeChange> CompareNodes(
        IReadOnlyList<GenICamNodeInfo> baseline,
        IReadOnlyList<GenICamNodeInfo> snapshot,
        GenICamNodeInfo selector,
        EnumOption option
    )
    {
        Dictionary<string, GenICamNodeInfo> beforeMap = baseline.ToDictionary(
            node => node.NodeName,
            StringComparer.Ordinal
        );
        Dictionary<string, GenICamNodeInfo> afterMap = snapshot.ToDictionary(
            node => node.NodeName,
            StringComparer.Ordinal
        );
        List<ProbeChange> changes = [];

        foreach (GenICamNodeInfo after in snapshot)
        {
            if (string.Equals(after.NodeName, selector.NodeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!beforeMap.TryGetValue(after.NodeName, out GenICamNodeInfo? before))
            {
                changes.Add(
                    CreateProbeChange(
                        selector,
                        option,
                        after.NodeName,
                        $"新增节点 Type={after.Type} Access={after.Access}",
                        null,
                        after
                    )
                );
                continue;
            }

            List<string> fields = [];
            AddTextFieldChange(fields, "Access", before.Access, after.Access);
            AddTextFieldChange(fields, "Visibility", before.Visibility, after.Visibility);
            AddValueFieldChange(fields, "Value", before.CurrentValue, after.CurrentValue);
            AddTextFieldChange(fields, "Range", before.Range, after.Range);
            AddTextFieldChange(fields, "EnumEntries", before.EnumEntries, after.EnumEntries);

            if (IsVolatileValueOnlyChange(after.NodeName, fields))
            {
                continue;
            }

            if (fields.Count > 0)
            {
                changes.Add(
                    CreateProbeChange(
                        selector,
                        option,
                        after.NodeName,
                        string.Join("; ", fields),
                        before,
                        after
                    )
                );
            }
        }

        foreach (GenICamNodeInfo before in baseline)
        {
            if (!afterMap.ContainsKey(before.NodeName))
            {
                changes.Add(
                    CreateProbeChange(selector, option, before.NodeName, "节点消失", before, null)
                );
            }
        }

        return changes;
    }

    private static ProbeChange CreateProbeChange(
        GenICamNodeInfo selector,
        EnumOption option,
        string affectedNode,
        string summary,
        GenICamNodeInfo? before,
        GenICamNodeInfo? after
    )
    {
        return new ProbeChange
        {
            SelectorNode = selector.NodeName,
            OptionValue = option.Value,
            OptionLabel = option.Label,
            AffectedNode = affectedNode,
            ChangeSummary = summary,
            AccessBefore = before?.Access ?? string.Empty,
            AccessAfter = after?.Access ?? string.Empty,
            VisibilityBefore = before?.Visibility ?? string.Empty,
            VisibilityAfter = after?.Visibility ?? string.Empty,
            ValueBefore = before?.CurrentValue ?? string.Empty,
            ValueAfter = after?.CurrentValue ?? string.Empty,
            RangeBefore = before?.Range ?? string.Empty,
            RangeAfter = after?.Range ?? string.Empty,
            EnumEntriesBefore = before?.EnumEntries ?? string.Empty,
            EnumEntriesAfter = after?.EnumEntries ?? string.Empty,
        };
    }

    private static void AddTextFieldChange(
        List<string> fields,
        string name,
        string before,
        string after
    )
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        fields.Add($"{name} {FormatInline(before)} -> {FormatInline(after)}");
    }

    private static void AddValueFieldChange(
        List<string> fields,
        string name,
        string before,
        string after
    )
    {
        if (AreEquivalentProbeValues(before, after))
        {
            return;
        }

        fields.Add($"{name} {FormatInline(before)} -> {FormatInline(after)}");
    }

    private static bool AreEquivalentProbeValues(string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return true;
        }

        if (
            double.TryParse(
                before,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double beforeValue
            )
            && double.TryParse(
                after,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double afterValue
            )
        )
        {
            double tolerance = Math.Max(
                0.000001,
                Math.Max(Math.Abs(beforeValue), Math.Abs(afterValue)) * 0.000001
            );
            return Math.Abs(beforeValue - afterValue) <= tolerance;
        }

        return false;
    }

    private static bool IsVolatileValueOnlyChange(string nodeName, IReadOnlyList<string> fields)
    {
        return fields.Count == 1
            && fields[0].StartsWith("Value ", StringComparison.Ordinal)
            && nodeName.EndsWith("Counter", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteChanges(
        TextWriter writer,
        IReadOnlyList<ProbeChange> changes,
        int maxCount
    )
    {
        int count = Math.Min(changes.Count, maxCount);
        for (int index = 0; index < count; index++)
        {
            writer.WriteLine($"      {changes[index].ToDisplayString()}");
        }

        if (changes.Count > maxCount)
        {
            writer.WriteLine($"      ... 还有 {changes.Count - maxCount} 项，完整内容见探测文件");
        }
    }

    private static void WriteProbeJson(string path, IReadOnlyList<ProbeChange> changes)
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(changes, options));
    }

    private static void WriteProbeCsv(string path, IReadOnlyList<ProbeChange> changes)
    {
        using StreamWriter writer = new(path, false);
        writer.WriteLine(
            Csv(
                "SelectorNode",
                "OptionValue",
                "OptionLabel",
                "AffectedNode",
                "ChangeSummary",
                "AccessBefore",
                "AccessAfter",
                "VisibilityBefore",
                "VisibilityAfter",
                "ValueBefore",
                "ValueAfter",
                "RangeBefore",
                "RangeAfter",
                "EnumEntriesBefore",
                "EnumEntriesAfter"
            )
        );

        foreach (ProbeChange change in changes)
        {
            writer.WriteLine(
                Csv(
                    change.SelectorNode,
                    change.OptionValue.ToString(CultureInfo.InvariantCulture),
                    change.OptionLabel,
                    change.AffectedNode,
                    change.ChangeSummary,
                    change.AccessBefore,
                    change.AccessAfter,
                    change.VisibilityBefore,
                    change.VisibilityAfter,
                    change.ValueBefore,
                    change.ValueAfter,
                    change.RangeBefore,
                    change.RangeAfter,
                    change.EnumEntriesBefore,
                    change.EnumEntriesAfter
                )
            );
        }
    }

    private static bool TryGetIntegerValue(IntPtr handle, int xml, string nodeName, out long value)
    {
        value = 0;
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement element = default;
            element.pName = pName;
            int ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref element, xml);
            if (ret != TUCAM_SUCCESS)
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

    private static int SetIntegerValue(IntPtr handle, int xml, string nodeName, long value)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement element = default;
            element.pName = pName;
            int getRet = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref element, xml);
            if (getRet != TUCAM_SUCCESS)
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

    private static List<GenICamNodeInfo> EnumerateBySeed(
        IntPtr handle,
        int xml,
        string? seed,
        out int firstRet
    )
    {
        List<GenICamNodeInfo> nodes = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string? cursor = seed;
        firstRet = 0;

        for (int index = 0; index < MaxNodeCount; index++)
        {
            IntPtr pName = cursor is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(cursor);
            try
            {
                TucamElement element = default;
                int ret = TUCamNative.TUCAM_GenICam_ElementAttrNext(
                    handle,
                    ref element,
                    pName,
                    xml
                );
                if (index == 0)
                {
                    firstRet = ret;
                }

                if (ret != TUCAM_SUCCESS)
                {
                    break;
                }

                GenICamNodeInfo node = CreateNodeInfo(handle, xml, index, element);
                if (string.IsNullOrWhiteSpace(node.NodeName))
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

    private static GenICamNodeInfo CreateNodeInfo(
        IntPtr handle,
        int xml,
        int index,
        TucamElement element
    )
    {
        string nodeName = ReadAnsi(element.pName) ?? string.Empty;
        string type = TypeName(element.Type);
        List<string> enumEntries = ReadEnumEntries(element);
        string currentValue = ReadCurrentValue(handle, xml, nodeName, element, enumEntries);

        return new GenICamNodeInfo
        {
            Index = index,
            Xml = xml,
            Level = element.Level,
            NodeName = nodeName,
            DisplayName = ReadAnsi(element.pDisplayName) ?? string.Empty,
            Type = type,
            Access = AccessName(element.Access),
            Visibility = VisibilityName(element.Visibility),
            Representation = RepresentationName(element.Representation),
            CurrentValue = currentValue,
            Range = BuildRange(element),
            Unit = ReadAnsi(element.pUnit) ?? string.Empty,
            Description = ReadAnsi(element.pDesc) ?? string.Empty,
            EnumEntries = string.Join(" | ", enumEntries),
            IsLocked = element.IsLocked != 0,
        };
    }

    private static string ReadCurrentValue(
        IntPtr handle,
        int xml,
        string nodeName,
        TucamElement attrElement,
        IReadOnlyList<string> enumEntries
    )
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return string.Empty;
        }

        if (!CanRead(attrElement.Access) || attrElement.Type is 4 or 8 or 11)
        {
            return string.Empty;
        }

        if (attrElement.Type == 6)
        {
            return ReadStringValueByAttr(handle, xml, nodeName);
        }

        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement current = default;
            current.pName = pName;
            int ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref current, xml);
            if (ret != TUCAM_SUCCESS)
            {
                return $"<读取失败 0x{ret:X8}>";
            }

            return attrElement.Type switch
            {
                5 => current.uValue.FloatValue.dbVal.ToString("G", CultureInfo.InvariantCulture),
                2 or 3 or 9 or 10 => FormatIntegerValue(current.uValue.IntValue.nVal, enumEntries),
                _ => current.uValue.IntValue.nVal.ToString(CultureInfo.InvariantCulture),
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static string ReadStringValueByAttr(IntPtr handle, int xml, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement element = default;
            int ret = TUCamNative.TUCAM_GenICam_ElementAttr(handle, ref element, pName, xml);
            if (ret != TUCAM_SUCCESS)
            {
                return $"<读取失败 0x{ret:X8}>";
            }

            return ToPrintable(ReadAnsi(element.pTransfer) ?? string.Empty);
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static List<string> ReadEnumEntries(TucamElement element)
    {
        List<string> entries = [];
        if (element.Type != 9 || element.pEntries == IntPtr.Zero)
        {
            return entries;
        }

        long min = element.uValue.IntValue.nMin;
        long max = element.uValue.IntValue.nMax;
        if (max < min || max - min + 1 <= 0 || max - min + 1 > 512)
        {
            return entries;
        }

        int count = (int)(max - min + 1);
        for (int index = 0; index < count; index++)
        {
            IntPtr entryPointer = Marshal.ReadIntPtr(element.pEntries, index * IntPtr.Size);
            if (entryPointer == IntPtr.Zero)
            {
                continue;
            }

            string? entry = ReadAnsi(entryPointer);
            if (!string.IsNullOrWhiteSpace(entry))
            {
                entries.Add($"{min + index}:{ToPrintable(entry)}");
            }
        }

        return entries.Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<EnumOption> ParseEnumOptions(string enumEntries)
    {
        if (string.IsNullOrWhiteSpace(enumEntries))
        {
            return [];
        }

        List<EnumOption> options = [];
        foreach (string entry in enumEntries.Split(" | ", StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = entry.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            string valueText = entry[..separatorIndex];
            if (!long.TryParse(valueText, CultureInfo.InvariantCulture, out long value))
            {
                continue;
            }

            options.Add(new EnumOption { Value = value, Label = entry[(separatorIndex + 1)..] });
        }

        return options
            .GroupBy(option => option.Value)
            .Select(group => group.First())
            .OrderBy(option => option.Value)
            .ToList();
    }

    private static string FormatIntegerValue(long value, IReadOnlyList<string> enumEntries)
    {
        if (enumEntries.Count == 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        string prefix = value.ToString(CultureInfo.InvariantCulture) + ":";
        string? entry = enumEntries.FirstOrDefault(e =>
            e.StartsWith(prefix, StringComparison.Ordinal)
        );
        return entry is null ? value.ToString(CultureInfo.InvariantCulture) : entry;
    }

    private static bool CanRead(int access) => access is 3 or 4;

    private static string BuildRange(TucamElement element)
    {
        return element.Type switch
        {
            2 or 3 or 9 or 10 => FormatRange(
                element.uValue.IntValue.nMin,
                element.uValue.IntValue.nMax,
                element.uValue.IntValue.nStep,
                element.uValue.IntValue.nDefault
            ),
            5 => FormatRange(
                element.uValue.FloatValue.dbMin,
                element.uValue.FloatValue.dbMax,
                element.uValue.FloatValue.dbStep,
                element.uValue.FloatValue.dbDefault
            ),
            6 => FormatStringLengthRange(element),
            _ => string.Empty,
        };
    }

    private static string FormatStringLengthRange(TucamElement element)
    {
        long min = element.uValue.IntValue.nMin;
        long max = element.uValue.IntValue.nMax;
        if (min == 0 && max == 0)
        {
            return string.Empty;
        }

        return $"len {min}..{max}";
    }

    private static string FormatRange(long min, long max, long step, long defaultValue)
    {
        if (min == 0 && max == 0 && step == 0 && defaultValue == 0)
        {
            return string.Empty;
        }

        return $"{min}..{max} step={step} default={defaultValue}";
    }

    private static string FormatRange(double min, double max, double step, double defaultValue)
    {
        if (min == 0 && max == 0 && step == 0 && defaultValue == 0)
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{min:G}..{max:G} step={step:G} default={defaultValue:G}"
        );
    }

    private static void PrintSummary(IReadOnlyList<GenICamNodeInfo> nodes)
    {
        Console.WriteLine("  类型统计：");
        foreach (
            IGrouping<string, GenICamNodeInfo> group in nodes
                .GroupBy(n => n.Type)
                .OrderBy(g => g.Key)
        )
        {
            Console.WriteLine($"    {group.Key, -12} {group.Count(), 4}");
        }

        Console.WriteLine("  权限统计：");
        foreach (
            IGrouping<string, GenICamNodeInfo> group in nodes
                .GroupBy(n => n.Access)
                .OrderBy(g => g.Key)
        )
        {
            Console.WriteLine($"    {group.Key, -16} {group.Count(), 4}");
        }
    }

    private static void PrintSelectorCandidates(IReadOnlyList<GenICamNodeInfo> nodes)
    {
        List<GenICamNodeInfo> selectors = nodes
            .Where(IsSelectorCandidate)
            .OrderBy(n => n.NodeName, StringComparer.Ordinal)
            .ToList();

        if (selectors.Count == 0)
        {
            return;
        }

        Console.WriteLine("  可能影响其他节点显示/可用性的选择器节点：");
        foreach (GenICamNodeInfo selector in selectors)
        {
            Console.WriteLine(
                $"    {selector.NodeName} = {FormatInline(selector.CurrentValue)}  entries={FormatInline(selector.EnumEntries)}"
            );
        }
    }

    private static bool IsSelectorCandidate(GenICamNodeInfo node)
    {
        return node.Type == "Enumeration"
            && node.Access == "ReadWrite"
            && (
                node.NodeName.Contains("Selector", StringComparison.OrdinalIgnoreCase)
                || node.NodeName.EndsWith("Select", StringComparison.OrdinalIgnoreCase)
                || node.NodeName.EndsWith("Mode", StringComparison.OrdinalIgnoreCase)
                || node.NodeName.Contains("Port", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string? ReadAnsi(IntPtr pointer)
    {
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(pointer);
    }

    private static string FormatInline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return ToPrintable(value);
    }

    private static string ToPrintable(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        return string.Concat(
            value.Select(character =>
                char.IsControl(character)
                && character != '\t'
                && character != '\r'
                && character != '\n'
                    ? $"\\x{(int)character:X2}"
                    : character.ToString()
            )
        );
    }

    private static string Csv(params string[] fields)
    {
        return string.Join(",", fields.Select(EscapeCsv));
    }

    private static string EscapeCsv(string? value)
    {
        string text = ToPrintable(value ?? string.Empty);
        if (text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n'))
        {
            return '"' + text.Replace("\"", "\"\"") + '"';
        }

        return text;
    }

    private static string TypeName(int type) =>
        type switch
        {
            0 => "Value",
            1 => "Base",
            2 => "Integer",
            3 => "Boolean",
            4 => "Command",
            5 => "Float",
            6 => "String",
            7 => "Register",
            8 => "Category",
            9 => "Enumeration",
            10 => "EnumEntry",
            11 => "Port",
            _ => $"Unknown({type})",
        };

    private static string AccessName(int access) =>
        access switch
        {
            0 => "NotImplemented",
            1 => "NotAvailable",
            2 => "WriteOnly",
            3 => "ReadOnly",
            4 => "ReadWrite",
            _ => $"Unknown({access})",
        };

    private static string VisibilityName(int visibility) =>
        visibility switch
        {
            0 => "Beginner",
            1 => "Expert",
            2 => "Guru",
            3 => "Invisible",
            0x10 => "Undefined",
            _ => $"Unknown({visibility})",
        };

    private static string RepresentationName(int representation) =>
        representation switch
        {
            0 => "Linear",
            1 => "Logarithmic",
            2 => "Boolean",
            3 => "PureNumber",
            4 => "HexNumber",
            5 => "IPv4Address",
            6 => "MacAddress",
            7 => "Undefined",
            8 => "Timestamp",
            9 => "PtpFrameCount",
            _ => $"Unknown({representation})",
        };
}

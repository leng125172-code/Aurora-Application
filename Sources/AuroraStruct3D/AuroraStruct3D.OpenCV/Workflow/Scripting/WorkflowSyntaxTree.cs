using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Scripting;

public readonly record struct WorkflowTextPosition(int Offset, int Line, int Column);

public readonly record struct WorkflowTextSpan(
    WorkflowTextPosition Start,
    WorkflowTextPosition End
);

public sealed record WorkflowSyntaxDiagnostic(
    string Code,
    string Severity,
    string Message,
    WorkflowTextSpan Span,
    string? NodeId = null,
    string? StatementId = null
);

public sealed class WorkflowSyntaxStatement
{
    public required string StatementId { get; init; }
    public required string Method { get; init; }
    public required string Text { get; init; }
    public required WorkflowTextSpan Span { get; init; }
    public string? NodeId { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

public sealed class WorkflowSyntaxTree
{
    public required string SourceText { get; init; }
    public required IReadOnlyList<WorkflowSyntaxStatement> Statements { get; init; }
    public required IReadOnlyList<WorkflowSyntaxDiagnostic> Diagnostics { get; init; }
    public required IReadOnlyDictionary<string, WorkflowSyntaxStatement> StatementsById { get; init; }

    public WorkflowSyntaxStatement? FindAt(int offset) =>
        Statements.FirstOrDefault(x =>
            offset >= x.Span.Start.Offset && offset <= x.Span.End.Offset
        );
}

public sealed class WorkflowIntermediateRepresentation
{
    public required string Name { get; init; }
    public required GraphDataModel Graph { get; init; }
    public required WorkflowSyntaxTree SyntaxTree { get; init; }
    public required IReadOnlyDictionary<string, WorkflowTextSpan> NodeSpans { get; init; }
}

/// <summary>
/// Error-tolerant lexer and syntax map for the restricted workflow language.
/// It never executes C# and deliberately recognizes only method-call statements.
/// </summary>
public static partial class WorkflowSyntaxParser
{
    public const int MaxSourceLength = 1024 * 1024;
    public const int MaxStatements = 10_000;
    public const int MaxGraphDepth = 32;

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(([\s\S]*)\)\s*;\s*$")]
    private static partial Regex CallPattern();

    [GeneratedRegex(@"^\s*(?:(?:var\s+[A-Za-z_][A-Za-z0-9_]*|var\s*\([^)]*\))\s*=\s*)?([A-Za-z_][A-Za-z0-9_]*)(?:\s*<[^;()]+>)?\s*\(([\s\S]*)\)\s*;\s*$")]
    private static partial Regex V2CallPattern();

    public static WorkflowSyntaxTree Parse(string source)
    {
        source ??= string.Empty;
        bool isCSharp = Regex.IsMatch(
            source,
            @"Workflow\s*\(\s*""(?:\\.|[^""])*""\s*,\s*(?:2|3)\s*\)",
            RegexOptions.Singleline);
        List<WorkflowSyntaxStatement> statements = [];
        List<WorkflowSyntaxDiagnostic> diagnostics = [];
        int graphDepth = 0;
        if (source.Length > MaxSourceLength)
        {
            diagnostics.Add(
                Diagnostic("WFS0001", "脚本超过 1 MB 限制。", source, 0, source.Length)
            );
            source = source[..MaxSourceLength];
        }

        foreach ((int start, int end) in SplitStatements(source))
        {
            int codeStart = FindCodeStart(source, start, end);
            string callText = source[codeStart..end];
            if (string.IsNullOrWhiteSpace(RemoveLineComments(callText)))
                continue;
            Match match = (isCSharp ? V2CallPattern() : CallPattern()).Match(callText);
            if (!match.Success)
            {
                diagnostics.Add(Diagnostic("WFS1001", "需要白名单方法调用语句并以分号结束。", source, codeStart, end));
                continue;
            }

            string method = match.Groups[1].Value;
            if (!isCSharp && !CSharpWorkflowScript.AllowedMethods.Contains(method))
            {
                diagnostics.Add(Diagnostic("WFS1002", $"不允许的语句 '{method}'。", source, codeStart, end));
            }

            IReadOnlyList<string> arguments = SplitArguments(match.Groups[2].Value);
            if (method == "GraphBegin" && ++graphDepth > MaxGraphDepth)
                diagnostics.Add(Diagnostic("WFS0003", "嵌套图深度超过 32 层限制。", source, codeStart, end));
            else if (method == "GraphEnd")
                graphDepth = Math.Max(0, graphDepth - 1);
            foreach (string argument in arguments.Where(x =>
                         x.TrimStart().StartsWith('{') || x.TrimStart().StartsWith('[')))
            {
                try
                {
                    using JsonDocument _ = JsonDocument.Parse(
                        argument,
                        new JsonDocumentOptions { MaxDepth = 64 });
                }
                catch (JsonException ex) when (ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Diagnostic("WFS0004", "JSON 参数深度超过 64 层限制。", source, codeStart, end));
                }
            }
            string? nodeId = isCSharp
                ? ReadV2NodeId(source, codeStart)
                : method switch
                {
                    "Node" or "Input" or "Output" or "Param" =>
                        ReadString(arguments.FirstOrDefault()),
                    _ => null,
                };
            int syntaxStart = isCSharp && nodeId is not null
                ? FindV2MetadataStart(source, codeStart)
                : codeStart;
            string text = source[syntaxStart..end];
            string statementId = CreateStatementId(method, nodeId, arguments, text, statements.Count);
            statements.Add(
                new WorkflowSyntaxStatement
                {
                    StatementId = statementId,
                    Method = method,
                    Text = text,
                    Span = Span(source, syntaxStart, end),
                    NodeId = nodeId,
                    Arguments = arguments,
                }
            );
            if (statements.Count >= MaxStatements)
            {
                diagnostics.Add(Diagnostic("WFS0002", "脚本语句数超过 10,000 条限制。", source, start, end));
                break;
            }
        }

        if (isCSharp && !diagnostics.Any(x => x.Severity == "error"))
        {
            try
            {
                CSharpWorkflowScript.Parse(source);
            }
            catch (WorkflowScriptException ex)
            {
                int offset = OffsetAtLineColumn(source, ex.Line, ex.Column);
                diagnostics.Add(
                    new WorkflowSyntaxDiagnostic(
                        ex.Code,
                        "error",
                        ex.Message,
                        Span(source, offset, Math.Min(source.Length, offset + 1)),
                        ex.NodeId));
            }
            catch (FormatException ex)
            {
                diagnostics.Add(
                    Diagnostic("WFS1000", ex.Message, source, 0, Math.Min(1, source.Length)));
            }
        }

        return new WorkflowSyntaxTree
        {
            SourceText = source,
            Statements = statements,
            Diagnostics = diagnostics,
            StatementsById = statements.ToDictionary(x => x.StatementId, StringComparer.Ordinal),
        };
    }

    public static string GetNodeStatementId(string nodeId) =>
        CreateStatementId("Node", nodeId, [JsonSerializer.Serialize(nodeId)], string.Empty, 0);

    /// <summary>
    /// Applies a graph-generated candidate to the original document by stable statement identity.
    /// Existing leading comments and all untouched source bytes are retained.
    /// </summary>
    public static string PatchPreservingTrivia(string originalSource, string candidateSource)
    {
        WorkflowSyntaxTree original = Parse(originalSource);
        WorkflowSyntaxTree candidate = Parse(candidateSource);
        if (original.Diagnostics.Any(x => x.Severity == "error")
            || candidate.Diagnostics.Any(x => x.Severity == "error"))
            throw new WorkflowScriptException("WFS3001", "存在语法错误，无法安全执行局部画布补丁。", 1);

        Dictionary<string, WorkflowSyntaxStatement> desired = candidate.Statements
            .ToDictionary(x => x.StatementId, StringComparer.Ordinal);
        List<(int Start, int End, string Text)> edits = [];
        foreach (WorkflowSyntaxStatement current in original.Statements)
        {
            if (!desired.Remove(current.StatementId, out WorkflowSyntaxStatement? replacement))
            {
                edits.Add((current.Span.Start.Offset, current.Span.End.Offset, string.Empty));
                continue;
            }
            if (!string.Equals(NormalizeStatement(current.Text), NormalizeStatement(replacement.Text), StringComparison.Ordinal))
                edits.Add((current.Span.Start.Offset, current.Span.End.Offset, replacement.Text.Trim()));
        }

        if (desired.Count > 0)
        {
            if (original.Statements.Count(x => x.Method == "GraphBegin") > 1)
                throw new WorkflowScriptException(
                    "WFS3002",
                    "嵌套图新增语句无法唯一确定插入容器，请刷新基线或改用脚本编辑。",
                    1);
            WorkflowSyntaxStatement? graphEnd = original.Statements.LastOrDefault(x =>
                x.Method is "GraphEnd" or "Return");
            int insertion = graphEnd?.Span.Start.Offset ?? originalSource.Length;
            string added = string.Join(
                Environment.NewLine,
                candidate.Statements.Where(x => desired.ContainsKey(x.StatementId)).Select(x => x.Text.Trim())
            ) + Environment.NewLine;
            edits.Add((insertion, insertion, added));
        }

        StringBuilder result = new(originalSource);
        foreach ((int start, int end, string text) in edits.OrderByDescending(x => x.Start))
        {
            result.Remove(start, end - start);
            result.Insert(start, text);
        }
        return result.ToString();
    }

    public static WorkflowIntermediateRepresentation BuildIr(string source)
    {
        WorkflowSyntaxTree syntax = Parse(source);
        if (syntax.Diagnostics.Any(x => x.Severity == "error"))
            throw new WorkflowScriptException("WFS1000", syntax.Diagnostics[0].Message, syntax.Diagnostics[0].Span.Start.Line, syntax.Diagnostics[0].Span.Start.Column);

        (string name, GraphDataModel graph) = CSharpWorkflowScript.Parse(source);
        Dictionary<string, WorkflowTextSpan> spans = syntax.Statements
            .Where(x => !string.IsNullOrWhiteSpace(x.NodeId))
            .GroupBy(x => x.NodeId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Span, StringComparer.Ordinal);
        return new WorkflowIntermediateRepresentation
        {
            Name = name,
            Graph = graph,
            SyntaxTree = syntax,
            NodeSpans = spans,
        };
    }

    private static string? ReadV2NodeId(string source, int statementStart)
    {
        int searchStart = Math.Max(0, source.LastIndexOf(';', Math.Max(0, statementStart - 1)) + 1);
        string prefix = source[searchStart..statementStart];
        Match match = Regex.Match(
            prefix,
            @"//\s*@node\s+(?<json>\{[^\r\n]*\})\s*$",
            RegexOptions.Multiline);
        if (!match.Success)
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(match.Groups["json"].Value);
            return document.RootElement.TryGetProperty("id", out JsonElement id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int FindV2MetadataStart(string source, int statementStart)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, statementStart - 1)) + 1;
        int previousLineEnd = Math.Max(0, lineStart - 1);
        int previousLineStart =
            source.LastIndexOf('\n', Math.Max(0, previousLineEnd - 1)) + 1;
        string previousLine = source[previousLineStart..previousLineEnd].Trim();
        return previousLine.StartsWith("// @node ", StringComparison.Ordinal)
            ? previousLineStart
            : statementStart;
    }

    private static int OffsetAtLineColumn(string source, int line, int column)
    {
        int currentLine = 1;
        int offset = 0;
        while (offset < source.Length && currentLine < line)
        {
            if (source[offset++] == '\n')
                currentLine++;
        }
        return Math.Min(source.Length, offset + Math.Max(0, column - 1));
    }

    public static string Format(string source)
    {
        (string name, GraphDataModel graph) = CSharpWorkflowScript.Parse(source);
        return CSharpWorkflowScript.Generate(name, graph);
    }

    private static IEnumerable<(int Start, int End)> SplitStatements(string source)
    {
        int start = 0;
        bool inString = false;
        bool escaped = false;
        bool lineComment = false;
        int objectDepth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (lineComment)
            {
                if (c == '\n')
                    lineComment = false;
                continue;
            }
            if (!inString && c == '/' && next == '/')
            {
                lineComment = true;
                i++;
                continue;
            }
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }
            if (c == '"')
            {
                inString = true;
                continue;
            }
            if (c is '{' or '[')
                objectDepth++;
            else if (c is '}' or ']')
                objectDepth--;
            else if (c == ';' && objectDepth == 0)
            {
                yield return (start, i + 1);
                start = i + 1;
            }
        }
        if (!string.IsNullOrWhiteSpace(RemoveLineComments(source[start..])))
            yield return (start, source.Length);
    }

    private static IReadOnlyList<string> SplitArguments(string text)
    {
        List<string> values = [];
        int start = 0;
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '{' or '[' or '(') depth++;
            else if (c is '}' or ']' or ')') depth--;
            else if (c == ',' && depth == 0)
            {
                values.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < text.Length || text.Length == 0)
            values.Add(text[start..].Trim());
        return values.Count == 1 && values[0].Length == 0 ? [] : values;
    }

    private static string? ReadString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try { return JsonSerializer.Deserialize<string>(value); }
        catch { return null; }
    }

    private static string CreateStatementId(
        string method,
        string? nodeId,
        IReadOnlyList<string> arguments,
        string text,
        int ordinal
    )
    {
        string discriminator = method switch
        {
            "Input" or "Output" or "Param" => arguments.ElementAtOrDefault(1) ?? string.Empty,
            "Edge" => arguments.ElementAtOrDefault(0) ?? string.Empty,
            "GraphBegin" => arguments.ElementAtOrDefault(0) ?? string.Empty,
            _ => string.Empty,
        };
        string identity = nodeId is null
            ? $"{method}:{discriminator}:{ordinal}"
            : method is "Input" or "Output" or "Param"
                ? $"{method}:{nodeId}:{discriminator}"
                : $"Node:{nodeId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    private static string NormalizeStatement(string text) =>
        Regex.Replace(RemoveLineComments(text), @"\s+", string.Empty);

    private static WorkflowSyntaxDiagnostic Diagnostic(string code, string message, string source, int start, int end) =>
        new(code, "error", message, Span(source, start, end));

    private static WorkflowTextSpan Span(string source, int start, int end) =>
        new(Position(source, start), Position(source, end));

    private static WorkflowTextPosition Position(string source, int offset)
    {
        int line = 1;
        int lastLineStart = 0;
        for (int i = 0; i < Math.Min(offset, source.Length); i++)
            if (source[i] == '\n') { line++; lastLineStart = i + 1; }
        return new(offset, line, offset - lastLineStart + 1);
    }

    private static string RemoveLineComments(string text) =>
        string.Join('\n', text.Split('\n').Select(x => x.Contains("//", StringComparison.Ordinal) ? x[..x.IndexOf("//", StringComparison.Ordinal)] : x));

    private static string RemoveLeadingComments(string text)
    {
        string[] lines = text.Split('\n');
        return string.Join('\n', lines.SkipWhile(x => string.IsNullOrWhiteSpace(x) || x.TrimStart().StartsWith("//", StringComparison.Ordinal))).Trim();
    }

    private static int FindCodeStart(string source, int start, int end)
    {
        int offset = start;
        while (offset < end)
        {
            while (offset < end && char.IsWhiteSpace(source[offset]))
                offset++;
            if (offset + 1 < end && source[offset] == '/' && source[offset + 1] == '/')
            {
                int newline = source.IndexOf('\n', offset);
                offset = newline < 0 || newline >= end ? end : newline + 1;
                continue;
            }
            break;
        }
        return offset;
    }
}

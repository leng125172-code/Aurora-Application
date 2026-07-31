using AuroraStruct3D.OpenCV.Workflow.Scripting;
using Xunit;

namespace AuroraStruct3D.Workflow;

public class WorkflowSyntaxTreeTests
{
    [Fact]
    public void V2_syntax_tree_should_recognize_operator_tuple_and_node_metadata()
    {
        string source =
            """
            Workflow("registration", 2);
            // @node {"id":"icp","x":10,"y":20,"title":"ICP"}
            var (alignedCloud, transformMatrix, stats) = icp_registration(
                sourceCloud,
                targetCloud,
                maxIterations: 50
            );
            Return(stats);
            """;

        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(source);

        // The undefined external variables are semantic errors, but the V2 statement
        // and its stable canvas identity must still be recognized.
        WorkflowSyntaxStatement statement = Assert.Single(
            tree.Statements,
            x => x.Method == "icp_registration");
        Assert.Equal("icp", statement.NodeId);
        Assert.Contains("// @node", statement.Text);
        Assert.Equal(2, statement.Span.Start.Line);
    }

    [Fact]
    public void V2_graph_patch_should_replace_operator_metadata_and_keep_document_comment()
    {
        string original =
            """
            // document comment
            Workflow("demo", 2);
            // @node {"id":"read","x":10,"y":20,"title":"Old"}
            var cloud = read_point_cloud("a.ply");
            // @node {"id":"end","x":10,"y":120,"title":"End"}
            Return(cloud);
            """;
        string candidate =
            """
            Workflow("demo", 2);
            // @node {"id":"read","x":50,"y":60,"title":"New"}
            var cloud = read_point_cloud("b.ply");
            // @node {"id":"end","x":10,"y":120,"title":"End"}
            Return(cloud);
            """;

        string patched = WorkflowSyntaxParser.PatchPreservingTrivia(original, candidate);

        Assert.Contains("// document comment", patched);
        Assert.Contains("\"title\":\"New\"", patched);
        Assert.Contains("\"b.ply\"", patched);
        Assert.DoesNotContain("\"title\":\"Old\"", patched);
    }

    [Fact]
    public void Syntax_tree_should_preserve_spans_comments_and_stable_node_statement_ids()
    {
        string source =
            """
            // heading
            Workflow("demo", 1);
            GraphBegin(null);
            Node("n1", "start-node", 10, 20, "Start", null, null);
            GraphEnd();
            """;

        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(source);

        Assert.Empty(tree.Diagnostics);
        WorkflowSyntaxStatement node = Assert.Single(tree.Statements, x => x.Method == "Node");
        Assert.Equal("n1", node.NodeId);
        Assert.Equal(WorkflowSyntaxParser.GetNodeStatementId("n1"), node.StatementId);
        Assert.Equal(4, node.Span.Start.Line);
        Assert.Contains("// heading", tree.SourceText);
    }

    [Fact]
    public void Recovery_parser_should_report_non_whitelisted_statement_without_executing_it()
    {
        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(
            """
            Workflow("demo", 1);
            System.IO.File.Delete("x");
            """
        );

        WorkflowSyntaxDiagnostic diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("WFS1001", diagnostic.Code);
    }

    [Fact]
    public void Comment_only_change_should_not_change_semantic_or_program_hash()
    {
        string first =
            """
            // first
            Workflow("demo", 1);
            GraphBegin(null);
            GraphEnd();
            """;
        string second = first.Replace("// first", "// changed");

        Assert.NotEqual(
            WorkflowProgramHash.ComputeContentHash(first),
            WorkflowProgramHash.ComputeContentHash(second)
        );
        Assert.Equal(
            WorkflowProgramHash.ComputeSemanticHash(first),
            WorkflowProgramHash.ComputeSemanticHash(second)
        );
        Assert.Equal(
            WorkflowProgramHash.ComputeProgramHash(first),
            WorkflowProgramHash.ComputeProgramHash(second)
        );
    }

    [Fact]
    public void Parser_should_enforce_source_size_limit()
    {
        string source = new(' ', WorkflowSyntaxParser.MaxSourceLength + 1);
        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(source);
        Assert.Contains(tree.Diagnostics, x => x.Code == "WFS0001");
    }

    [Fact]
    public void Port_statements_should_have_stable_distinct_ids()
    {
        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(
            """
            Workflow("demo", 1);
            GraphBegin(null);
            Input("n1", "left", null, null);
            Input("n1", "right", null, null);
            GraphEnd();
            """
        );

        WorkflowSyntaxStatement[] inputs = tree.Statements.Where(x => x.Method == "Input").ToArray();
        Assert.Equal(2, inputs.Length);
        Assert.NotEqual(inputs[0].StatementId, inputs[1].StatementId);
        Assert.Equal(2, inputs.Select(x => x.StatementId).Distinct().Count());
    }

    [Fact]
    public void Graph_patch_should_preserve_untouched_comments_and_formatting()
    {
        string source =
            """
            // document comment
            Workflow("demo", 1);
            GraphBegin(null);
            // keep this node comment
              Node("n1", "start-node", 10, 20, "Old", null, null);
            GraphEnd();
            """;
        string candidate =
            """
            Workflow("demo", 1);
            GraphBegin(null);
            Node("n1", "start-node", 10, 20, "New", null, null);
            GraphEnd();
            """;

        string patched = WorkflowSyntaxParser.PatchPreservingTrivia(source, candidate);

        Assert.Contains("// document comment", patched);
        Assert.Contains("// keep this node comment", patched);
        Assert.Contains("\"New\"", patched);
    }
}

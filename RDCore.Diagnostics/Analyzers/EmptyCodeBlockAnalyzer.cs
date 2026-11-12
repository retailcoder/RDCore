using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01004</c>: an <c>Else</c>, <c>Case</c>, <c>Do</c>, <c>For</c>, <c>For Each</c> or <c>While</c> block that has no statement (<strong>MS-VBAL §5.4.2</strong>).
/// </summary>
/// <remarks>
/// A comment, a line label and a declaration are no statement. A <c>Case Else</c> is not judged: an empty one states that the other values are left alone. The
/// finding is the header of the block, its keyword and what it states.
/// </remarks>
internal sealed class EmptyCodeBlockAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            if (Empty(node) is ({ } range, { } keyword))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.EmptyCodeBlock.ToCode(),
                    range,
                    DiagnosticSeverity.Information,
                    Say(RDCoreDiagnosticsResources.EmptyCodeBlock_Message, keyword));
            }
        }
    }

    private static (SourceRange? Range, string? Keyword) Empty(SyntaxNode node) => node switch
    {
        ElseBlockStatementNode block when block.Body.HasNoStatement() => (EmptyBlocks.Keyword(block, "Else".Length), "Else"),
        CaseExpressionStatementNode block when block.Block.HasNoStatement() && !block.RangeClauses.IsDefaultOrEmpty
            => (EmptyBlocks.Header(block, block.RangeClauses[^1]), "Case"),
        DoWhileLoopStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.ConditionExpression), "Do"),
        DoUntilLoopStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.ConditionExpression), "Do"),
        DoLoopWhileStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Keyword(loop, "Do".Length), "Do"),
        DoLoopUntilStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Keyword(loop, "Do".Length), "Do"),
        DoLoopStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Keyword(loop, "Do".Length), "Do"),
        ForStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.StepExpression ?? loop.EndExpression), "For"),
        ForEachStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.CollectionExpression), "For Each"),
        WhileWendStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.ConditionExpression), "While"),
        _ => (null, null),
    };
}

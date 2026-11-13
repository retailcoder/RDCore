using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01003</c>: a branch of an <c>If</c> statement that has no statement (<strong>MS-VBAL §5.4.2.8</strong>, <strong>§5.4.2.9</strong>).
/// </summary>
/// <remarks>
/// The <c>If</c> or <c>ElseIf</c> branch of a block <c>If</c>, or the <c>Then</c> of a single-line <c>If</c> that has only an <c>Else</c>. A comment, a line label and a
/// declaration are no statement. The finding is the header of the branch, its keyword and its condition.
/// </remarks>
internal sealed class EmptyIfBlockAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            switch (node)
            {
                case IfBlockStatementNode block when block.Body.HasNoStatement():
                    yield return Finding(EmptyBlocks.Header(block, block.ConditionExpression), "If");
                    break;
                case ElseIfBlockStatementNode branch when branch.Body.HasNoStatement():
                    yield return Finding(EmptyBlocks.Header(branch, branch.ConditionExpression), "ElseIf");
                    break;
                case InlineIfStatementNode inline when inline.ThenBody.HasNoStatement():
                    yield return Finding(EmptyBlocks.Header(inline, inline.ConditionExpression), "If");
                    break;
            }
        }
    }

    private static AnalyzerFinding Finding(SDK.Model.Source.SourceRange range, string keyword) => new(
        RDCoreDiagnosticId.EmptyIfBlock.ToCode(),
        range,
        DiagnosticSeverity.Information,
        Say(RDCoreDiagnosticsResources.EmptyIfBlock_Message, keyword));
}

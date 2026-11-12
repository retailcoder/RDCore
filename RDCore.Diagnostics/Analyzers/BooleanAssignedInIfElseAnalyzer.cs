using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00209</c>: an <c>If…Else</c> whose two branches each assign one Boolean literal to the same name, <c>True</c> in one and <c>False</c> in the other
/// (<strong>MS-VBAL §5.4.2.8</strong>): the assignment of the condition, or of its negation, says the same.
/// </summary>
/// <remarks>
/// An <c>ElseIf</c>, a second statement or a <c>Set</c> in a branch makes it something else. The name is a variable, or a member of one (<c>Me.Done</c>,
/// <c>o.Done</c>); an element of an array is not compared. The finding is the header of the <c>If</c>.
/// </remarks>
internal sealed class BooleanAssignedInIfElseAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var block in module.Descendants().OfType<IfBlockStatementNode>())
        {
            if (block is { ElseIfBlocks.IsDefaultOrEmpty: true, ElseBlock: { } otherwise }
                && Assigned(block.Body) is ({ } name, { } value)
                && Assigned(otherwise.Body) is ({ } otherName, { } otherValue)
                && string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase)
                && value != otherValue)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.BooleanAssignedInIfElse.ToCode(),
                    Header(block),
                    DiagnosticSeverity.Information,
                    Say(RDCoreDiagnosticsResources.BooleanAssignedInIfElse_Message, name));
            }
        }
    }

    private static SDK.Model.Source.SourceRange Header(IfBlockStatementNode block)
        => new(block.SourceLocation.Range.Start, block.ConditionExpression.SourceLocation.Range.End);

    // the one statement of the branch, when it Let-assigns True or False to a name.
    private static (string? Name, bool? Value) Assigned(StatementBlock body)
        => body.Children is [AssignmentStatementNode { Kind: SDK.Model.AST.Statements.AssignmentKind.ImplicitLet or SDK.Model.AST.Statements.AssignmentKind.ExplicitLet } assignment]
            && assignment.Value is LiteralExpressionNode { StaticValue: VBBooleanValue literal }
            && NameOf(assignment.Target) is { } name
                ? (name, literal.Value.Equals(VBBooleanValue.True.Value))
                : (null, null);

    private static string? NameOf(ExpressionNode target) => target switch
    {
        SimpleNameExpressionNode simple => simple.IdentifierName,
        MemberAccessExpressionNode { Owner: { } owner } member when NameOf(owner) is { } qualifier => $"{qualifier}.{member.Member.IdentifierName}",
        InstanceExpressionNode => "Me",
        _ => null,
    };
}

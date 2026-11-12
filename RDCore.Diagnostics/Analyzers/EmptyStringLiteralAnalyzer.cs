using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00312</c>: the string literal <c>""</c> (<strong>MS-VBAL §3.3.4</strong>), which the constant <c>vbNullString</c> says.
/// </summary>
/// <remarks>
/// Both are a String of no characters, and compare equal; only the address of their characters tells them apart (<c>StrPtr</c>, and a String given to a procedure of a
/// DLL), which is <c>0</c> for <c>vbNullString</c>. The finding is the literal: the only string literal two characters long.
/// </remarks>
internal sealed class EmptyStringLiteralAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<LiteralExpressionNode>()
            .Where(literal => literal is { StaticValue: VBStringValue, SourceLocation.Range: var range }
                && range.Start.Line == range.End.Line && range.End.Character - range.Start.Character == 2)
            .Select(literal => new AnalyzerFinding(
                RDCoreDiagnosticId.EmptyStringLiteral.ToCode(),
                literal.SourceLocation.Range,
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.EmptyStringLiteral_Message));
}

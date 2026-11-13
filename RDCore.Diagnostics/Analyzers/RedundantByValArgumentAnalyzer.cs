using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00310</c>: an argument written <c>ByVal</c> for a parameter that is <c>ByVal</c> already, where the keyword changes nothing.
/// </summary>
/// <remarks>
/// MS-VBA compiles it and MS-VBAL §5.6.13.1 refuses it. Whether the parameter is <c>ByVal</c> is a fact of the argument
/// (<see cref="ValueExpressionSemanticFlags.RedundantByVal"/>), which is what this reads. The fix takes the keyword out, up to the argument it is written for.
/// </remarks>
internal sealed class RedundantByValArgumentAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var arguments = context.ParseResult.SyntaxTree is { } module ? module.Descendants().OfType<ByValArgumentExpressionNode>().ToList() : [];
        return (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.RedundantByVal))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.RedundantByValArgument.ToCode(), fact.Location.Range, DiagnosticSeverity.Hint, RDCoreDiagnosticsResources.RedundantByValArgument_Message)
            {
                Fixes = arguments.LastOrDefault(argument => argument.SourceLocation.Range.Contains(fact.Location.Range.Start)) is { } byVal
                    ? new DiagnosticFixes
                    {
                        Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.RedundantByValArgument_Fix,
                            SyntaxEdits.Remove(new SourceRange(byVal.SourceLocation.Range.Start, byVal.Operand.SourceLocation.Range.Start))),
                    }
                    : null,
            });
    }
}

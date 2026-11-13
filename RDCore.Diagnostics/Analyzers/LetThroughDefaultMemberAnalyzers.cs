using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11045</c>: a <c>Let</c> to an object of a specific class, written without an argument list, whose default member has no <c>Property Let</c>
/// (<c>os = 5</c>, <c>Me = 5</c>), which MS-VBA refuses at compile time.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.3.8</strong> makes the statement valid. MS-VBA refuses it: "Invalid use of property" for a <c>Property Get</c>, "Type mismatch" for a
/// function (observed in Excel). A warning, because the code does not compile in MS-VBA. The host states it of the target
/// (<see cref="ValueExpressionSemanticFlags.LetToADefaultMemberWithNoPropertyLet"/>).
/// </remarks>
internal sealed class LetToADefaultMemberWithNoPropertyLetAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => LetThroughDefaultMember.Findings(context, ValueExpressionSemanticFlags.LetToADefaultMemberWithNoPropertyLet, RDCoreDiagnosticId.LetToADefaultMemberWithNoPropertyLet,
            _ => RDCoreDiagnosticsResources.LetToADefaultMemberWithNoPropertyLet_Message);
}

/// <summary>
/// <c>RDC11046</c>: a <c>Let</c> that names a <c>Property Get</c> with no <c>Property Let</c>, or a function, declared <c>Variant</c>, outside its own body
/// (<c>o.Value = 5</c>), which RD-VBA runs as MS-VBA does.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.3.8</strong> raises run-time error 451 for the property and 450 for the function. MS-VBA calls it, and let-assigns the default member of
/// the object it returns, or raises 424 when it returns no object (observed in Excel). A warning, because the statement does what neither the code nor the
/// specification says. The host states it of the callee (<see cref="ValueExpressionSemanticFlags.LetThroughAVariantGetOrFunction"/>).
/// </remarks>
internal sealed class LetThroughAVariantGetOrFunctionAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => LetThroughDefaultMember.Findings(context, ValueExpressionSemanticFlags.LetThroughAVariantGetOrFunction, RDCoreDiagnosticId.LetThroughAVariantGetOrFunction,
            fact => fact.Classification == ExpressionClassification.Function
                ? RDCoreDiagnosticsResources.LetThroughAVariantFunction_Message
                : RDCoreDiagnosticsResources.LetThroughAVariantGet_Message);
}

// a finding at each expression the flag is stated of, named as the code writes it.
internal static class LetThroughDefaultMember
{
    public static IEnumerable<AnalyzerFinding> Findings(ModuleAnalysisContext context, ValueExpressionSemanticFlags flag, RDCoreDiagnosticId id,
        Func<SDK.Platform.Protocol.ExpressionFactDto, string> message)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var expressions = module.Descendants().OfType<ExpressionNode>().GroupBy(expression => expression.Identity).ToDictionary(group => group.Key, group => group.First());
        foreach (var fact in semantics.Procedures.SelectMany(procedure => procedure.Expressions).Where(fact => fact.Flags.HasFlag(flag)))
        {
            if (expressions.TryGetValue(fact.Node, out var expression) && NameOf(expression) is { } name)
            {
                yield return new AnalyzerFinding(id.ToCode(), fact.Location.Range, DiagnosticSeverity.Warning, AnalyzerMessages.Format(message(fact), name));
            }
        }
    }

    private static string? NameOf(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode access => access.Member.IdentifierName,
        InstanceExpressionNode => Tokens.Me,
        _ => null,
    };
}

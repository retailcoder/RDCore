using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00218</c>: <c>IsMissing</c> of a <c>Variant</c> that is not an <c>Optional</c> parameter (<strong>MS-VBAL §6.1.2.7.1.6</strong>): a required
/// <c>Variant</c> parameter, or a <c>Variant</c> variable.
/// </summary>
/// <remarks>
/// Such a name holds the Missing value only when it was given one, from an <c>Optional</c> parameter whose argument was omitted: <c>IsMissing</c> of it does
/// not tell whether an argument of the procedure was omitted, which is what it is written for. The host states it from the declaration
/// (<see cref="ValueExpressionSemanticFlags.IsMissingOfAVariantThatIsNotOptional"/>); the analyzer reads whether the name is a parameter only to say which.
/// </remarks>
internal sealed class IsMissingOfAVariantThatIsNotOptionalAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        // each call, with the names of the parameters of the procedure it is written in.
        var calls = new Dictionary<SyntaxNodeId, (IndexExpressionNode Call, HashSet<string> Parameters)>();
        foreach (var member in module.Descendants().OfType<MemberDeclarationNode>())
        {
            var parameters = member.Descendants().OfType<ParameterDeclarationNode>().Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var call in member.Descendants().OfType<IndexExpressionNode>())
            {
                calls.TryAdd(call.Identity, (call, parameters));
            }
        }

        foreach (var procedure in semantics.Procedures)
        {
            foreach (var fact in procedure.Expressions.Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.IsMissingOfAVariantThatIsNotOptional)))
            {
                if (!calls.TryGetValue(fact.Node, out var written) || written.Call.Arguments is not [SimpleNameExpressionNode argument])
                {
                    continue;
                }

                var what = written.Parameters.Contains(argument.IdentifierName)
                    ? RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_RequiredParameter
                    : RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Variable;
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.IsMissingOfAVariantThatIsNotOptional.ToCode(),
                    fact.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Message, argument.IdentifierName, what));
            }
        }
    }
}

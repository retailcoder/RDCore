using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics.Flags;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00207</c>: <c>IsMissing</c> of a name that can never hold the Missing value (<strong>MS-VBAL §6.1.2.7.1.6</strong>), so that it is always <c>False</c>.
/// </summary>
/// <remarks>
/// Only a <c>Variant</c> parameter can be missing: an omitted argument leaves a parameter of any other type its type's default value, and gives an
/// <c>Optional</c> <c>Variant</c> its default value when it has one. An <c>IsMissing</c> of such a parameter looks like a test and never is one, which MS-VBA
/// compiles without a word (observed in Excel). A variable that is not a <c>Variant</c>, and a constant, never hold the Missing value
/// either. The host states it from the declaration (<see cref="ValueExpressionSemanticFlags.IsMissingAlwaysFalse"/>); the analyzer reads the argument's
/// classification, declared type and whether it is a parameter only to say why.
/// </remarks>
internal sealed class IsMissingAlwaysFalseAnalyzer : IModuleAnalyzer
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
            foreach (var fact in procedure.Expressions.Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.IsMissingAlwaysFalse)))
            {
                if (!calls.TryGetValue(fact.Node, out var written) || written.Call.Arguments is not [SimpleNameExpressionNode argument])
                {
                    continue;
                }

                var argumentFact = procedure.Expressions.FirstOrDefault(candidate => candidate.Node == argument.Identity);
                string message;
                if (written.Parameters.Contains(argument.IdentifierName))
                {
                    var reason = string.Equals(argumentFact?.DeclaredType, "Variant", StringComparison.OrdinalIgnoreCase)
                        ? RDCoreDiagnosticsResources.IsMissingAlwaysFalse_HasADefault
                        : RDCoreDiagnosticsResources.IsMissingAlwaysFalse_NotAVariant;
                    message = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_Message, argument.IdentifierName, reason);
                }
                else
                {
                    var reason = argumentFact?.Classification is ExpressionClassification.Constant
                        ? RDCoreDiagnosticsResources.IsMissingAlwaysFalse_AConstant
                        : RDCoreDiagnosticsResources.IsMissingAlwaysFalse_AVariableNotAVariant;
                    message = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_NotAParameter_Message, argument.IdentifierName, reason);
                }

                // the call is always False, and its argument is a name, which evaluating does nothing else: False in its place does what it did.
                yield return new AnalyzerFinding(RDCoreDiagnosticId.IsMissingAlwaysFalse.ToCode(), fact.Location.Range, DiagnosticSeverity.Warning, message)
                {
                    Fixes = new DiagnosticFixes
                    {
                        Edit = new EditFix
                        {
                            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_Fix, argument.IdentifierName),
                            Edits = [new TextEdit { Range = written.Call.Location.Range.ToLsp(), NewText = "False" }],
                        },
                    },
                };
            }
        }
    }
}

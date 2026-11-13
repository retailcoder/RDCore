using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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

        // each call, with the procedure it is written in and the names of its parameters.
        var calls = new Dictionary<SyntaxNodeId, (IndexExpressionNode Call, MemberDeclarationNode Member, HashSet<string> Parameters)>();
        foreach (var member in module.Descendants().OfType<MemberDeclarationNode>())
        {
            var parameters = member.Descendants().OfType<ParameterDeclarationNode>().Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var call in member.Descendants().OfType<IndexExpressionNode>())
            {
                calls.TryAdd(call.Identity, (call, member, parameters));
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

                var isParameter = written.Parameters.Contains(argument.IdentifierName);
                var what = isParameter
                    ? RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_RequiredParameter
                    : RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Variable;
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.IsMissingOfAVariantThatIsNotOptional.ToCode(),
                    fact.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Message, argument.IdentifierName, what))
                {
                    Fixes = isParameter && FixOf(written.Member, argument.IdentifierName, semantics) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
                };
            }
        }
    }

    // Optional before the parameter. MS-VBAL §5.3.1.5 lets an Optional parameter be followed only by Optional parameters, never with a ParamArray, and makes the
    // accessors of a property declare the same parameters; a handler or an implementation is declared as the event or the interface says.
    private static EditFix? FixOf(MemberDeclarationNode member, string name, ModuleSemanticsDto semantics)
    {
        if (member.MemberKind is not (MemberKind.Procedure or MemberKind.Function)
            || semantics.Declarations.Any(declaration => declaration is { Kind: DeclarationKind.Procedure, Role: not DeclarationRole.None }
                && string.Equals(declaration.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var parameters = member.Children.OfType<ParameterDeclarationNode>().OrderBy(parameter => parameter.Location.Range.Start).ToArray();
        var index = Array.FindIndex(parameters, parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || parameters.Any(parameter => parameter.IsParamArray) || parameters.Skip(index + 1).Any(parameter => !parameter.IsOptional))
        {
            return null;
        }

        var start = parameters[index].Location.Range.Start;
        return new EditFix
        {
            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Fix, name),
            Edits = [new TextEdit { Range = new Range(start.Line, start.Character, start.Line, start.Character), NewText = "Optional " }],
        };
    }
}

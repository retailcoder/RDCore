using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11047</c>: a <c>Let</c> gives an object to a property whose value parameter is a class or <c>Object</c>. MS-VBA passes the object itself, as a
/// <c>Set</c> would, where <strong>MS-VBAL §5.5.1.1</strong> makes the assignment a type mismatch, and <strong>§5.4.3.8</strong> with
/// <strong>§5.5.1.2.13</strong> evaluates the object's default member (error 424, 91 for <c>Nothing</c>).
/// </summary>
/// <remarks>
/// The host states it of the property's name (<see cref="ValueExpressionSemanticFlags.ObjectGivenToObjectProperty"/>), where the property is bound early and
/// the value is declared as an object.
/// </remarks>
internal sealed class ObjectLetToObjectPropertyAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        return semantics.Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding is not null && fact.Flags.HasFlag(ValueExpressionSemanticFlags.ObjectGivenToObjectProperty))
            .GroupBy(fact => fact.Location.Range.Start)
            .Select(target => target.First())
            .OrderBy(target => target.Location.Range.Start)
            .Select(target => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectLetToObjectProperty.ToCode(),
                target.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToObjectProperty_Message, SymbolUri.NameOf(target.Binding!))));
    }
}

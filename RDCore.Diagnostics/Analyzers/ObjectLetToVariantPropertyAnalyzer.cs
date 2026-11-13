using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11043</c>: a <c>Let</c> gives an object to a property whose value parameter is a <c>Variant</c>. MS-VBA passes the object itself, where
/// <strong>MS-VBAL §5.4.3.8</strong> passes its default member's value.
/// </summary>
/// <remarks>
/// The host states it of the property's name (<see cref="ValueExpressionSemanticFlags.ObjectGivenToVariantProperty"/>), where the property is bound early and
/// the value is declared as an object.
/// </remarks>
internal sealed class ObjectLetToVariantPropertyAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        return semantics.Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding is not null && fact.Flags.HasFlag(ValueExpressionSemanticFlags.ObjectGivenToVariantProperty))
            .GroupBy(fact => fact.Location.Range.Start)
            .Select(target => target.First())
            .OrderBy(target => target.Location.Range.Start)
            .Select(target => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectLetToVariantProperty.ToCode(),
                target.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToVariantProperty_Message, SymbolUri.NameOf(target.Binding!))));
    }
}

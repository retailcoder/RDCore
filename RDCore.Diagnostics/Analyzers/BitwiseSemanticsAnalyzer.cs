using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

internal sealed class BitwiseSemanticsAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.LogicalOpSemanticFlags?.HasFlag(LogicalOperatorSemanticFlags.IsBitwiseSemantics) ?? false)
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.BitwiseOperator,
                op.Location.Range,
                DiagnosticSeverity.Hint,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.BitwiseOperator_Message,
                    op.Operator)));
}

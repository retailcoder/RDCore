using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00315</c>: <c>Option Base 0</c> or <c>Option Compare Binary</c>, the options a module has when it states none (<strong>MS-VBAL §5.2.1.1</strong>,
/// <strong>§5.2.1.2</strong>).
/// </summary>
internal sealed class RedundantModuleOptionAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .OfType<ModuleOptionDirectiveNode>()
            .Where(option => option.ModuleOption is ModuleOptions.OptionBase0 or ModuleOptions.OptionCompareBinary)
            .Select(option => new AnalyzerFinding(
                RDCoreDiagnosticId.RedundantModuleOption.ToCode(),
                option.SourceLocation.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.RedundantModuleOption_Message, option.ModuleOption is ModuleOptions.OptionBase0 ? "Option Base 0" : "Option Compare Binary")));
}

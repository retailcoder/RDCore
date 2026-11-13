using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00210</c>: a <c>Stop</c> statement (<strong>MS-VBAL §5.4.2.11</strong>), which suspends the program where it is, as a breakpoint does.
/// </summary>
/// <remarks>
/// A <c>Stop</c> on a line of its own comes with the fix that removes the line. The fix changes what the program does, and its title says so: the program no
/// longer breaks there.
/// </remarks>
internal sealed class StopStatementAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        var nodes = module.Descendants().ToArray();
        return nodes
            .OfType<KeywordStatementNode>()
            .Where(statement => string.Equals(statement.Token, Tokens.Stop, StringComparison.OrdinalIgnoreCase))
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.StopStatement.ToCode(),
                statement.SourceLocation.Range,
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.StopStatement_Message)
            {
                Fixes = LineRemoval.Of(statement, nodes, RDCoreDiagnosticsResources.StopStatement_Fix) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
            });
    }
}

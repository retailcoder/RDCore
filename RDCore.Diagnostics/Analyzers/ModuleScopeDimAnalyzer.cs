using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00202</c>: a <c>Dim</c> statement at the module level, which declares <c>Private</c> variables (<strong>MS-VBAL §5.2.3</strong>).
/// </summary>
/// <remarks>
/// A module-level variable that states no access modifier can only have been declared with <c>Dim</c>. One finding is made for each statement, however many variables it declares.
/// The fix writes <c>Private</c> in place of the <c>Dim</c> the statement starts with: its variables are as accessible as they were.
/// <para>
/// A statement of several names is this finding's and not <c>RDC00204</c>'s, since its one keyword declares every variable of the list
/// (<strong>MS-VBAL §5.2.3.1</strong>). The fix then also declares each name on a line of its own, as <c>RDC00204</c>'s fix does:
/// <c>Dim a, b As Long</c> is <c>Private a</c> and <c>Private b As Long</c>, and nothing is left for <c>RDC00204</c> to find.
/// </para>
/// </remarks>
internal sealed class ModuleScopeDimAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => DeclarationStatements.In(module.Children)
            .Where(IsDim)
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.ModuleScopeDimDeclaration.ToCode(),
                DeclarationStatements.RangeOf(statement),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ModuleScopeDimDeclaration_Message)
            {
                Fixes = new DiagnosticFixes
                {
                    Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.ModuleScopeDimDeclaration_Fix,
                        [SyntaxEdits.Replace(SyntaxEdits.Following(statement[0].SourceLocation.Range.Start, "Dim".Length), "Private"),
                         .. MultipleDeclarationsAnalyzer.SplitEdits(statement, "Private") ?? []]),
                },
            });

    // a statement of the module's declarations that declares variables with no access modifier: a Dim statement.
    internal static bool IsDim(IReadOnlyList<SyntaxNode> statement) => statement[0] is VariableDeclarationNode { AccessModifier: AccessModifier.Implicit };
}

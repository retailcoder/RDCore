using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11014</c>: a <c>Case Is</c> clause whose value is compared with the selector in the selector's declared type, where MS-VBAL §5.4.2.10 states the
/// comparison as an expression of the two (<see cref="RDCore.SDK.Semantics.Facts.CaseClauseStatementFact"/>).
/// </summary>
[TestClass]
public sealed class CaseComparedInSelectorTypeAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindAsync(params string[] body)
    {
        var uri = TestUri.TestModuleUri();
        var source = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        return [.. new CaseComparedInSelectorTypeAnalyzer().Analyze(
            new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), WithoutVerdicts(payload.Modules.Single())))];
    }

    // the payload as the host sends it, without the flag that marks a clause as compared in another type than the expression's: the analyzer judges from the
    // two types each clause states.
    private static ModuleSemanticsDto WithoutVerdicts(ModuleSemanticsDto model) => model with
    {
        Procedures = [.. model.Procedures.Select(procedure => procedure.Runtime is not { } runtime ? procedure : procedure with
        {
            Runtime = runtime with
            {
                Statements = [.. runtime.Statements.Select(statement => statement.CaseClauseFlags is not { } flags ? statement : statement with
                {
                    CaseClauseFlags = flags & ~CaseClauseSemanticFlags.ComparedInSelectorType,
                })],
            },
        })],
    };

    [TestMethod]
    [DataRow("Dim i As Integer", "i = 5", "Case Is < 5.4", DisplayName = "an Integer, Is < 5.4")]
    [DataRow("Dim b As Boolean", "b = False", "Case Is < 1", DisplayName = "a Boolean, Is < 1")]
    [DataRow("Dim s As String", "s = \"10\"", "Case Is < 5", DisplayName = "a String, Is < 5")]
    public async Task ACaseIsComparedInTheSelectorType_IsReported(string declaration, string assignment, string clause)
    {
        var finding = (await FindAsync(declaration, assignment, "Select Case " + assignment[..1], clause, "Debug.Print 1", "End Select")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.CaseComparedInSelectorType, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("Dim i As Integer", "i = 5", "Case 5.4", DisplayName = "a single value, which MS-VBAL does not say how to compare")]
    [DataRow("Dim i As Integer", "i = 5", "Case 1.5 To 5.4", DisplayName = "a range")]
    [DataRow("Dim i As Integer", "i = 5", "Case Is < 6", DisplayName = "a value of the selector's type")]
    [DataRow("Dim n As Long", "n = 5", "Case Is < 6", DisplayName = "an Integer, which a Long comparison widens")]
    [DataRow("Dim v As Variant", "v = 5", "Case Is < 5.4", DisplayName = "a Variant selector")]
    [DataRow("Dim i As Integer", "i = 3", "Case Is < \"5\"", DisplayName = "a String and an Integer selector, compared as Integers as MS-VBAL's i < \"5\" is")]
    [DataRow("Dim n As Long", "n = 3", "Case Is < \"5.5\"", DisplayName = "a String and a Long selector")]
    public async Task ACaseOfAnotherKind_IsNotReported(string declaration, string assignment, string clause)
    {
        Assert.IsEmpty(await FindAsync(declaration, assignment, "Select Case " + assignment[..1], clause, "Debug.Print 1", "End Select"));
    }
}

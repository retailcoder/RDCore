using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The analyzers that find their condition from what the facts describe report the same findings when no fact carries a flag that states the condition.
/// </summary>
[TestClass]
public sealed class AnalyzersWithoutFindingFlagsTests
{
    private const ValueExpressionSemanticFlags Findings = ValueExpressionSemanticFlags.IsMissingAlwaysFalse
        | ValueExpressionSemanticFlags.ArrayGivenASecondEmptyArgumentList | ValueExpressionSemanticFlags.CollectionSetToNothingInItsOwnLoop;

    private static ModuleSemanticsDto WithoutFindings(ModuleSemanticsDto model)
        => model with
        {
            Procedures = [.. model.Procedures.Select(procedure => procedure with
            {
                Expressions = [.. procedure.Expressions.Select(fact => fact with { Flags = fact.Flags & ~Findings })],
            })],
        };

    private static async Task<(string[] With, string[] Without)> FindingsAsync(IModuleAnalyzer analyzer, params string[] lines)
    {
        var program = "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n", lines) + "\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parse = new ModuleParser().Parse(uri, program);
        static string[] Said(IEnumerable<AnalyzerFinding> findings) => [.. findings.Select(finding => $"{finding.Code} {finding.Range} {finding.Message}")];
        return (Said(analyzer.Analyze(new ModuleAnalysisContext(uri, parse, model))), Said(analyzer.Analyze(new ModuleAnalysisContext(uri, parse, WithoutFindings(model)))));
    }

    [TestMethod]
    public async Task IsMissingAlwaysFalse()
    {
        var (with, without) = await FindingsAsync(new IsMissingAlwaysFalseAnalyzer(),
            "Public Sub Probe(Optional p As Long, Optional q As Variant = 5, Optional r As Variant)",
            "    Debug.Print IsMissing(p), IsMissing(q), IsMissing(r)",
            "End Sub");

        Assert.HasCount(2, with);
        CollectionAssert.AreEqual(with, without);
    }

    [TestMethod]
    public async Task ArrayGivenASecondEmptyArgumentList()
    {
        var (with, without) = await FindingsAsync(new ArrayGivenASecondEmptyArgumentListAnalyzer(),
            "Public Sub Probe()",
            "    Dim f(2) As Long, a() As Long, v As Variant",
            "    Debug.Print UBound(f()())",
            "    v = a()()",
            "    Debug.Print UBound(f())",
            "End Sub");

        Assert.HasCount(2, with);
        CollectionAssert.AreEqual(with, without);
    }

    [TestMethod]
    public async Task CollectionSetToNothingInItsOwnLoop()
    {
        var (with, without) = await FindingsAsync(new CollectionSetToNothingInItsOwnLoopAnalyzer(),
            "Public Sub Probe()",
            "    Dim C As New Collection, D As New Collection, V As Variant",
            "    For Each V In C",
            "        Set C = Nothing",
            "        For Each V In D",
            "            Set C = Nothing",
            "        Next",
            "    Next",
            "    Set C = Nothing",
            "End Sub");

        Assert.HasCount(2, with);
        CollectionAssert.AreEqual(with, without);
    }
}

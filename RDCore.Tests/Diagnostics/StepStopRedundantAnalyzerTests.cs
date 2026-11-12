using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The <c>Step</c> of a <c>For</c> loop (<c>RDC00116</c>, <c>RDC00313</c>), a Boolean assigned in both branches of an <c>If</c> (<c>RDC00209</c>), <c>Stop</c>
/// (<c>RDC00210</c>), a <c>ByRef</c> written (<c>RDC00314</c>) and a module option that states the default (<c>RDC00315</c>).
/// </summary>
[TestClass]
public sealed class StepStopRedundantAnalyzerTests
{
    private static string Module(string source) => $"Attribute VB_Name = \"Program\"\r\n{source.ReplaceLineEndings("\r\n")}\r\n";

    private static readonly Uri Program = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    private static ModuleSemanticsDto StandardModule(Uri uri) => new(uri, true, [], [], []);

    private static IOptions<DiagnosticsOptions> Options(ForStepConvention forStep = ForStepConvention.Implicit, ByRefConvention byRef = ByRefConvention.Explicit)
        => Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions { ForStep = forStep, ByRefModifier = byRef });

    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto? semantics = null, Uri? uri = null)
    {
        uri ??= Program;
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id)
        => Assert.IsTrue(findings.All(finding => finding is { Severity: DiagnosticSeverity.Information } && finding.Id == id), string.Join("; ", findings.Select(finding => finding.Message)));

    private static string Text(string source, AnalyzerFinding finding)
    {
        var line = source.ReplaceLineEndings("\n").Split('\n')[finding.Range.Start.Line];
        return line[finding.Range.Start.Character..finding.Range.End.Character];
    }

    #region RDC00313 Step 1, RDC00116 no Step

    private const string Loops = "Public Sub DoSomething()\n    Dim i As Long\n    For i = 1 To 10 Step 1\n    Next\n    For i = 1 To 10\n    Next\n    For i = 10 To 1 Step -1\n    Next\n    For i = 1 To 10 Step 2\n    Next\nEnd Sub";

    [TestMethod]
    public void StepOne_IsRedundant_UnderTheImplicitConvention()
    {
        var source = Module(Loops);
        var findings = Run(new ForStepAnalyzer(Options()), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.RedundantStepOne);
        Assert.AreEqual("1", Text(source, findings[0]), "the step");
    }

    [TestMethod]
    public void ALoopWithNoStep_IsReported_UnderTheExplicitConvention()
    {
        var source = Module(Loops);
        var findings = Run(new ForStepAnalyzer(Options(forStep: ForStepConvention.Explicit)), source);

        CollectionAssert.AreEqual(new[] { 5 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.ImplicitForStep);
        Assert.AreEqual("For i = 1 To 10", Text(source, findings[0]), "the header, up to the end value");
    }

    [TestMethod]
    public void TheConventionsDefault_IsImplicit()
        => Assert.AreEqual(ForStepConvention.Implicit, new DiagnosticsOptions().ForStep);

    #endregion

    #region RDC00209 Boolean assigned in If…Else

    [TestMethod]
    public void ABooleanLiteralAssignedInEachBranch_IsTheCondition()
    {
        var source = Module("Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean\n    If foo > 0 Then\n        bar = True\n    Else\n        bar = False\n    End If\nEnd Sub");
        var findings = Run(new BooleanAssignedInIfElseAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.BooleanAssignedInIfElse);
        Assert.AreEqual("If foo > 0", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "'bar'");
    }

    [TestMethod]
    public void TheNegation_AndAMemberOfAnObject_AreReported()
    {
        var source = Module("Public Done As Boolean\nPublic Sub DoSomething(ByVal foo As Long)\n    If foo > 0 Then\n        Let Program.Done = False\n    Else\n        Program.Done = True\n    End If\nEnd Sub");
        var findings = Run(new BooleanAssignedInIfElseAnalyzer(), source);

        Assert.HasCount(1, findings);
        StringAssert.Contains(findings[0].Message, "'Program.Done'");
    }

    [TestMethod]
    [DataRow("bar = True", "bar = True", DisplayName = "the same value")]
    [DataRow("bar = True", "baz = False", DisplayName = "two names")]
    [DataRow("bar = 1", "bar = 0", DisplayName = "no Boolean literal")]
    [DataRow("bar = True\n        Debug.Print 1", "bar = False", DisplayName = "a second statement")]
    [DataRow("Set obj = Nothing", "Set obj = Nothing", DisplayName = "a Set")]
    public void AnythingElse_IsNotReported(string then, string otherwise)
        => Assert.IsEmpty(Run(new BooleanAssignedInIfElseAnalyzer(),
            Module($"Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean, baz As Boolean, obj As Object\n    If foo > 0 Then\n        {then}\n    Else\n        {otherwise}\n    End If\nEnd Sub")));

    [TestMethod]
    public void AnElseIf_MakesItSomethingElse()
        => Assert.IsEmpty(Run(new BooleanAssignedInIfElseAnalyzer(),
            Module("Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean\n    If foo > 0 Then\n        bar = True\n    ElseIf foo < 0 Then\n        bar = True\n    Else\n        bar = False\n    End If\nEnd Sub")));

    #endregion

    #region RDC00210 Stop

    [TestMethod]
    public void Stop_IsReported()
    {
        var source = Module("Public Sub DoSomething()\n    Debug.Print 1\n    Stop\nEnd Sub");
        var findings = Run(new StopStatementAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.StopStatement);
        Assert.AreEqual("Stop", Text(source, findings[0]));
    }

    [TestMethod]
    public void End_IsNoStop()
        => Assert.IsEmpty(Run(new StopStatementAnalyzer(), Module("Public Sub DoSomething()\n    End\nEnd Sub")));

    #endregion

    #region RDC00314 ByRef written, RDC00104 under the other convention

    private const string ByRefs = "Declare PtrSafe Function Beep Lib \"kernel32\" (ByRef dwMs As Long) As Long\nPublic Sub DoSomething(ByRef foo As Long, bar As Long, ByVal baz As Long, ParamArray rest() As Variant)\nEnd Sub\nPublic Property Let Value(ByRef v As Long)\nEnd Property";

    [TestMethod]
    public void ByRefWritten_IsRedundant_UnderTheImplicitConvention()
    {
        var source = Module(ByRefs);
        var findings = Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), source, StandardModule(Program));

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings), "neither a property's value, nor a Declare");
        AssertAre(findings, RDCoreDiagnosticId.RedundantByRefModifier);
        Assert.AreEqual("ByRef foo As Long", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "'foo'");
    }

    [TestMethod]
    public void ByRefWritten_IsTheConvention_ByDefault()
    {
        var source = Module(ByRefs);

        Assert.IsEmpty(Run(new RedundantByRefModifierAnalyzer(Options()), source, StandardModule(Program)));
        CollectionAssert.AreEqual(new[] { 2 }, Lines(Run(new ImplicitByRefModifierAnalyzer(Options()), source)), "bar states neither");
    }

    [TestMethod]
    public void NeitherWritten_IsTheConvention_UnderTheImplicitOne()
        => Assert.IsEmpty(Run(new ImplicitByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), Module(ByRefs)));

    [TestMethod]
    public void WithNoSemantics_NothingIsSaid()
        => Assert.IsEmpty(Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), Module(ByRefs)));

    [TestMethod]
    public async Task AnImplementationOrAnEventHandler_RepeatsTheDeclarationItMatches()
    {
        var shape = ModuleWorkspace.ClassModule("IShape", "Public Sub Move(ByRef dx As Long)", "End Sub");
        var button = ModuleWorkspace.ClassModule("Button", "Public Event Changed(ByRef value As Long)", "Public Sub Press()", "    RaiseEvent Changed(1)", "End Sub");
        var square = ModuleWorkspace.ClassModule("Square",
            "Implements IShape", "Private WithEvents Source As Button",
            "Private Sub IShape_Move(ByRef dx As Long)", "End Sub",
            "Private Sub Source_Changed(ByRef value As Long)", "End Sub",
            "Private Sub Grow(ByRef by As Long)", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([("IShape", shape), ("Button", button), ("Square", square)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Square");

        var findings = Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), square, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Square.cls")));

        Assert.HasCount(1, findings);
        StringAssert.Contains(findings[0].Message, "'by'");
    }

    #endregion

    #region RDC00315 a module option that states the default

    [TestMethod]
    public void TheDefaultOptions_AreRedundant()
    {
        var source = Module("Option Explicit\nOption Base 0\nOption Compare Binary\nPublic Sub DoSomething()\nEnd Sub");
        var findings = Run(new RedundantModuleOptionAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 2, 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.RedundantModuleOption);
        StringAssert.StartsWith(findings[0].Message, "Option Base 0");
        StringAssert.StartsWith(findings[1].Message, "Option Compare Binary");
    }

    [TestMethod]
    public void TheOtherOptions_AreNot()
        => Assert.IsEmpty(Run(new RedundantModuleOptionAnalyzer(), Module("Option Explicit\nOption Base 1\nOption Compare Text\nOption Private Module\nPublic Sub DoSomething()\nEnd Sub")));

    #endregion
}

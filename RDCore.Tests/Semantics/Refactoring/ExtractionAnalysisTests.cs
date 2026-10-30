using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring.ExtractMethod;
using RDCore.Tests.Semantics.Flow;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// What a selection extracted as a procedure is given, what it gives back, and what moves with it: from the data flow of the source procedure, never from where the
/// names are on the page (<strong>Extract Method §3.2, §3.3</strong>).
/// </summary>
[TestClass]
public sealed class ExtractionAnalysisTests
{
    // the plan of extracting the lines of the body from `first` to `last` (zero-based).
    private static ExtractionPlan Plan(string body, int first, int last, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub")
    {
        var analyzed = FlowFixture.Analyze(body, header, end);
        var requested = new SourceRange(analyzed.FirstBodyLine + first, 0, analyzed.FirstBodyLine + last, 1);
        Assert.IsTrue(ExtractionSelection.TrySnap(analyzed.Procedure, analyzed.Instructions, requested, out var selection, out var refusal, out _), $"refused: {refusal}");
        return ExtractionAnalysis.Plan(selection!, analyzed.Graph, analyzed.Accesses, analyzed.Model.Procedure, analyzed.Scopes);
    }

    private static ExtractionParameter Parameter(ExtractionPlan plan, string name) => plan.Parameters.Single(parameter => parameter.Name == name);

    // legacy Rubberduck passed x ByVal: nothing reads it after the selection on the page. The loop reads it at the top on the next iteration, the write was lost,
    // and the loop never ended.
    [TestMethod]
    public void AVariableTheLoopReadsAgain_IsPassedByRef()
    {
        var plan = Plan("Dim x As Long\nDo While x < 10\nx = x + 1\nLoop", 2, 2);

        var x = Parameter(plan, "x");
        Assert.AreEqual(ExtractionPassing.ByRef, x.Passing);
        Assert.AreEqual(ExtractionPassingReason.ReadAndWritten, x.Reason);
        Assert.IsTrue(x.IsInput);
        Assert.IsTrue(x.IsOutput);
    }

    [TestMethod]
    public void AVariableOnlyRead_IsPassedByVal()
    {
        var plan = Plan("Dim x As Long\nx = 5\nDebug.Print x + n", 2, 2);

        Assert.AreEqual(ExtractionPassing.ByVal, Parameter(plan, "x").Passing);
        Assert.AreEqual(ExtractionPassing.ByVal, Parameter(plan, "n").Passing, "a parameter of the source procedure, too");
    }

    [TestMethod]
    public void AVariableOnlyWritten_AndReadAfter_MayBeTheReturnValue()
    {
        var plan = Plan("Dim y As Long\ny = n * 2\nDebug.Print y", 1, 1);

        var y = Parameter(plan, "y");
        Assert.AreEqual(ExtractionPassingReason.WrittenOnly, y.Reason);
        CollectionAssert.AreEqual(new[] { "y" }, plan.ReturnCandidates.Select(parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public void AValueWrittenThenReplacedBeforeItIsRead_IsNoOutput()
    {
        var plan = Plan("Dim y As Long\ny = 1\ny = 2\nDebug.Print y", 1, 1);

        Assert.IsFalse(plan.Parameters.Any(parameter => parameter.Name == "y"));
        Assert.IsFalse(plan.Locals.Single(local => local.Name == "y").MovesDeclaration, "the source procedure uses it too");
    }

    [TestMethod]
    public void AVariableUsedOnlyInTheSelection_MovesIn_WithItsDeclaration()
    {
        var plan = Plan("Dim t As Long\nt = n\nDebug.Print t", 1, 2);

        Assert.IsTrue(plan.Locals.Single(local => local.Name == "t").MovesDeclaration);
        CollectionAssert.AreEqual(new[] { "n" }, plan.Parameters.Select(parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public void AVariableDeclaredInTheSelection_AndUsedAfter_HasItsDeclarationMovedOut()
    {
        var analyzed = FlowFixture.Analyze("Dim a As Long\na = n\nDebug.Print a");
        var plan = Plan("Dim a As Long\na = n\nDebug.Print a", 0, 1);

        CollectionAssert.AreEqual(new[] { analyzed.Variable("a") }, plan.MovedOut.ToArray());
        Assert.AreEqual(ExtractionPassingReason.WrittenOnly, Parameter(plan, "a").Reason);
    }

    [TestMethod]
    [DataRow("Dim arr(1 To 3) As Long\narr(1) = n\nDebug.Print arr(1)", "arr", ExtractionPassingReason.Array, DisplayName = "an array")]
    [DataRow("Dim p As Point\np.X = n\nDebug.Print p.X", "p", ExtractionPassingReason.UserDefinedType, DisplayName = "a variable of a Type")]
    [DataRow("Dim s As String * 5\ns = \"abc\"\nDebug.Print s", "s", ExtractionPassingReason.FixedLengthString, DisplayName = "a fixed-length string")]
    [DataRow("Static k As Long\nk = k + n\nDebug.Print k", "k", ExtractionPassingReason.Static, DisplayName = "a Static local")]
    public void WhatVbaPassesByReferenceOnly_IsPassedByRef(string body, string name, ExtractionPassingReason reason)
    {
        var plan = Plan(body, 1, 1);

        var parameter = Parameter(plan, name);
        Assert.AreEqual(ExtractionPassing.ByRef, parameter.Passing);
        Assert.AreEqual(reason, parameter.Reason);
    }

    [TestMethod]
    public void AFieldOfTheModule_IsNotPassed()
        => CollectionAssert.AreEqual(new[] { "n" }, Plan("field = n", 0, 0).Parameters.Select(parameter => parameter.Name).ToArray());

    [TestMethod]
    public void AnAssignmentOfTheFunctionsResult_IsSaidOf()
    {
        var plan = Plan("Run = n * 2", 0, 0, header: "Public Function Run(ByVal n As Long) As Long", end: "End Function");

        Assert.IsTrue(plan.AssignsResult);
        Assert.IsFalse(plan.Parameters.Any(parameter => parameter.Name == "Run"));
    }

    [TestMethod]
    public void TheParameters_AreInTheOrderTheSelectionFirstTouchesThem()
    {
        var plan = Plan("Dim a As Long\nDim b As Long\nb = n\na = b + 1\nDebug.Print a + b", 2, 3);

        CollectionAssert.AreEqual(new[] { "n", "b", "a" }, plan.Parameters.Select(parameter => parameter.Name).ToArray());
    }
}

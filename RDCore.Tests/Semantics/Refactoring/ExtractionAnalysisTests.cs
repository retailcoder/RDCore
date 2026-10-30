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
    private static ExtractionPlan Plan(string body, int first, int last, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true)
        => ExtractionFixture.Plan(body, first, last, header, end, optionExplicit);

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
    public void AReadOfTheFunctionsResult_GivenBeforeTheSelection_IsSaidOf()
    {
        var plan = Plan("Run = n\nRun = Run * 2", 1, 1, header: "Public Function Run(ByVal n As Long) As Long", end: "End Function");

        Assert.IsTrue(plan.AssignsResult);
        Assert.IsTrue(plan.ReadsResult);
    }

    [TestMethod]
    public void AnAssignmentOfTheFunctionsResult_ReadsNoneOfIt()
        => Assert.IsFalse(Plan("Run = n * 2", 0, 0, header: "Public Function Run(ByVal n As Long) As Long", end: "End Function").ReadsResult);

    // §3.2: the lifetime of a Static is the source procedure's, so its declaration stays there even when only the selection uses it.
    [TestMethod]
    public void AStaticDeclaredInTheSelection_KeepsItsDeclarationInTheSourceProcedure()
    {
        var analyzed = FlowFixture.Analyze("Static k As Long\nk = k + n");
        var plan = Plan("Static k As Long\nk = k + n", 0, 1);

        CollectionAssert.AreEqual(new[] { analyzed.Variable("k") }, plan.MovedOut.ToArray());
        Assert.AreEqual(ExtractionPassingReason.Static, Parameter(plan, "k").Reason);
    }

    [TestMethod]
    public void AnUndeclaredVariable_IsDeclaredByTheNewProcedure()
    {
        var plan = Plan("t = n\nDebug.Print t", 0, 1, optionExplicit: false);

        var t = plan.Locals.Single(local => local.Name == "t");
        Assert.IsTrue(t.IsUndeclared);
        Assert.IsFalse(t.MovesDeclaration, "it has no declaration to move");
        Assert.IsNull(t.Type, "a Variant");
    }

    [TestMethod]
    public void AnUndeclaredVariable_ReadAfter_IsAnUndeclaredParameter()
        => Assert.IsTrue(Parameter(Plan("t = n\nDebug.Print t", 0, 0, optionExplicit: false), "t").IsUndeclared);

    [TestMethod]
    public void AParamArrayTheSelectionReads_IsPassedAsAVariant()
    {
        var plan = Plan("Debug.Print args(0)", 0, 0, header: "Public Sub Run(ParamArray args() As Variant)");

        var args = Parameter(plan, "args");
        Assert.AreEqual(ExtractionPassingReason.ParamArray, args.Reason);
        Assert.AreEqual(ExtractionPassing.ByVal, args.Passing);
        Assert.IsTrue(args.MayBeMissing);
        Assert.IsTrue(plan.IsExtractable);
    }

    // MS-VBAL §5.3.1.5: the elements of a ParamArray are the caller's variables, which the copy a new procedure is given cannot write.
    [TestMethod]
    [DataRow("args(0) = 1", DisplayName = "an element assigned")]
    [DataRow("Bump args(0)", DisplayName = "an element passed by reference")]
    public void AParamArrayTheSelectionWrites_IsRefused(string body)
    {
        var plan = Plan(body, 0, 0, header: "Public Sub Run(ParamArray args() As Variant)");

        var refusal = plan.Refusals.Single();
        Assert.AreEqual(ExtractionRefusalKind.ParamArrayWritten, refusal.Kind);
        Assert.AreEqual("args", refusal.Name);
        Assert.IsNotNull(refusal.Location);
    }

    [TestMethod]
    [DataRow("Optional ByVal v As Variant", true, DisplayName = "an Optional Variant")]
    [DataRow("Optional ByVal v As Variant = 1", false, DisplayName = "an Optional Variant with a default value")]
    [DataRow("Optional ByVal v As Long", false, DisplayName = "an Optional Long")]
    [DataRow("ByVal v As Variant", false, DisplayName = "a required Variant")]
    public void AParameterThatCanBeMissing_IsSaidOf(string parameter, bool mayBeMissing)
        => Assert.AreEqual(mayBeMissing, Parameter(Plan("Debug.Print IsMissing(v)", 0, 0, header: $"Public Sub Run({parameter})"), "v").MayBeMissing);

    [TestMethod]
    [DataRow("Dim p As Point\nWith p\n.X = n\nEnd With", "p", ExtractionPassing.ByRef, DisplayName = "a variable of a Type, whose members the selection writes")]
    [DataRow("Dim c As Collection\nSet c = New Collection\nWith c\n.Add n\nEnd With", "c", ExtractionPassing.ByVal, DisplayName = "an object")]
    public void TheVariableOfTheWithBlockTheSelectionIsInside_IsGivenToTheNewProcedure(string body, string name, ExtractionPassing passing)
    {
        var lines = body.Split('\n');
        var inside = Array.FindIndex(lines, line => line.StartsWith('.'));
        var plan = Plan(body, inside, inside);

        Assert.IsNotNull(plan.WithTarget);
        Assert.AreEqual(name, plan.WithTarget.Name);
        Assert.AreEqual(passing, plan.WithTarget.Passing);
        Assert.IsTrue(plan.IsExtractable);
    }

    [TestMethod]
    public void AWithBlockTheSelectionHoldsWhole_IsExtractedAsItIs()
        => Assert.IsNull(Plan("Dim p As Point\nWith p\n.X = n\nEnd With", 1, 3).WithTarget);

    // the block evaluates its expression once; a copy of it at the call site would evaluate it again, and give the new procedure another object.
    [TestMethod]
    public void AWithBlockOfAnExpression_TheSelectionUsesAMemberOf_IsRefused()
    {
        var plan = Plan("With New Collection\n.Add n\nEnd With", 1, 1);

        Assert.AreEqual(ExtractionRefusalKind.WithTargetIsNotAVariable, plan.Refusals.Single().Kind);
        Assert.IsNull(plan.WithTarget);
    }

    [TestMethod]
    public void TheParameters_AreInTheOrderTheSelectionFirstTouchesThem()
    {
        var plan = Plan("Dim a As Long\nDim b As Long\nb = n\na = b + 1\nDebug.Print a + b", 2, 3);

        CollectionAssert.AreEqual(new[] { "n", "b", "a" }, plan.Parameters.Select(parameter => parameter.Name).ToArray());
    }
}

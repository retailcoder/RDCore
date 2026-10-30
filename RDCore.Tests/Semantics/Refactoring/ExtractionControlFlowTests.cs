using RDCore.SDK.Semantics.Refactoring.ExtractMethod;
using static RDCore.Tests.Semantics.Refactoring.ExtractionFixture;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The edges of a selection, and the error handling it runs under: what is refused, where control leaves it for, and what extracting it changes (<strong>Extract
/// Method §3.1, §3.4</strong>).
/// </summary>
[TestClass]
public sealed class ExtractionControlFlowTests
{
    private static ExtractionRefusalKind[] RefusalsOf(ExtractionPlan plan) => [.. plan.Refusals.Select(refusal => refusal.Kind)];

    // §3.1, inside → inside: the labels move with the code.
    [TestMethod]
    public void AJumpToALabelInTheSelection_MovesWithIt()
    {
        var plan = Plan("Dim i As Long\nGoTo L\ni = 1\nL:\ni = 2", 1, 4);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsEmpty(plan.Exits);
    }

    [TestMethod]
    public void ALabelOnTheSelectionsFirstLine_IsInIt()
    {
        var plan = Plan("Dim i As Long\nL:\ni = i + 1\nIf i < n Then GoTo L", 1, 3);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.AreEqual(plan.Selection.Statements[0].SourceLocation.Range.Start.Line - 1, plan.Selection.Range.Start.Line, "the selection starts at the label");
        CollectionAssert.AreEqual(new[] { "L" }, plan.Selection.Labels.ToArray());
    }

    [TestMethod]
    public void AJumpFromOutside_ToALabelOnTheSelectionsFirstLine_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("If n > 0 Then GoTo L\nL:\nDebug.Print 1", 1, 2)), ExtractionRefusalKind.JumpIntoTheSelection);

    // the label stays where it is, and names the call.
    [TestMethod]
    public void AJumpFromOutside_ToALabelJustBeforeTheSelection_LandsOnTheCall()
    {
        var plan = Plan("If n > 0 Then GoTo L\nL:\nDebug.Print 1", 2, 2);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // §3.1, outside → inside.
    [TestMethod]
    public void AJumpFromOutside_ToALabelInTheSelection_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("If n > 0 Then GoTo L\nDebug.Print 1\nL:\nDebug.Print 2", 1, 3)), ExtractionRefusalKind.JumpIntoTheSelection);

    [TestMethod]
    public void AnExitOfTheProcedure_IsAnExitTheCallerTakesToo()
    {
        var plan = Plan("If n > 0 Then Exit Sub\nDebug.Print n", 0, 0);

        Assert.IsTrue(plan.IsExtractable);
        Assert.AreEqual(ExtractionExitKind.Procedure, plan.Exits.Single().Kind);
    }

    [TestMethod]
    public void AnExitOfALoopTheSelectionIsInside_IsAnExit()
    {
        var plan = Plan("Dim i As Long\nFor i = 1 To 3\nIf i = n Then Exit For\nDebug.Print i\nNext", 2, 3);

        Assert.AreEqual(ExtractionExitKind.Loop, plan.Exits.Single().Kind);
    }

    [TestMethod]
    public void AnExitOfALoopTheSelectionHoldsWhole_IsNoExit()
        => Assert.IsEmpty(Plan("Dim i As Long\nFor i = 1 To 3\nIf i = n Then Exit For\nDebug.Print i\nNext\nDebug.Print n", 1, 4).Exits);

    [TestMethod]
    public void AJumpToALabelOutside_IsAnExitToIt()
    {
        var plan = Plan("If n = 0 Then GoTo Done\nDebug.Print n\nDone:\nDebug.Print 0", 0, 0);

        var exit = plan.Exits.Single();
        Assert.AreEqual(ExtractionExitKind.Label, exit.Kind);
        Assert.AreEqual("Done", exit.Label);
    }

    [TestMethod]
    public void TwoExitsToOneTarget_AreOneExit()
        => Assert.HasCount(1, Plan("If n = 0 Then Exit Sub\nIf n = 1 Then Exit Sub\nDebug.Print n", 0, 1).Exits);

    [TestMethod]
    public void AGoSubToARoutineOutside_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("GoSub R\nExit Sub\nR:\nDebug.Print n\nReturn", 0, 0)), ExtractionRefusalKind.GoSubToOutside);

    [TestMethod]
    public void APartOfAGoSubRoutine_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("GoSub R\nExit Sub\nR:\nDebug.Print n\nReturn", 3, 4)), ExtractionRefusalKind.ReturnOfAGoSubOutside);

    [TestMethod]
    public void EndAndStop_AreExtracted()
    {
        var plan = Plan("Debug.Print n\nStop\nEnd", 0, 2);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsEmpty(plan.Exits);
    }

    // §3.4, none: nothing changes.
    [TestMethod]
    public void ASelectionUnderNoOnError_ChangesNothing()
    {
        var plan = Plan("Debug.Print 1 / n", 0, 0);

        Assert.IsFalse(plan.StartsWithResumeNext);
        Assert.IsEmpty(plan.BehaviourChanges);
    }

    // §3.4, only On Error Resume Next: an error resumes inside the selection inline, and does in the new procedure too.
    [TestMethod]
    public void ASelectionUnderOnErrorResumeNext_StartsWithIt()
    {
        var plan = Plan("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print n", 1, 2);

        Assert.IsTrue(plan.StartsWithResumeNext);
        Assert.IsEmpty(plan.BehaviourChanges);
    }

    [TestMethod]
    public void ASelectionUnderOnErrorResumeNextOnSomePaths_IsABehaviourChange()
    {
        var plan = Plan("If n > 0 Then On Error Resume Next\nDebug.Print 1 / n", 1, 1);

        Assert.IsFalse(plan.StartsWithResumeNext);
        Assert.AreEqual(ExtractionBehaviourChangeKind.ResumeNextOnSomePaths, plan.BehaviourChanges.Single().Kind);
    }

    // §3.4, On Error GoTo: a Resume Next of the handler would skip the rest of the extracted code.
    [TestMethod]
    public void AHandlerThatResumes_IsABehaviourChange_NamingItAndItsResume()
    {
        var plan = Plan("On Error GoTo H\nDebug.Print 1 / n\nDebug.Print n\nExit Sub\nH:\nResume Next", 1, 2);

        var change = plan.BehaviourChanges.Single();
        Assert.AreEqual(ExtractionBehaviourChangeKind.CallerHandlerResumes, change.Kind);
        CollectionAssert.AreEqual(new[] { "H" }, change.Handlers.ToArray());
        Assert.HasCount(1, change.Resumes);
        Assert.IsFalse(change.IsNote);
        Assert.IsTrue(plan.IsExtractable, "the user accepts it, or cancels");
    }

    [TestMethod]
    public void AHandlerThatDoesNotResume_ChangesOnlyErl_AsANote()
    {
        var change = Plan("On Error GoTo H\nDebug.Print 1 / n\nExit Sub\nH:\nDebug.Print Err.Number", 1, 1).BehaviourChanges.Single();

        Assert.AreEqual(ExtractionBehaviourChangeKind.ErlOnly, change.Kind);
        Assert.IsTrue(change.IsNote);
    }

    [TestMethod]
    public void LineNumbersUnderAHandler_AreABehaviourChangeOfErl()
        => CollectionAssert.Contains(
            Plan("On Error GoTo H\n10 Debug.Print 1 / n\nExit Sub\nH:\nDebug.Print Erl", 1, 1).BehaviourChanges.Select(change => change.Kind).ToArray(),
            ExtractionBehaviourChangeKind.ErlReportsTheCall);

    // §3.4: the On Error of the selection would be the new procedure's, and what follows the call would run as it did before the selection.
    [TestMethod]
    public void AnOnErrorThatChangesTheStateAfterTheSelection_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("On Error Resume Next\nDebug.Print 1 / n", 0, 0)), ExtractionRefusalKind.ErrorHandlingChanged);

    [TestMethod]
    public void AnOnErrorTheSelectionRestores_IsExtracted()
    {
        var plan = Plan("On Error Resume Next\nDebug.Print 1 / n\nOn Error GoTo 0\nDebug.Print n", 0, 2);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsFalse(plan.StartsWithResumeNext, "it sets its own");
    }

    [TestMethod]
    public void AnOnErrorGoToAHandlerOutside_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("On Error GoTo H\nDebug.Print 1 / n\nOn Error GoTo 0\nExit Sub\nH:\nResume Next", 0, 2)),
            ExtractionRefusalKind.HandlerOutsideTheSelection);

    [TestMethod]
    public void AResumeOfAnErrorRaisedBefore_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("On Error GoTo H\nDebug.Print 1 / n\nExit Sub\nH:\nDebug.Print Err.Number\nResume Next", 5, 5)),
            ExtractionRefusalKind.ResumeOfAnErrorBefore);

    // Err is global: reading it about an error raised before the selection is the same from the new procedure.
    [TestMethod]
    public void AReadOfErrInAHandler_IsExtracted()
    {
        var plan = Plan("On Error GoTo H\nDebug.Print 1 / n\nExit Sub\nH:\nDebug.Print Err.Number\nResume Next", 4, 4);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // the second review's 2a-6: a handler that ends without a Resume leaves the error active inline; extracted, the call ends with its callee, and the error with it.
    [TestMethod]
    public void AHandlerOfItsOwnThatFallsThroughWithTheErrorActive_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("Debug.Print n\nOn Error GoTo H\nDebug.Print 1 / n\nH:\nOn Error GoTo 0\nDebug.Print n", 1, 4)),
            ExtractionRefusalKind.ErrorHandlingChanged);

    [TestMethod]
    public void AHandlingOfItsOwnThatResumes_IsExtracted()
    {
        var plan = Plan("Debug.Print n\nOn Error GoTo H\nDebug.Print 1 / n\nGoTo Done\nH:\nResume Done\nDone:\nOn Error GoTo 0\nDebug.Print n", 1, 7);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // the second review's 2a-5: the On Error Resume Next the new procedure starts with clears what Err says of an error before the selection.
    [TestMethod]
    public void AReadOfErrUnderAnInheritedResumeNext_IsABehaviourChange()
    {
        var plan = Plan("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print Err.Number", 2, 2);

        Assert.IsTrue(plan.StartsWithResumeNext);
        CollectionAssert.Contains(plan.BehaviourChanges.Select(change => change.Kind).ToArray(), ExtractionBehaviourChangeKind.ErrClearedOnEntry);
    }

    [TestMethod]
    public void NoReadOfErrUnderAnInheritedResumeNext_ChangesNothing()
        => Assert.IsEmpty(Plan("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print 2 / n", 2, 2).BehaviourChanges);
}

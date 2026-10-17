namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> "If the redimensioned variable is currently locked by a ByRef formal parameter runtime Error 10 is raised" - as MS-VBA
/// reads it: an array is locked while an element of it is passed by reference, or a <c>For Each</c> over it runs, and a locked array is not re-dimensioned,
/// erased or assigned another.
/// </summary>
/// <remarks>
/// The whole array passed by reference is not locked: the parameter is another name for the variable, and sees what a <c>ReDim</c> of either gives it. Each
/// expectation is what MS-VBA prints for the same code, recorded in Excel.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimArrayLockTests
{
    private const string Module = "Public arr() As Long\r\nPublic v As Variant";

    private static Task<string[]> RunAsync(string procedures, params string[] body)
        => ArrayStatementTargetTests.RunProgramAsync(Module, procedures, body);

    #region the whole array, passed by reference

    [TestMethod]
    public async Task ReDim_OfTheVariable_WhileACalleeHoldsTheWholeArrayByRef_IsNotLocked_AndTheParameterSeesIt()
        => CollectionAssert.AreEqual(new[] { "0 5" },
            await RunAsync(
                "Public Sub Hold(ByRef p() As Long)\r\nOn Error Resume Next\r\nReDim arr(1 To 5)\r\nDebug.Print Err.Number & \" \" & UBound(p)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Hold arr"));

    [TestMethod]
    public async Task ReDim_OfAnotherModulesVariable_NamedThroughTheModule_WhileACalleeHoldsItByRef_IsNotLocked()
        => CollectionAssert.AreEqual(new[] { "0" },
            await RunAsync(
                "Public Sub Hold(ByRef p() As Long)\r\nOn Error Resume Next\r\nReDim Mod2.Items(1 To 5)\r\nDebug.Print Err.Number\r\nEnd Sub",
                "ReDim Mod2.Items(1 To 3)", "Hold Mod2.Items"));

    [TestMethod]
    public async Task AnotherModulesArray_NamedThroughTheModule_PassedByRef_IsTheParameter()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync(
                "Public Sub Grow(ByRef p() As Long)\r\nReDim p(1 To 9)\r\nEnd Sub",
                "ReDim Mod2.Items(1 To 3)", "Grow Mod2.Items", "Debug.Print UBound(Mod2.Items)"));

    [TestMethod]
    public async Task ReDim_OfTheParameterItself_IsWhatPassingItByRefIsFor()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync(
                "Public Sub Grow(ByRef p() As Long)\r\nReDim p(1 To 9)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Grow arr", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task ReDim_OfAParameterThatWasPassedDownAChainOfByRefCalls_IsStillTheParameters()
        => CollectionAssert.AreEqual(new[] { "7" },
            await RunAsync(
                "Public Sub First(ByRef p() As Long)\r\nSecond p\r\nEnd Sub\r\nPublic Sub Second(ByRef q() As Long)\r\nReDim q(1 To 7)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "First arr", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task ReDim_OfAVariableNoParameterHolds_IsNeverLocked()
        => CollectionAssert.AreEqual(new[] { "4" },
            await RunAsync(
                "Public Sub Other(ByRef p() As Long)\r\nReDim arr(1 To 4)\r\nEnd Sub",
                "Dim mine() As Long", "ReDim mine(1 To 2)", "Other mine", "Debug.Print UBound(arr)"));

    #endregion

    #region an element, passed by reference

    [TestMethod]
    [DataRow("ReDim arr(1 To 5)", DisplayName = "ReDim")]
    [DataRow("ReDim Preserve arr(1 To 5)", DisplayName = "ReDim Preserve")]
    [DataRow("Erase arr", DisplayName = "Erase")]
    public async Task TheArray_WhileACalleeHoldsAnElementOfItByRef_IsLocked_Error10(string statement)
        => CollectionAssert.AreEqual(new[] { "10", "3" },
            await RunAsync(
                $"Public Sub Hold(ByRef x As Long)\r\nOn Error Resume Next\r\n{statement}\r\nDebug.Print Err.Number\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Hold arr(1)", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task TheArray_WhileACalleeHoldsAnElementOfItByRef_IsNotAssignedAnother_Error10()
        => CollectionAssert.AreEqual(new[] { "10 3" },
            await RunAsync(
                "Public Sub Hold(ByRef x As Long)\r\nOn Error Resume Next\r\nDim other() As Long\r\nReDim other(1 To 7)\r\narr = other\r\nDebug.Print Err.Number & \" \" & UBound(arr)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Hold arr(1)"));

    [TestMethod]
    public async Task AnArrayAVariantHolds_WhileACalleeHoldsAnElementOfItByRef_IsLocked_Error10()
        => CollectionAssert.AreEqual(new[] { "10" },
            await RunAsync(
                "Public Sub Hold(ByRef x As Variant)\r\nOn Error Resume Next\r\nReDim v(1 To 5)\r\nDebug.Print Err.Number\r\nEnd Sub",
                "v = Array(1, 2, 3)", "Hold v(1)"));

    [TestMethod]
    public async Task TheArray_OnceTheCallThatHeldAnElementOfItHasReturned_IsNoLongerLocked()
        => CollectionAssert.AreEqual(new[] { "6" },
            await RunAsync(
                "Public Sub Hold(ByRef x As Long)\r\nEnd Sub",
                "ReDim arr(1 To 3)", "Hold arr(1)", "ReDim arr(1 To 6)", "Debug.Print UBound(arr)"));

    // MS-VBAL §5.3.1.11: an element is a variable, and what the parameter is assigned is what the element holds.
    [TestMethod]
    [DataRow("Public Sub Bump(ByRef x As Long)\r\nx = x + 1\r\nEnd Sub", DisplayName = "A Long parameter")]
    [DataRow("Public Sub Bump(ByRef x As Variant)\r\nx = x + 1\r\nEnd Sub", DisplayName = "A Variant parameter")]
    public async Task AnElement_PassedByRef_IsWhatTheCalleeAssigns(string procedure)
        => CollectionAssert.AreEqual(new[] { "2" },
            await RunAsync(procedure, "ReDim arr(1 To 3)", "arr(2) = 1", "Bump arr(2)", "Debug.Print arr(2)"));

    [TestMethod]
    public async Task AnElementOfAnArrayAVariantHolds_PassedByRef_IsWhatTheCalleeAssigns()
        => CollectionAssert.AreEqual(new[] { "3" },
            await RunAsync("Public Sub Bump(ByRef x As Variant)\r\nx = x + 1\r\nEnd Sub", "v = Array(1, 2, 3)", "Bump v(1)", "Debug.Print v(1)"));

    [TestMethod]
    public async Task AnElementOfAFixedSizeArray_PassedByRef_IsWhatTheCalleeAssigns()
        => CollectionAssert.AreEqual(new[] { "42" },
            await RunAsync("Public Sub Answer(ByRef x As Long)\r\nx = 42\r\nEnd Sub", "Dim fixedArr(1 To 3) As Long", "Answer fixedArr(2)", "Debug.Print fixedArr(2)"));

    #endregion

    #region For Each

    [TestMethod]
    [DataRow("ReDim arr(1 To 5)", DisplayName = "ReDim")]
    [DataRow("Erase arr", DisplayName = "Erase")]
    public async Task TheArray_WhileAForEachOverItRuns_IsLocked_Error10(string statement)
        => CollectionAssert.AreEqual(new[] { "10" },
            await RunAsync(string.Empty,
                "On Error Resume Next", "ReDim arr(1 To 3)", "Dim x As Variant", "For Each x In arr", statement, "Exit For", "Next", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task TheArray_OnceTheForEachOverItIsOver_IsNoLongerLocked()
        => CollectionAssert.AreEqual(new[] { "0 5" },
            await RunAsync(string.Empty,
                "On Error Resume Next", "ReDim arr(1 To 3)", "Dim x As Variant", "For Each x In arr", "Next", "ReDim arr(1 To 5)", "Debug.Print Err.Number & \" \" & UBound(arr)"));

    #endregion
}

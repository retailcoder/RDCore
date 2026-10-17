using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable of a procedural module named through its module - <c>Program.Count</c> - is the variable the bare name is (<strong>MS-VBAL
/// §5.6.12</strong>): it is assigned as the bare name is, by a <c>Let</c> or a <c>Set</c>, from the module itself or from any other. Reading it so
/// worked; assigning it was not implemented.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class ModuleQualifiedAssignmentTests
{
    private static Task<string[]> Run(string classBody, params string[] lines)
        => RunAsync(
            [("Widget", ClassModule("Widget", "Public Name As String", "Public Sub Act()", classBody, "End Sub",
                "Private Sub Class_Terminate()", "    Debug.Print \"terminated \" & Name", "End Sub"))],
            $"Attribute VB_Name = \"Program\"\r\nPublic Kept As Object\r\nPublic Count As Long\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ALet_OfAVariableOfTheModule_ThroughTheModulesName_AssignsTheVariable()
        => CollectionAssert.AreEqual(new[] { "5" }, await Run(string.Empty, "Program.Count = 5", "Debug.Print Count"));

    [TestMethod]
    public async Task ASet_OfAVariableOfTheModule_ThroughTheModulesName_AssignsTheVariable()
        => CollectionAssert.AreEqual(new[] { "Collection" }, await Run(string.Empty, "Set Program.Kept = New Collection", "Debug.Print TypeName(Kept)"));

    // the Widget is destroyed when Main is over: its Terminate prints the last line, its Name being empty.
    [TestMethod]
    public async Task ALet_FromAClass_OfAVariableOfAModule_AssignsTheVariable()
        => CollectionAssert.AreEqual(new[] { "7", "terminated" }, await Run("    Program.Count = 7", "Dim w As New Widget", "w.Act", "Debug.Print Count"));

    // the variable lets go of the object it held, as a Set of its bare name does; the object of the member stays alive while the member runs (Me).
    [TestMethod]
    public async Task ASet_FromAClass_OfAVariableOfAModule_LetsGoOfTheObjectItHeld()
        => CollectionAssert.AreEqual(new[] { "still k", "terminated k", "Nothing" }, await Run(
            "    Set Program.Kept = Nothing\r\n    Debug.Print \"still \" & Name",
            "Dim w As Widget",
            "Set w = New Widget",
            "w.Name = \"k\"",
            "Set Kept = w",
            "Set w = Nothing",
            "Kept.Act",
            "Debug.Print TypeName(Kept)"));
}

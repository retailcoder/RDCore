namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Declare</c>d procedure is external: a call to it goes through the interceptors and is made to the native library it names, which the platform may
/// not have - which is error 53, "File not found", as MS-VBA has it, on any platform.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.4.3 External Procedure Declarations")]
public sealed class DeclareCallTests
{
    private static string Program(params string[] lines)
        => "Attribute VB_Name = \"Program\"\r\n"
            + "Private Declare PtrSafe Function GetQuack Lib \"no-such-library-quack\" () As Long\r\n"
            + "Private Declare PtrSafe Sub Quack Lib \"no-such-library-quack\" Alias \"QuackEx\" (ByVal ms As Long)\r\n"
            + "Public Sub Main()\r\n" + string.Join("\r\n", lines) + "\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AModuleThatDeclaresAnExternalProcedure_Loads()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program("Debug.Print 1")));

    [TestMethod]
    public async Task ACallToAFunctionOfALibraryThatIsNotThere_IsError53()
        => CollectionAssert.AreEqual(new[] { "53" }, await ModuleWorkspace.RunAsync([], Program(
            "On Error Resume Next",
            "Dim t As Long",
            "t = GetQuack()",
            "Debug.Print Err.Number")));

    [TestMethod]
    public async Task ACallToASubOfALibraryThatIsNotThere_IsError53()
        => CollectionAssert.AreEqual(new[] { "53" }, await ModuleWorkspace.RunAsync([], Program(
            "On Error Resume Next",
            "Quack 10",
            "Debug.Print Err.Number")));

    [TestMethod]
    public async Task TheDescriptionOfThatError_IsTheOneOfError53()
        => CollectionAssert.AreEqual(new[] { "File not found" }, await ModuleWorkspace.RunAsync([], Program(
            "On Error Resume Next",
            "Quack 10",
            "Debug.Print Err.Description")));
}

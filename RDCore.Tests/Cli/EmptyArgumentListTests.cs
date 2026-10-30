using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL 5.6.13.1</strong> An empty argument list of an array, which was not given one already, is the array itself: <c>f()</c> is <c>f</c>, the same
/// array, of the same declared type.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.1 Index Expressions")]
public sealed class EmptyArgumentListTests
{
    private static string Program(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim f(2) As Long, a() As Long",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        "Private Sub Fill(ByRef values() As Long)",
        "    values(0) = 9",
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow(new[] { "Debug.Print UBound(f())" }, "2", DisplayName = "the bounds of a fixed-size array")]
    [DataRow(new[] { "ReDim a(3)", "Debug.Print UBound(a())" }, "3", DisplayName = "the bounds of a dynamic array")]
    [DataRow(new[] { "f(1) = 7", "Dim v As Variant", "v = f()", "Debug.Print v(1)" }, "7", DisplayName = "assigned: a copy of the array")]
    [DataRow(new[] { "Fill f()", "Debug.Print f(0)" }, "9", DisplayName = "passed by reference: the array itself")]
    public async Task AnArrayWithAnEmptyArgumentList_IsTheArray(string[] body, string printed)
    {
        var output = await RunAsync([], Program(body));

        CollectionAssert.AreEqual(new[] { printed }, output);
    }

    // what `f` is printed as, `f()` is printed as: an array is no value Print can write (13).
    [TestMethod]
    [DataRow("f", DisplayName = "f")]
    [DataRow("f()", DisplayName = "f()")]
    [DataRow("a()", DisplayName = "a()")]
    public async Task AnArrayPrinted_WithOrWithoutTheEmptyArgumentList_IsATypeMismatch(string printed)
    {
        var output = await RunAsync([], Program("On Error Resume Next", $"Debug.Print {printed}", "Debug.Print Err.Number"));

        CollectionAssert.AreEqual(new[] { "13" }, output);
    }
}

using static RDCore.Tests.Runtime.StdLib.Information.InformationWorkspace;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §5.5.1.2.13</strong>: an object given for a <c>Variant</c> parameter of the <c>Strings</c> module is read as its
/// "simple data value", what its default member returns, as its Let-coercion reads it.
/// </summary>
/// <remarks>
/// Run through the whole platform, against the classes of <see cref="Information.InformationWorkspace"/>: one for each kind of
/// default member.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.13 Let-coercion to and from a class or Object or Nothing")]
[TestCategory("MS-VBAL 6.1.2.11 Strings")]
public sealed class StringsDefaultMemberTests
{
    // the error a statement raises, printed by a handler.
    private static Task<string[]> ErrorOf(string setup, string statement)
        => Run(string.Empty, setup, "On Error GoTo Handler", statement, "Exit Sub", "Handler:", "Debug.Print \"ERR \" & Err.Number");

    [TestMethod]
    public async Task AnObject_IsItsDefaultMembersValue()
        => CollectionAssert.AreEqual(new[] { "du", "DUCK", "1.50", "42.00", "ddd", "42-duck", "4", "2", "uc" }, await Run(string.Empty,
            "Dim s As New DfltStr, n As New DfltNum, d As New DfltDbl, c As New DfltChain",
            "Debug.Print Left(s, 2)",
            "Debug.Print UCase(s)",
            "Debug.Print Format(d, \"0.00\")",
            "Debug.Print FormatNumber(n)",
            "Debug.Print String(3, s)",
            "Debug.Print Join(Array(n, s), \"-\")",
            "Debug.Print Left(c, 1)",
            "Debug.Print InStr(s, \"u\")",
            "Debug.Print Mid(s, 2, d)"));

    [TestMethod]
    public async Task AnObjectWhoseDefaultMemberReturnsNull_IsNull()
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(string.Empty,
            "Dim o As New DfltNull",
            "Debug.Print IsNull(Left(o, 1))"));

    [TestMethod]
    [DataRow("Dim o As New Class1", "438")]
    [DataRow("Dim o As New DfltArgs", "438")]
    [DataRow("Dim o As New DfltRaise", "5")]
    [DataRow("Dim o As New DfltNothing", "91")]
    [DataRow("Dim o As New DfltObj", "438")]
    public async Task AnObjectWithNoValue_IsAnError(string setup, string error)
        => CollectionAssert.AreEqual(new[] { $"ERR {error}" }, await ErrorOf(setup, "Debug.Print Left(o, 1)"));
}

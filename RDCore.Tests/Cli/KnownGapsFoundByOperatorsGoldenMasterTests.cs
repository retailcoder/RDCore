using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the golden master of the relational, logical and concatenation operators (<c>OtherOperatorsGoldenMasterTests</c>) found that the interpreter gets
/// wrong, and is not an internal error: each is pinned here, ignored until it is fixed. The golden master records what the operators do today, these included.
/// </summary>
[TestClass]
public sealed class KnownGapsFoundByOperatorsGoldenMasterTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // MS-VBAL 5.6.9.8.1: the bits of a Boolean are negated in a Boolean, as the binary logical operators have it. Not evaluates it in an Integer.
    [TestMethod]
    [Ignore("Known gap: the Not of a Boolean is an Integer.")]
    public async Task TheNot_OfABoolean_IsABoolean()
        => CollectionAssert.AreEqual(new[] { "Boolean" }, await RunMainAsync("Debug.Print TypeName(Not True)"));

    // MS-VBAL 5.6.9.4: Null is concatenated as the zero-length string. A Null operand is, but a Variant holding Null is let-coerced to a String, which raises 94.
    [TestMethod]
    [Ignore("Known gap: a Variant holding Null concatenated to a number raises Invalid use of Null.")]
    public async Task AVariantHoldingNull_ConcatenatedToANumber_IsTheNumber()
        => CollectionAssert.AreEqual(new[] { "7" }, await RunMainAsync(
            "Dim V As Variant",
            "V = Null",
            "Debug.Print V & 7"));

    // MS-VBAL 5.6.9.8.2: Null And a number other than 0 is Null. A Null operand is, but a Variant holding Null is let-coerced to a Long, which raises 94.
    [TestMethod]
    [Ignore("Known gap: a Variant holding Null And a number raises Invalid use of Null.")]
    public async Task AVariantHoldingNull_AndANumber_IsNull()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunMainAsync(
            "Dim V As Variant",
            "V = Null",
            "Debug.Print IsNull(V And 7)"));

    // MS-VBAL 5.6.9.5: a Single and a LongLong are compared as Doubles. The table of the relational operators has no row for them, and raises 13.
    [TestMethod]
    [Ignore("Known gap: comparing a Single to a LongLong raises Type mismatch.")]
    public async Task ASingle_ComparedToALongLong_IsCompared()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunMainAsync(
            "Dim S As Single",
            "Dim L As LongLong",
            "S = 1",
            "L = 1",
            "Debug.Print S = L"));

    // MS-VBAL 5.5.1.2.7: an array of anything but Bytes let-coerced to anything but a Variant or an array is a type mismatch. No strategy takes an array to a
    // String or a number, which is an internal error - the Like of one included.
    [TestMethod]
    [Ignore("Known gap: a Variant holding an array let-coerced to a String or a number is an internal error.")]
    public async Task AVariantHoldingAnArray_LetCoercedToAScalar_IsATypeMismatch()
        => CollectionAssert.AreEqual(new[] { "13", "13", "13" }, await RunMainAsync(
            "On Error Resume Next",
            "Dim V As Variant",
            "Dim S As String",
            "Dim D As Double",
            "V = Array(1, 2)",
            "S = V",
            "Debug.Print Err.Number",
            "Err.Clear",
            "D = V",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Debug.Print V Like \"x\"",
            "Debug.Print Err.Number"));
}

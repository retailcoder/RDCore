using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the golden master of the relational, logical and concatenation operators (<c>OtherOperatorsGoldenMasterTests</c>) found that the interpreter gets
/// wrong, and is not an internal error: each is pinned here, ignored until it is fixed. The golden master records what the operators do today, these included.
/// (What it found and is fixed is tested on its own: <c>ArrayLetCoercionTests</c> and <c>MixedTypeOperationTests</c>.)
/// </summary>
[TestClass]
public sealed class KnownGapsFoundByOperatorsGoldenMasterTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

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
}

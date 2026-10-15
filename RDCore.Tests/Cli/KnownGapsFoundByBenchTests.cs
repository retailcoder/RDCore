using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the first run of <c>rdc bench</c> found that the interpreter gets wrong: each is pinned here, ignored until it is fixed, and the corpus of the bench
/// steers around it in the meantime (see the comments in <c>RDCore.CLI/Bench/Corpus</c>).
/// </summary>
[TestClass]
public sealed class KnownGapsFoundByBenchTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // MS-VBAL 5.2.3.1.3: an array dimension's bounds are Long. Indexing raises 9 once a bound is past 32767, as though the bounds were held in an Integer.
    [TestMethod]
    [Ignore("Known gap: a fixed-size array with a bound past 32767 raises Subscript out of range when it is indexed.")]
    public async Task AFixedSizeArray_WhoseBoundIsPastTheRangeOfAnInteger_IsIndexed()
        => CollectionAssert.AreEqual(new[] { "7" }, await Run(
            "Dim Values(1 To 40000) As Long",
            "Values(40000) = 7",
            "Debug.Print Values(40000)"));

    // MS-VBA: a Variant arithmetic operation whose result overflows the value type of its operands is evaluated again in the next wider type - a Long that
    // overflows is a Double, which is what lets a Variant accumulate past 2147483647.
    [TestMethod]
    [Ignore("Known gap: a Variant holding a Long that overflows raises Overflow instead of widening to a Double.")]
    public async Task AVariantLong_ThatOverflows_WidensToADouble()
        => CollectionAssert.AreEqual(new[] { "2147483648" }, await Run(
            "Dim S As Variant",
            "S = 2147483647",
            "S = S + 1",
            "Debug.Print S"));
}

using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Operations whose operands are of two different types, end to end: the table of effective types has a row for each pair VBA evaluates (MS-VBAL 5.6.9).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class MixedTypeOperationTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // a Boolean is an operand wherever an integral value is: True is -1.
    [TestMethod]
    public async Task ABoolean_AndANumber_AreAddedComparedAndCombined()
        => CollectionAssert.AreEqual(new[] { "1.5", "False", "7" }, await RunMainAsync(
            "Dim B As Variant",
            "B = True",
            "Debug.Print B + 2.5",
            "Debug.Print B = 2.5",
            "Debug.Print B And 7&"));

    // MS-VBAL 5.6.9.3.3: the difference of two dates is the number of days between them.
    [TestMethod]
    public async Task TheDifference_OfTwoDates_IsADouble()
        => CollectionAssert.AreEqual(new[] { "Double", "31" }, await RunMainAsync(
            "Dim A As Date",
            "Dim B As Date",
            "A = #1/1/2000#",
            "B = #2/1/2000#",
            "Debug.Print TypeName(B - A)",
            "Debug.Print B - A"));

    // MS-VBAL 5.6.9.5: a Single and a LongLong are compared as Doubles.
    [TestMethod]
    public async Task ASingle_ComparedToALongLong_IsCompared()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunMainAsync(
            "Dim S As Single",
            "Dim L As LongLong",
            "S = 1",
            "L = 1",
            "Debug.Print S = L"));
}

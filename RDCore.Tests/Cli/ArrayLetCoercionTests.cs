using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.5.1.2.6-7</strong>, end to end: an array is let-coerced to an array or to a Variant, and a <c>Byte()</c> to a <c>String</c> as well;
/// to any other type it is run-time error 13 (<em>Type mismatch</em>). Declared as an array, the static semantics reject it; a Variant brings it to run time.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.7 Let-coercion to and from non-Byte arrays")]
public sealed class ArrayLetCoercionTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // the destination's rule has no row for an array, which used to be an internal error rather than the type mismatch it is - the Like of one included.
    [TestMethod]
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

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.6 Let-coercion to and from Byte()")]
    public async Task AVariantHoldingAByteArray_LetCoercedToAString_IsTheStringItIsTheDataOf()
        => CollectionAssert.AreEqual(new[] { "AB" }, await RunMainAsync(
            "Dim B() As Byte",
            "Dim V As Variant",
            "Dim S As String",
            "B = \"AB\"",
            "V = B",
            "S = V",
            "Debug.Print S"));
}

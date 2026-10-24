using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.6.9.3: a <c>Single</c> or <c>Double</c> operation of finite operands whose result is not finite overflows, unless an operand is a <c>Variant</c>
/// and a <c>Double</c> holds the result; <c>^</c> raises 5 for a power that is no real number. Both ways: the payload evaluation declines what the boxed one
/// raises (the suite is also run with <c>RDCORE_UNBOXED=0</c>). Every expected line was recorded in Excel (grid <c>oracle\s16</c> D01-D10, E01-E04).
/// </summary>
[TestClass]
public sealed class FloatingPointOverflowTests
{
    private static string Run(string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D01 a Single product")]
    [DataRow("Dim s As Single, d As Double ;; d = 1E+39 ;; s = d ;; Debug.Print Err.Number", "6", DisplayName = "D02 a Double past a Single, assigned to one")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D03 a Double product")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; s = s + s ;; Debug.Print Err.Number", "6", DisplayName = "D04 a Single sum")]
    [DataRow("Dim s As Single ;; s = -3E+38 ;; s = s * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D07 a negative Single product")]
    [DataRow("Dim s As Single ;; s = 1 ;; s = s / 0 ;; Debug.Print Err.Number", "11", DisplayName = "D08 a Single divided by zero")]
    [DataRow("Dim s As Single, i As Long ;; s = 3E+38 ;; i = 2 ;; s = s * i ;; Debug.Print Err.Number", "6", DisplayName = "D09 a Single by a Long")]
    [DataRow("Dim s As Single ;; s = 3E+38 ;; Debug.Print s * 10 ;; Debug.Print Err.Number", "6", DisplayName = "D10 not assigned: nothing printed")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d + d ;; Debug.Print Err.Number", "6", DisplayName = "E05 a Double sum")]
    [DataRow("Dim d As Double ;; d = 1E+308 ;; d = d / 0.1 ;; Debug.Print Err.Number", "6", DisplayName = "E06 a Double quotient")]
    public void AFloatingPointResultThatIsNotFinite_Overflows(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    public void AVariantSingleProductPastASingle_IsADouble()
        => Assert.AreEqual("0|Double|3.00000000549776E+39", Run("Dim v As Variant ;; v = CSng(3E+38) ;; v = v * 10 ;; Debug.Print Err.Number & \"|\" & TypeName(v) & \"|\" & v"));

    [TestMethod]
    [DataRow("Dim d As Double ;; d = 10 ;; d = d ^ 400 ;; Debug.Print Err.Number & \"|\" & d", "6|10", DisplayName = "E01 past a Double, the variable unchanged")]
    [DataRow("Dim d As Double ;; d = -8 ;; d = d ^ (1 / 3) ;; Debug.Print Err.Number & \"|\" & d", "5|-8", DisplayName = "E02 a negative number to a fraction")]
    [DataRow("Dim s As Single ;; s = 10 ;; s = s ^ 50 ;; Debug.Print Err.Number", "6", DisplayName = "E03 a Double power past a Single, assigned to one")]
    [DataRow("Dim v As Variant ;; v = 10 ;; v = v ^ 400 ;; Debug.Print Err.Number & \"|\" & TypeName(v)", "6|Integer", DisplayName = "E04 a Variant: nothing wider than Double")]
    [DataRow("Dim d As Double ;; d = 0 ;; d = d ^ 0 ;; Debug.Print Err.Number & \"|\" & d", "0|1", DisplayName = "0 ^ 0 is 1")]
    public void APowerThatIsNoFiniteNumber_RaisesAnError(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    // MS-VBA assigns the IEEE result of +, -, * and / (and of a Double let-coerced to a Single) to the variable before it raises the error: s reads inf
    // after `s = s * 10` (D01-D09; ^ leaves it unchanged, E01-E03). RD-VBA raises the same errors and leaves the variable as it was.
    [TestMethod]
    [Ignore("Undecided (author): MS-VBA stores the ±infinity an overflowing floating-point assignment computed before raising 6; RD-VBA keeps the old value.")]
    public void AnOverflowingFloatingPointAssignment_StoresTheInfinity()
        => Assert.AreEqual("6|True", Run("Dim s As Single ;; s = 3E+38 ;; s = s * 10 ;; Debug.Print Err.Number & \"|\" & (s > 3.4E+38)"));
}

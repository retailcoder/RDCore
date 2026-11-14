using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.6.9.3: MS-VBA computes an expression of Single, Double and Date operations of typed operands in the x87's 80-bit extended format, and rounds
/// it where it is stored (ExtendedIntermediates) - in range and in precision. Both ways: on payloads, and boxed (the suite is also run with
/// <c>RDCORE_UNBOXED=0</c>). Every expected line was recorded in Excel (grid <c>oracle\s42</c>: cases A, B, C, D and E, by their id).
/// </summary>
[TestClass]
public sealed class ExtendedIntermediateTests
{
    // the program of a case: its declarations, P - which writes what it is given, as a Variant - and Bits, which writes the 8 bytes of a Double in hex.
    private static async Task<string> Run(string declarations, string body)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private Type D8", "    V As Double", "End Type",
            "Private Type L8", "    Lo As Long", "    Hi As Long", "End Type",
            "Private acc As String",
            .. (declarations.Length == 0 ? [] : declarations.Split(" ;; ")),
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Private Function Bits(ByVal x As Double) As String",
            "    Dim a As D8, b As L8",
            "    a.V = x",
            "    LSet b = a",
            "    Bits = Right$(\"0000000\" & Hex$(b.Hi), 8) & Right$(\"0000000\" & Hex$(b.Lo), 8)",
            "End Function",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", await RunAsync([], string.Join("\r\n", lines) + "\r\n"));
    }
    // An intermediate past the range of a Double overflows only where it is stored as one; one past the extended range overflows where it is computed; a division by zero is error 11, before an overflow.
    [TestMethod]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 10 / 10 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) - (d * 10) ;; P Err.Number ;; P e", "0|0|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 10 * 10 / 100 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 3E+38 ;; t = s * 10 / 10 ;; P Err.Number ;; P t", "0|3E+38|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = s * 10 ;; P Err.Number ;; P e", "0|3.00000000549776E+39|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 3E+38 ;; l = s * 10 / 1E+35 ;; P Err.Number ;; P l", "0|30000|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E-300 ;; e = d * d * 1E+300 ;; P Err.Number ;; P e", "0|1E-300|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E-308 ;; e = d / 1E+10 * 1E+10 ;; P Err.Number ;; P e", "0|1E-308|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * d / d / d ;; P Err.Number ;; P e", "0|1|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 * 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 / 1E+308 ;; P Err.Number ;; P e", "0|1|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 0 ;; e = (1 / d) - (1 / d) ;; P Err.Number ;; P e", "11|-nan(ind)|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) / (d * 10) ;; P Err.Number ;; P e", "0|1|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d * 10 ;; P Err.Number ;; P e", "6|inf|")]
    [DataRow("", "Dim d As Double, i As Integer ;; d = 1E+308 ;; i = 5 ;; i = d * 10 / 1E+308 ;; P Err.Number ;; P i", "0|10|")]
    [DataRow("", "Dim d As Double, z As Double, e As Double ;; d = 1E+308 ;; z = 0 ;; e = CDbl(d * 10) + 1 / z ;; P Err.Number ;; P e", "11|inf|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E-300 ;; e = d * 1E-10 ;; P Err.Number ;; P Bits(e)", "0|000012688B70E62B|")]
    public async Task AnIntermediatePastADouble_OverflowsOnlyWhereItIsStored(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // Abs, Int, Fix and Sgn compute on the extended value; CDbl rounds it to a Double, CSng to a Double and then to 24 bits, in the range of a Double; Sqr, Log and Atn are given it stored, and raise at once.
    [TestMethod]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Abs(d * 10) / 10 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Sgn(d * 10) ;; P Err.Number ;; P e", "0|1|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Int(d * 10 / 10) ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Fix(d * 10) / 10 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+200 ;; e = Sqr(d * d) ;; P Err.Number ;; P e", "6|0|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = CDbl(d * 10) / 10 ;; P Err.Number ;; P e", "6|inf|")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 3E+38 ;; t = CSng(s * 10) / 10 ;; P Err.Number ;; P t", "0|3E+38|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = CDbl(d + t) - d ;; P e", "0|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = CSng(s + 1) - s ;; P e", "0|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = CSng(d * 10) ;; P Err.Number ;; P e", "6|inf|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = CSng(s * 10) ;; P Err.Number ;; P e", "0|3.00000008662739E+39|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = CDbl(s * 10) ;; P Err.Number ;; P e", "0|3.00000000549776E+39|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 3 ;; t = 1 / 2 ^ 60 ;; e = Int(d - t) ;; P e", "2|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = Abs(d + t) - d ;; P e", "8.67361737988404E-19|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 3 ;; t = 1 / 2 ^ 60 ;; e = Fix(d - t) ;; P e", "2|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Log(d * 10) ;; P Err.Number ;; P e", "6|0|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = Int(d * 10) / 10 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("", "Dim z As Double, e As Double ;; z = 0 ;; e = 5 ;; e = Abs(z / z) ;; P Err.Number ;; P e", "6|nan|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = Sgn(d + t - d) ;; P e", "1|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = Int(s * 10) ;; P Err.Number ;; P e", "0|3.00000000549776E+39|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = Abs(s * 10) ;; P Err.Number ;; P e", "0|3.00000000549776E+39|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = Fix(s + 1) ;; P l", "16777217|")]
    public async Task TheFunctionsOfANumber_ComputeOnTheExtendedValue_OrAreGivenItStored(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // Each operation is rounded to 64 bits - a Single's too - and the value again where it is stored: twice.
    [TestMethod]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; e = (d + t) - d ;; P e", "8.67361737988404E-19|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 53 ;; e = d + t - d ;; P e", "1.11022302462516E-16|")]
    [DataRow("", "Dim d As Double, t As Double, e As Double ;; d = 1 ;; t = 1 / 2 ^ 53 ;; e = d + t + t - d ;; P e", "2.22044604925031E-16|")]
    [DataRow("", "Dim a As Double, b As Double, c As Double, e As Double ;; a = 0.1 ;; b = 0.2 ;; c = 0.3 ;; e = a + b - c ;; P e", "2.77555756156289E-17|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 0.1 ;; e = s * 3 ;; P e", "0.300000004470348|")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 16777216 ;; t = s + 1 - s ;; P t", "1|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = s + 1 ;; P e", "16777217|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 3 ;; e = 1 / d * d - 1 ;; P e", "0|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 0.1 ;; l = s * 3 * 1000000000 ;; P l", "300000004|")]
    [DataRow("", "Dim s As Single, i As Integer, e As Double ;; s = 0.1 ;; i = 3 ;; e = s * i ;; P e", "0.300000004470348|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 1 ;; e = s / 3 ;; P e", "0.333333333333333|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 0.1 ;; e = s * s ;; P e", "1.00000002980232E-02|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 3E+38 ;; e = s + s ;; P Err.Number ;; P e", "0|6.00000001099551E+38|")]
    [DataRow("", "Dim d As Double, b As Double, e As Double ;; d = 1 ;; b = 1 / 2 ^ 53 + 1 / 2 ^ 78 ;; e = d + b ;; P Bits(e) ;; P Bits(b)", "3FF0000000000000|3CA0000008000000|")]
    [DataRow("", "Dim c As Currency, d As Double, e As Double ;; c = 1.1 ;; d = 3 ;; e = c * d ;; P Bits(e)", "400A666666666666|")]
    [DataRow("", "Dim c As Currency, e As Double ;; c = 0.3 ;; e = c / 3 ;; P Bits(e)", "3FB999999999999A|")]
    [DataRow("", "Dim e As Double ;; e = 0.1 + 0.2 - 0.3 ;; P e", "2.77555756156289E-17|")]
    [DataRow("", "Dim dt As Date, e As Double ;; dt = 0.1 ;; e = dt + 0.2 - 0.3 ;; P e", "2.77555756156289E-17|")]
    [DataRow("", "Dim s As Single, t As Single ;; s = 0.1 ;; t = s * 3 ;; P Bits(t)", "3FD3333340000000|")]
    [DataRow("", "Dim l As Long, d As Double, e As Double ;; l = 3 ;; d = 0.1 ;; e = l * d - 0.3 ;; P e", "2.77555756156289E-17|")]
    [DataRow("", "Dim a As Double, b As Double, e As Double ;; a = 1E+16 ;; b = 1 ;; e = (a + b) - a ;; P e", "1|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 0.1 ;; e = s + 0.5 ;; P e", "0.600000001490116|")]
    public async Task EachOperation_IsRoundedToSixtyFourBits_AndTheValueAgainWhereItIsStored(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // Let-coerced to a numeric type - a variable, an element, a field, a For bound, a subscript, a ReDim bound, an operand of \, Mod, And, Or, Not - the value is converted from its 80 bits; written as a String, made a Decimal or held by a Variant, it is stored as its own type first.
    [TestMethod]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = d + t ;; P l", "3|")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = CLng(d + t) ;; P l", "3|")]
    [DataRow("", "Dim d As Double, t As String ;; d = 1E+308 ;; t = \"\" & (d * 10 / 10) ;; P Err.Number ;; P t", "0|1E+308|")]
    [DataRow("", "Dim z As Double, b As Boolean ;; z = 0 ;; b = z / z ;; P Err.Number ;; P b", "6|False|")]
    [DataRow("", "Dim z As Double, b As Boolean ;; z = 0 ;; b = 1 / z ;; P Err.Number ;; P b", "11|False|")]
    [DataRow("", "Dim d As Double, c As Currency ;; d = 1E+308 ;; c = d * 10 / 1E+308 ;; P Err.Number ;; P c", "0|10|")]
    [DataRow("", "Dim d As Double, t As Double, c As Currency ;; d = 0.00005 ;; t = 1 / 2 ^ 70 ;; c = d + t ;; P c", "0.0001|")]
    [DataRow("", "Dim d As Double, t As String ;; d = 1E+308 ;; t = d * 10 / 10 ;; P Err.Number ;; P t", "0|1E+308|")]
    [DataRow("", "Dim d As Double, t As String ;; d = 1E+308 ;; t = \"x\" ;; t = d * 10 ;; P Err.Number ;; P t", "6|x|")]
    [DataRow("", "Dim d As Double, b As Boolean ;; d = 1E+308 ;; b = d * 10 ;; P Err.Number ;; P b", "0|True|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = CLng(s + 1) ;; P l", "16777217|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = s + 1 ;; P l", "16777217|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; l = (s + 1) Mod 16777218 ;; P l", "16777217|")]
    [DataRow("", "Dim s As Single, c As Currency ;; s = 16777216 ;; c = s + 1 ;; P c", "16777217|")]
    [DataRow("", "Dim s As Single, t As String ;; s = 16777216 ;; t = s + 1 ;; P t", "1.677722E+07|")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; P CStr(s + 1)", "1.677722E+07|")]
    [DataRow("", "Dim s As Single, b As Boolean ;; s = 3E+38 ;; b = s * 10 ;; P Err.Number ;; P b", "0|True|")]
    [DataRow("", "Dim s As Single, i As Integer ;; s = 3E+38 ;; i = 3 ;; i = s * 10 / 1E+35 ;; P Err.Number ;; P i", "0|30000|")]
    [DataRow("", "Dim s As Single, v As Variant ;; s = 3E+38 ;; v = s * 10 ;; P Err.Number ;; P TypeName(v)", "6|Empty|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = s + 1 + CDbl(0) ;; P e", "16777217|")]
    [DataRow("", "Dim s As Single, a(0 To 1) As Double ;; s = 16777216 ;; a(0) = s + 1 ;; P a(0)", "16777217|")]
    [DataRow("Private Type R ;; X As Double ;; End Type", "Dim s As Single, r1 As R ;; s = 16777216 ;; r1.X = s + 1 ;; P r1.X", "16777217|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; For l = 1 To s + 1 ;; Next ;; P l", "16777218|")]
    [DataRow("", "Dim d As Double, n As Long ;; d = 1E+308 ;; For n = 1 To d * 10 / 1E+308 ;; Next ;; P Err.Number ;; P n", "0|11|")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = Not (d + t) ;; P l", "-4|")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; P CBool(d * 10) ;; P Err.Number", "True|0|")]
    [DataRow("", "Dim a(0 To 3) As Long, d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; a(d + t) = 7 ;; P a(2) ;; P a(3)", "0|7|")]
    [DataRow("", "Dim a() As Long, d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; ReDim a(d + t) ;; P UBound(a)", "3|")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; P Mid$(\"abcdef\", d + t)", "cdef|")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = (d + t) Or 0 ;; P l", "3|")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; P Left$(\"abcdef\", d + t)", "abc|")]
    public async Task LetCoercedToANumericType_TheValueIsConvertedFromItsEightyBits(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // A parameter of a numeric type, ByVal or ByRef, is given the value converted to its type; a Variant parameter, stored as the expression's.
    [TestMethod]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; P d * 10 / 10 ;; P Err.Number", "1E+308|0|")]
    [DataRow("Private Function F(ByVal x As Double) As Double ;; F = x * 10 / 10 ;; End Function", "P F(1E+308) ;; P Err.Number", "1E+308|0|")]
    [DataRow("Private Function F(ByVal x As Double) As Double ;; F = x * 10 ;; End Function", "P F(1E+308) ;; P Err.Number", "6|")]
    [DataRow("Private Function G(ByVal x As Double) As Double ;; G = x - 1 ;; End Function", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P G(d + t)", "0|")]
    [DataRow("Private Function H(x As Double) As Double ;; H = x - 1 ;; End Function", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P H(d + t)", "0|")]
    [DataRow("Private Function V1(ByVal x As Variant) As Double ;; V1 = x - 1 ;; End Function", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P V1(d + t)", "0|")]
    [DataRow("Private Function G(ByVal x As Double) As Double ;; G = x ;; End Function", "Dim s As Single ;; s = 3E+38 ;; P G(s * 10) ;; P Err.Number", "3.00000000549776E+39|0|")]
    [DataRow("Private Function G(ByVal x As Double) As Double ;; G = x ;; End Function", "Dim s As Single ;; s = 16777216 ;; P G(s + 1)", "16777217|")]
    [DataRow("", "Dim s As Single ;; s = 3E+38 ;; P s * 10 ;; P Err.Number", "6|")]
    [DataRow("Private Function F2(ByVal s As Single) As Double ;; F2 = s * 10 ;; End Function", "P F2(3E+38) ;; P Err.Number", "3.00000000549776E+39|0|")]
    [DataRow("Private Sub Q(ByRef x As Double) ;; P x ;; End Sub", "Dim s As Single ;; s = 16777216 ;; Q s + 1", "16777217|")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; P Round(d + t)", "2|")]
    public async Task AnArgument_IsConvertedToItsParametersType(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // A comparison rounds each operand to the precision of its effective type, in the extended range, and raises what is pending; a Select Case selector is stored.
    [TestMethod]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; Select Case d * 10 ;; Case Is > 0 ;; P \"gt\" ;; End Select ;; P Err.Number", "6|")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; If d * 10 = d * 10 Then P \"eq\" ;; P Err.Number", "eq|0|")]
    [DataRow("", "Dim s As Single ;; s = 3E+38 ;; If s * 10 > s Then P \"gt\" ;; P Err.Number", "gt|0|")]
    [DataRow("", "Dim s As Single ;; s = 0.1 ;; If s * 3 = CSng(0.3) Then P \"eq\" Else P \"ne\"", "eq|")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; If s + 1 > s Then P \"gt\" Else P \"ng\"", "ng|")]
    [DataRow("", "Dim d As Double ;; d = 1 ;; If d + 1 / 2 ^ 60 > d Then P \"gt\" Else P \"ng\"", "ng|")]
    [DataRow("", "Dim z As Double ;; z = 0 ;; If 1 / z > 0 Then P \"gt\" ;; P Err.Number", "gt|11|")]
    [DataRow("", "Dim z As Double ;; z = 0 ;; If z / z = z / z Then P \"eq\" Else P \"ne\" ;; P Err.Number", "eq|6|")]
    [DataRow("", "Dim l As Long, d As Double ;; l = 1 ;; d = 1 ;; If l + 1 / 2 ^ 60 > d Then P \"gt\" Else P \"ng\"", "ng|")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; Select Case d + t ;; Case Is > d ;; P \"gt\" ;; Case Else ;; P \"ng\" ;; End Select", "ng|")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; If CDbl(d * 10) > 0 Then P \"gt\" ;; P Err.Number", "gt|6|")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; Select Case s + 1 ;; Case 16777217 ;; P \"is\" ;; Case Else ;; P \"not\" ;; End Select", "not|")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P IIf(d + t > d, \"gt\", \"ng\")", "ng|")]
    [DataRow("", "Dim d As Double, t As Double, b As Boolean ;; d = 1 ;; t = 1 / 2 ^ 60 ;; b = d + t > d ;; P b", "False|")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; If d * 10 Then P \"t\" ;; P Err.Number", "t|0|")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; If s + 1 = 16777217 Then P \"eq\" Else P \"ne\"", "eq|")]
    [DataRow("", "Dim s As Single, v As Variant ;; s = 16777216 ;; v = 16777217 ;; If s + 1 = v Then P \"eq\" Else P \"ne\"", "ne|")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; Select Case 5 ;; Case Is < d * 10 ;; P \"lt\" ;; End Select ;; P Err.Number", "lt|6|")]
    [DataRow("", "Dim d As Double, n As Long ;; d = 1E+308 ;; Do While d * 10 > 0 And n < 1 ;; n = n + 1 ;; Loop ;; P n ;; P Err.Number", "1|0|")]
    [DataRow("", "Dim s As Single, l As Long ;; s = 16777216 ;; If s + 1 > 16777216 Then P \"gt\" Else P \"ng\"", "gt|")]
    [DataRow("", "Dim d As Double, t As Double ;; d = 1 ;; t = 1 / 2 ^ 60 ;; P (d + t > d) And True", "False|")]
    [DataRow("", "Dim d As Double ;; d = 1E+308 ;; P IIf(d * 10 > 0, \"gt\", \"ng\") ;; P Err.Number", "gt|0|")]
    [DataRow("", "Dim s As Single, i As Integer ;; s = 16777216 ;; i = 1 ;; If s + i = 16777217 Then P \"eq\" Else P \"ne\"", "eq|")]
    public async Task AComparison_RoundsToThePrecisionOfItsType_InTheExtendedRange(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // An operation with a Variant operand is the Variant arithmetic, which rounds each result to its type: a typed operand is stored as its type for it.
    [TestMethod]
    [DataRow("", "Dim d As Double, v As Variant, e As Double ;; d = 1E+308 ;; v = 10 ;; e = d * v / v ;; P Err.Number ;; P e", "6|0|")]
    [DataRow("", "Dim v As Variant, e As Double ;; v = CDbl(1E+308) ;; e = v * 10 / 10 ;; P Err.Number ;; P e", "6|0|")]
    [DataRow("", "Dim s As Single, v As Variant ;; s = 16777216 ;; v = s + 1 ;; P TypeName(v) ;; P v - s", "Single|0|")]
    [DataRow("", "Dim v As Variant, e As Double ;; v = 0.1 ;; e = v + 0.2 - 0.3 ;; P e", "5.55111512312578E-17|")]
    [DataRow("", "Dim v As Variant, a As Double, e As Double ;; v = 0.2 ;; a = 0.1 ;; e = a + v - 0.3 ;; P e", "5.55111512312578E-17|")]
    [DataRow("", "Dim v As Variant, e As Double ;; v = CSng(0.1) ;; e = v * 3 ;; P e", "0.300000011920929|")]
    [DataRow("", "Dim v As Variant, w As Variant ;; v = CSng(16777216) ;; w = v + 1 ;; P TypeName(w) ;; P w - v", "Single|0|")]
    [DataRow("", "Dim d As Double, t As Double, v As Variant ;; d = 1 ;; t = 1 / 2 ^ 60 ;; v = d + t ;; P v - d", "0|")]
    [DataRow("", "Dim v As Variant, s As Single, e As Double ;; v = 1 ;; s = 16777216 ;; e = (s + 1) + v ;; P e", "16777216|")]
    [DataRow("", "Dim s As Single, v As Variant, e As Double ;; s = 3E+38 ;; v = 0 ;; e = s * 10 + v ;; P Err.Number ;; P e", "6|0|")]
    public async Task AVariantOperation_RoundsEachResult(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // The operands of ^ are converted to Doubles from their 80 bits, and raise at once; its result is a Double, which the expression goes on with.
    [TestMethod]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) ^ 1 ;; P Err.Number ;; P e", "6|0|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = d ^ 2 / d ;; P Err.Number ;; P e", "6|0|")]
    [DataRow("", "Dim d As Double, l As Long ;; d = 1E+308 ;; l = (d * 10 / 1E+308) Mod 3 ;; P Err.Number ;; P l", "0|1|")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = (d + t) \\ 1 ;; P l", "3|")]
    [DataRow("", "Dim d As Double, t As Double, l As Long ;; d = 2.5 ;; t = 1 / 2 ^ 60 ;; l = (d + t) And 7 ;; P l", "3|")]
    [DataRow("", "Dim s As Single, e As Double ;; s = 16777216 ;; e = (s + 1) ^ 1 ;; P e", "16777217|")]
    [DataRow("", "Dim s As Single ;; s = 16777216 ;; P TypeName(s + 1 + 0.5)", "Double|")]
    [DataRow("", "Dim d As Double, e As Double, i As Integer ;; d = 1E+308 ;; i = 2 ;; e = d * 10 / 10 ^ i ;; P Err.Number ;; P e", "0|1E+307|")]
    [DataRow("", "Dim d As Double, e As Double ;; d = 1E+308 ;; e = (d * 10) / (5 + 5) ;; P Err.Number ;; P e", "0|1E+308|")]
    public async Task TheOperandsOfAnotherOperator_AreConvertedFromTheirEightyBits(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    // A constant expression - of a Const, or of literals - is computed in the extended format too.
    [TestMethod]
    [DataRow("", "Dim e As Double ;; e = 1E+308 * 10 / 10 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("Const K As Double = 1E+308", "Dim e As Double ;; e = K * 10 / 10 ;; P Err.Number ;; P e", "0|1E+308|")]
    [DataRow("Const K = 1E+308 * 10 / 10", "P K ;; P Err.Number", "1E+308|0|")]
    [DataRow("Const K = 0.1 + 0.2 - 0.3", "P K", "2.77555756156289E-17|")]

    public async Task AConstantExpression_IsComputedInTheExtendedFormat(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));
}

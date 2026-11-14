using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBA makes a <c>Single</c> a <c>Decimal</c> as its value to 7 significant digits, ties to even - <c>CDec</c>, and a <c>Variant</c> operation of a
/// <c>Decimal</c> and a <c>Single</c> alike - where a <c>Double</c> keeps 15 digits and a <c>Currency</c> the exact value. Every expected line was recorded
/// in Excel (grid <c>oracle\s43</c> K01-K26, J01).
/// </summary>
[TestClass]
public sealed class SingleToDecimalTests
{
    private static async Task<string> Run(string body)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private acc As String",
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", await RunAsync([], string.Join("\r\n", lines) + "\r\n"));
    }

    [TestMethod]
    [DataRow("P CDec(CSng(16777217))", "16777220|")]
    [DataRow("P CDec(CSng(0.1))", "0.1|")]
    [DataRow("Dim s As Single ;; s = 1 ;; s = s / 3 ;; P CDec(s)", "0.3333333|")]
    [DataRow("P CDec(CSng(123456.789))", "123456.8|")]
    [DataRow("P CDec(CSng(3E+38)) ;; P Err.Number", "6|")]
    [DataRow("P CDec(CSng(1E-20)) ;; P Err.Number", "0.00000000000000000001|0|")]
    [DataRow("P CDec(CSng(-2.5))", "-2.5|")]
    [DataRow("P CDec(CSng(9999999))", "9999999|")]
    [DataRow("P CDec(CSng(99999999))", "100000000|")]
    [DataRow("Dim v As Variant ;; v = CSng(0.1) ;; P CDec(v)", "0.1|")]
    [DataRow("Dim s As Single ;; s = 0.1 ;; P CDec(0) + s ;; P TypeName(CDec(0) + s)", "0.1|Decimal|")]
    [DataRow("P CCur(CSng(0.1))", "0.1|")]
    [DataRow("P CCur(CSng(16777217))", "16777216|")]
    [DataRow("Dim s As Single ;; s = 0.1 ;; P CDec(s) * 3", "0.3|")]
    [DataRow("P CDec(CSng(1.5E-07))", "0.00000015|")]
    [DataRow("P CDec(CSng(12345678))", "12345680|")]
    [DataRow("Dim d As Double ;; d = 0.1 ;; P CDec(d) ;; P CDec(1 / 3) ;; P CDec(16777217.123456789)", "0.1|0.333333333333333|16777217.1234568|")]
    [DataRow("P CDec(CSng(0.000001234567))", "0.000001234567|")]
    [DataRow("P CDec(CDbl(CSng(0.1)))", "0.100000001490116|")]
    [DataRow("Dim d As Double ;; d = 3E+20 ;; P CDec(d)", "300000000000000000000|")]
    [DataRow("P CDec(CSng(0.5)) ;; P CDec(CSng(1E+28)) ;; P Err.Number", "0.5|9999999000000000000000000000|0|")]
    [DataRow("P CDec(CSng(2.5E-28)) ;; P CDec(CSng(1E-28))", "0.0000000000000000000000000003|0.0000000000000000000000000001|")]
    [DataRow("Dim s As Single ;; s = 16777217 ;; P CDec(s) ;; P CStr(s) ;; P Str(s)", "16777220|1.677722E+07| 1.677722E+07|")]
    [DataRow("Dim s As Single, v As Variant ;; s = 0.1 ;; v = CDec(1) ;; v = v * s ;; P v ;; P TypeName(v)", "0.1|Decimal|")]
    [DataRow("Dim s As Single, c As Currency ;; s = 0.1 ;; c = s ;; P c", "0.1|")]
    [DataRow("Dim s As Single ;; s = 1.25 ;; P CDec(s) ;; s = 1234.5678 ;; P CDec(s)", "1.25|1234.568|")]
    [DataRow("P CDec(CSng(12345665)) ;; P CDec(CSng(12345675)) ;; P CDec(CSng(-12345665)) ;; P CDec(CSng(12345645)) ;; P CDec(CSng(1234566.5)) ;; P CDec(CSng(1234567.5)) ;; P CDec(CSng(16777215)) ;; P CDec(CSng(0.0001234565))", "12345660|12345680|-12345660|12345640|1234566|1234568|16777220|0.0001234565|")]
    public async Task ASingleMadeADecimal_IsItsSevenSignificantDigits(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));
}
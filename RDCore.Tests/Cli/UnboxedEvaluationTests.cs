using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A scalar variable assigned an expression of them, and a condition of them, are evaluated on payloads (IR lowering S1.2): the same values, and the same
/// errors, as the boxed evaluation - which the suite is also run with (<c>RDCORE_UNBOXED=0</c>), for these to hold both ways.
/// </summary>
[TestClass]
public sealed class UnboxedEvaluationTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Dim l As Long, i As Integer ;; i = 3 ;; l = 2147483647 ;; l = l - i ;; Debug.Print l", "2147483644", DisplayName = "a Long less an Integer")]
    [DataRow("Dim d As Double, l As Long ;; d = 0.5 ;; l = 2 ;; d = d + l ;; Debug.Print d", "2.5", DisplayName = "a Double plus a Long")]
    [DataRow("Dim d As Double ;; d = 7 ;; d = d / 2 ;; Debug.Print d", "3.5", DisplayName = "a Double divided")]
    [DataRow("Dim s As Single ;; s = 0.5 ;; s = s * 3 ;; Debug.Print s", "1.5", DisplayName = "a Single multiplied")]
    [DataRow("Dim i As Integer ;; i = 5 ;; i = -i ;; Debug.Print i", "-5", DisplayName = "an Integer negated")]
    [DataRow("Dim ll As LongLong ;; ll = 4000000000^ ;; ll = ll * 2 ;; Debug.Print ll", "8000000000", DisplayName = "a LongLong multiplied")]
    [DataRow("Dim i As Integer, l As Long ;; l = 300 ;; i = l + 1 ;; Debug.Print i", "301", DisplayName = "a Long in the range of an Integer, assigned to one")]
    [DataRow("Dim b As Byte ;; b = 200 ;; b = b + 50 ;; Debug.Print b", "250", DisplayName = "a Byte that fits")]
    [DataRow("Dim l As Long ;; l = -17 ;; l = l Mod 5 ;; Debug.Print l", "-2", DisplayName = "a Long Mod, with the dividend's sign")]
    [DataRow("Dim l As Long, i As Long ;; l = 10 ;; i = 3 ;; l = (l + i) * 2 ;; Debug.Print l", "26", DisplayName = "an expression in parentheses")]
    [DataRow("Dim l As Long ;; l = -17 ;; l = l \\ 5 ;; Debug.Print l", "-3", DisplayName = "a Long integer-divided, truncated")]
    [DataRow("Dim l As Long, d As Double ;; d = 7.5 ;; l = d \\ 2 ;; Debug.Print l", "4", DisplayName = "a Double integer-divided, rounded first (boxed)")]
    public void WhatIsAssignedOnPayloads_IsWhatTheBoxedEvaluationAssigns(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // the errors are the boxed evaluation's: the payload evaluation declines what would raise one, and the expression is evaluated again, boxed.
    [TestMethod]
    [DataRow("Dim l As Long ;; l = 2147483647 ;; l = l + 1 ;; Debug.Print Err.Number & \" \" & l", "6 2147483647", DisplayName = "a Long past its range")]
    [DataRow("Dim i As Integer, l As Long ;; l = 40000 ;; i = l ;; Debug.Print Err.Number & \" \" & i", "6 0", DisplayName = "a Long past the range of an Integer, assigned to one")]
    [DataRow("Dim i As Integer ;; i = -32768 ;; i = -i ;; Debug.Print Err.Number & \" \" & i", "6 -32768", DisplayName = "the least Integer negated")]
    [DataRow("Dim b As Byte ;; b = 200 ;; b = b + 100 ;; Debug.Print Err.Number & \" \" & b", "6 200", DisplayName = "a Byte past its range")]
    [DataRow("Dim d As Double ;; d = 1 ;; d = d / 0 ;; Debug.Print Err.Number & \" \" & d", "11 1", DisplayName = "a Double divided by zero")]
    [DataRow("Dim l As Long ;; l = 7 ;; l = l Mod 0 ;; Debug.Print Err.Number & \" \" & l", "11 7", DisplayName = "a Long Mod zero")]
    [DataRow("Dim l As Long ;; l = 7 ;; l = l \\ 0 ;; Debug.Print Err.Number & \" \" & l", "11 7", DisplayName = "a Long integer-divided by zero")]
    [DataRow("Dim ll As LongLong ;; ll = 9223372036854775807^ ;; ll = ll + 1 ;; Debug.Print Err.Number & \" \" & ll", "6 9223372036854775807", DisplayName = "a LongLong past its range")]
    public void WhatWouldRaiseAnError_RaisesTheBoxedEvaluationsError(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // MS-VBAL §5.4.2.3, stepped on payloads: the counter after the loop is the first value past the end.
    [TestMethod]
    [DataRow("Dim i As Long, n As Long ;; For i = 1 To 10 ;; n = n + i ;; Next ;; Debug.Print n & \" \" & i", "55 11", DisplayName = "a Long counter")]
    [DataRow("Dim i As Integer, n As Long ;; For i = 10 To 1 Step -3 ;; n = n + i ;; Next ;; Debug.Print n & \" \" & i", "22 -2", DisplayName = "an Integer counter, stepping down")]
    [DataRow("Dim d As Double, n As Long ;; For d = 0 To 2 Step 0.5 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & d", "5 2.5", DisplayName = "a Double counter, a fractional step")]
    [DataRow("Dim i As Long, n As Long ;; For i = 1 To 10 ;; i = i + 1 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & i", "5 11", DisplayName = "a counter its body assigns")]
    [DataRow("Dim i As Integer ;; For i = 32766 To 32767 ;; Next ;; Debug.Print Err.Number & \" \" & i", "6 32767", DisplayName = "an Integer counter stepped past its range")]
    public void AForLoop_SteppedOnPayloads_IsWhatTheBoxedEvaluationSteps(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // MS-VBAL §5.6.9.5 and §5.6.9.8 on payloads, a Boolean being -1 or 0 (recorded in Excel).
    [TestMethod]
    [DataRow("Dim b As Boolean, l As Long ;; l = 3 ;; b = l > 2 ;; Debug.Print b", "True", DisplayName = "a Long compared")]
    [DataRow("Dim b As Boolean, i As Integer, d As Double ;; i = 2 ;; d = 2.5 ;; b = i >= d ;; Debug.Print b", "False", DisplayName = "an Integer compared with a Double, as Doubles")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 5 ;; b = Not (l = 5) ;; Debug.Print b", "False", DisplayName = "a comparison negated")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 5 ;; b = l > 1 And l < 10 ;; Debug.Print b", "True", DisplayName = "two comparisons And-ed")]
    [DataRow("Dim l As Long ;; l = 12 ;; l = l And 10 ;; Debug.Print l", "8", DisplayName = "the bits of a Long And-ed")]
    [DataRow("Dim y As Byte ;; y = 5 ;; y = Not y ;; Debug.Print y", "250", DisplayName = "the 8 bits of a Byte inverted")]
    [DataRow("Dim l As Long, b As Boolean ;; b = True ;; l = b + 1 ;; Debug.Print l", "0", DisplayName = "a Boolean added, as an Integer")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 7 ;; b = l ;; Debug.Print b", "True", DisplayName = "a Long assigned to a Boolean")]
    [DataRow("Dim b As Boolean, s As Single ;; s = 0.1 ;; b = s = 0.1 ;; Debug.Print b", "True", DisplayName = "a Single compared with a Double, as Singles (boxed)")]
    [DataRow("Dim b As Boolean ;; b = True ;; b = b Eqv False ;; Debug.Print b ;; b = False Imp b ;; Debug.Print b", "True", DisplayName = "Eqv and Imp")]
    [DataRow("Dim y As Byte, b As Boolean ;; b = True ;; y = b ;; Debug.Print Err.Number & \" \" & y", "0 255", DisplayName = "True assigned to a Byte (boxed)")]
    [DataRow("Dim b As Boolean, l As Long ;; l = 2 ;; b = l ;; b = Not b ;; Debug.Print b & \" \" & CInt(b)", "False 0", DisplayName = "a Boolean inverted")]
    [DataRow("Dim ll As LongLong, b As Boolean ;; ll = 9007199254740993^ ;; b = ll > 9007199254740992^ ;; Debug.Print b", "True", DisplayName = "two LongLongs no Double tells apart")]
    [DataRow("Dim s As Single, l As Long, b As Boolean ;; s = 16777216 ;; l = 16777217 ;; b = s < l ;; Debug.Print b", "True", DisplayName = "a Single compared with a Long, as Doubles")]
    public void AComparisonOrALogicalOperation_OnPayloads_IsWhatTheBoxedEvaluationEvaluates(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // a Currency as its ten-thousandths, a Date as its serial; what would round, or would not be a date, is the boxed evaluation's (recorded in Excel).
    [TestMethod]
    [DataRow("Dim c As Currency ;; c = 1.5 ;; c = c + 2 ;; Debug.Print c", "3.5", DisplayName = "a Currency plus an Integer")]
    [DataRow("Dim c As Currency ;; c = 922337203685477.5807@ ;; c = c + 1 ;; Debug.Print Err.Number & \" \" & c", "6 922337203685477.5807", DisplayName = "a Currency past its range")]
    [DataRow("Dim c As Currency ;; c = 1.25 ;; c = c * 3 ;; Debug.Print c", "3.75", DisplayName = "a Currency multiplied")]
    [DataRow("Dim c As Currency ;; c = 0.0001 ;; c = c * 0.5 ;; Debug.Print c ;; c = 0.0003 ;; c = c * 0.5 ;; Debug.Print c", "0.0002", DisplayName = "a Currency product rounded to even (boxed)")]
    [DataRow("Dim c As Currency, l As Long ;; c = 2.5 ;; l = c ;; Debug.Print l", "2", DisplayName = "a Currency rounded to a Long (boxed)")]
    [DataRow("Dim c As Currency, l As Long ;; c = 7 ;; l = c * 2 ;; Debug.Print l", "14", DisplayName = "a whole Currency assigned to a Long")]
    [DataRow("Dim c As Currency, b As Boolean ;; c = 0.0001 ;; b = c > 0 ;; Debug.Print b", "True", DisplayName = "a Currency compared")]
    [DataRow("Dim c As Currency, d As Double ;; c = 1.1 ;; d = c ;; Debug.Print d", "1.1", DisplayName = "a Currency assigned to a Double (boxed)")]
    [DataRow("Dim c As Currency, n As Long ;; For c = 0 To 1 Step 0.25 ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & c", "5 1.25", DisplayName = "a Currency counter")]
    [DataRow("Dim c As Currency ;; c = 3.5 ;; c = -c ;; Debug.Print c", "-3.5", DisplayName = "a Currency negated")]
    [DataRow("Dim c As Currency ;; c = 0.1 ;; c = c * 0.1 ;; Debug.Print c", "0.01", DisplayName = "a Currency multiplied by a Double")]
    [DataRow("Dim c As Currency, i As Integer ;; c = 10 ;; i = 3 ;; c = c - i ;; Debug.Print c & \" \" & TypeName(c - i)", "7 Currency", DisplayName = "a Currency less an Integer")]
    [DataRow("Dim d As Date ;; d = #1/1/2020# ;; d = d + 1 ;; Debug.Print CDbl(d)", "43832", DisplayName = "a Date plus a day")]
    [DataRow("Dim d As Date, e As Date ;; d = #1/1/2020# ;; e = #1/3/2020# ;; Debug.Print e - d & \" \" & TypeName(e - d)", "2 Double", DisplayName = "two Dates subtracted")]
    [DataRow("Dim d As Date, b As Boolean ;; d = #1/1/2020# ;; b = d < #1/2/2020# ;; Debug.Print b", "True", DisplayName = "a Date compared")]
    [DataRow("Dim d As Date ;; d = #12/31/9999# ;; d = d + 1 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 2958465", DisplayName = "a Date past 9999")]
    [DataRow("Dim d As Date ;; d = 3000000 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 0", DisplayName = "a number past the last date, assigned to a Date")]
    [DataRow("Dim d As Date ;; d = -657434.5 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "0 -657434.5", DisplayName = "noon of the first date")]
    [DataRow("Dim d As Date ;; d = -657435 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 0", DisplayName = "a number before the first date")]
    [DataRow("Dim d As Date ;; d = 2958465.9999999 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "0 2958465.9999999", DisplayName = "the last instant of the last date")]
    [DataRow("Dim d As Date ;; d = -657434.9 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "0 -657434.9", DisplayName = "late on the first date")]
    [DataRow("Dim d As Date ;; d = #1/1/100# ;; d = d - 1 ;; Debug.Print Err.Number & \" \" & CDbl(d)", "6 -657434", DisplayName = "a Date before the year 100")]
    [DataRow("Dim d As Date, n As Long ;; For d = #1/1/2020# To #1/5/2020# ;; n = n + 1 ;; Next ;; Debug.Print n & \" \" & CDbl(d)", "5 43836", DisplayName = "a Date counter")]
    [DataRow("Dim d As Date, n As Long ;; d = #1/1/2020# ;; If d Then n = 1 ;; Debug.Print n", "1", DisplayName = "If a Date")]
    [DataRow("Dim d As Date, x As Double ;; x = 0.5 ;; d = #1/1/2020# + x ;; Debug.Print CDbl(d)", "43831.5", DisplayName = "a Date plus a Double")]
    [DataRow("Dim d As Date ;; d = 1 ;; d = -d ;; Debug.Print CDbl(d)", "-1", DisplayName = "a Date negated")]
    public void ACurrencyOrADate_OnPayloads_IsWhatTheBoxedEvaluationEvaluates(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // the conditions of If, Do, While and Case on payloads; an error in a condition is the boxed evaluation's, and On Error Resume Next goes on into the
    // statement that follows it - the Then of an If (recorded in Excel).
    [TestMethod]
    [DataRow("Dim l As Long, n As Long ;; l = 4 ;; If l Mod 2 = 0 Then n = 1 Else n = 2 ;; Debug.Print n", "1", DisplayName = "If")]
    [DataRow("Dim d As Double, n As Long ;; d = 0.5 ;; If d Then n = 1 ;; Debug.Print n", "1", DisplayName = "If a Double that is not zero")]
    [DataRow("Dim l As Long, n As Long ;; Do While l < 5 ;; l = l + 2 ;; n = n + 1 ;; Loop ;; Debug.Print n & \" \" & l", "3 6", DisplayName = "Do While")]
    [DataRow("Dim l As Long ;; Do ;; l = l + 1 ;; Loop Until l >= 3 ;; Debug.Print l", "3", DisplayName = "Loop Until")]
    [DataRow("Dim l As Long ;; While l < 4 ;; l = l + 1 ;; Wend ;; Debug.Print l", "4", DisplayName = "While")]
    [DataRow("Dim l As Long, n As Long ;; l = 7 ;; Select Case l ;; Case 1 To 5 ;; n = 1 ;; Case Is > 6 ;; n = 2 ;; Case Else ;; n = 3 ;; End Select ;; Debug.Print n", "2", DisplayName = "Case To and Case Is")]
    [DataRow("Dim d As Double, n As Long ;; d = 3 ;; Select Case d ;; Case 2, 3 ;; n = 1 ;; End Select ;; Debug.Print n", "1", DisplayName = "a Double selector, Integer cases")]
    [DataRow("Dim l As Long, n As Long ;; l = 7 ;; Select Case True ;; Case l > 5 ;; n = 1 ;; End Select ;; Debug.Print n", "1", DisplayName = "Select Case True")]
    [DataRow("Dim i As Integer, n As Long ;; i = 3 ;; Select Case i ;; Case 2.5 To 3.5 ;; n = 1 ;; End Select ;; Debug.Print n", "1", DisplayName = "an Integer selector, a range of Doubles")]
    [DataRow("Dim d As Double, n As Long ;; d = 1 ;; If d / 0 > 1 Then n = 1 ;; Debug.Print Err.Number & \" \" & n", "11 1", DisplayName = "a condition divided by zero")]
    [DataRow("Dim i As Integer, n As Long ;; i = 32767 ;; If i + 1 > 0 Then n = 1 ;; Debug.Print Err.Number & \" \" & n", "6 1", DisplayName = "a condition past the range of an Integer")]
    public void ACondition_OnPayloads_IsWhatTheBoxedEvaluationDecides(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // a variable a ByRef parameter is an alias of is the caller's: assigned on payloads through the alias, the caller sees it.
    [TestMethod]
    public async Task AVariableAssignedThroughAByRefParameter_IsTheCallersVariable()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Sub Twice(ByRef N As Long)\r\nN = N * 2\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nDim L As Long\r\nL = 21\r\nTwice L\r\nL = L + 0\r\nDebug.Print L\r\nEnd Sub\r\n";

        Assert.AreEqual("42", (await RunAsync([], program)).Last());
    }
}

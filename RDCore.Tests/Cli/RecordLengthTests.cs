namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Len</c> and <c>LenB</c> of a record in a 64-bit environment, each row as MS-VBA reports it (64-bit Excel, 2026-10-21):
/// <c>Len</c> is the members' memory widths with no padding, <c>LenB</c> the padded in-memory size.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.22 Len / LenB")]
public sealed class RecordLengthTests
{
    private static string Program(params string[] lines)
        => "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n", lines) + "\r\n";

    [TestMethod]
    [DataRow("a As Long, b As String", 12, 16)]
    [DataRow("a As Byte", 1, 1)]
    [DataRow("a As Byte, b As Integer", 3, 4)]
    [DataRow("a As Byte, b As Long", 5, 8)]
    [DataRow("a As Byte, b As Double", 9, 16)]
    [DataRow("a As Byte, b As Variant", 25, 32)]
    [DataRow("a As Byte, b As String * 3", 4, 7)]
    [DataRow("b As String * 3", 3, 6)]
    [DataRow("a As Byte, b As Object", 9, 16)]
    [DataRow("a As Byte, b() As Long", 9, 16)]
    [DataRow("a As Byte, b(1 To 3) As Integer", 7, 8)]
    [DataRow("a As Byte, b As Currency", 9, 16)]
    [DataRow("a As Byte, b As Boolean", 3, 4)]
    [DataRow("a As Byte, b As Date", 9, 16)]
    [DataRow("a As Byte, b As LongLong", 9, 16)]
    [DataRow("a As Byte, b As LongPtr", 9, 16)]
    [DataRow("a As Byte, b As Collection", 9, 16)]
    [DataRow("a As Byte, b As Single", 5, 8)]
    [DataRow("a As Byte, b(2) As String", 25, 32)]
    [DataRow("a As Byte, b(1) As Variant", 49, 56)]
    [DataRow("a As Double, b As Byte", 9, 16)]
    [DataRow("a As String * 3, b As Byte", 4, 7)]
    [DataRow("a As Byte, b(1) As String * 2", 5, 9)]
    [DataRow("a As Byte, b(1) As Byte, c As Integer", 5, 6)]
    [DataRow("a As Byte, b() As Byte", 9, 16)]
    [DataRow("a As Byte, b As String * 3, c As Long", 8, 12)]
    public async Task LenAndLenB_OfARecord_AreMSVBAs(string fields, int len, int lenB)
        => CollectionAssert.AreEqual(new[] { $"{len} {lenB}" }, await ModuleWorkspace.RunAsync([], Program(
            ["Private Type T", .. fields.Split(", "), "End Type",
            "Public Sub Main()", "Dim r As T", "Debug.Print CStr(Len(r)) & \" \" & CStr(LenB(r))", "End Sub"])));

    [TestMethod]
    public async Task LenAndLenB_OfARecordWithNestedRecords_CountTheirOwnSizes()
        => CollectionAssert.AreEqual(new[] { "10 24", "19 40", "4 7", "6 10", "3 4", "5 8" }, await ModuleWorkspace.RunAsync([], Program(
            "Private Enum E1", "X1", "End Enum",
            "Private Type T5", "a As Byte", "b As Double", "End Type",
            "Private Type T8", "b As String * 3", "End Type",
            "Private Type T2", "a As Byte", "End Type",
            "Private Type T12", "a As Byte", "b As T5", "End Type",
            "Private Type T26", "a As Byte", "b(1) As T5", "End Type",
            "Private Type T28", "a As T8", "b As Byte", "End Type",
            "Private Type T29", "a As Byte", "b As T8", "c As Integer", "End Type",
            "Private Type T32", "a As Integer", "b As T2", "End Type",
            "Private Type T24", "a As Byte", "b As E1", "End Type",
            "Public Sub Main()",
            "Dim v12 As T12, v26 As T26, v28 As T28, v29 As T29, v32 As T32, v24 As T24",
            "Debug.Print CStr(Len(v12)) & \" \" & CStr(LenB(v12))",
            "Debug.Print CStr(Len(v26)) & \" \" & CStr(LenB(v26))",
            "Debug.Print CStr(Len(v28)) & \" \" & CStr(LenB(v28))",
            "Debug.Print CStr(Len(v29)) & \" \" & CStr(LenB(v29))",
            "Debug.Print CStr(Len(v32)) & \" \" & CStr(LenB(v32))",
            "Debug.Print CStr(Len(v24)) & \" \" & CStr(LenB(v24))",
            "End Sub")));

    // what a member holds does not change either size: a String, a Variant and a resizable array member are measured as the slot they are.
    [TestMethod]
    public async Task LenAndLenB_OfARecord_DoNotMoveWithWhatItsMembersHold()
        => CollectionAssert.AreEqual(new[] { "12 16", "25 32", "9 16" }, await ModuleWorkspace.RunAsync([], Program(
            "Private Type T1", "a As Long", "b As String", "End Type",
            "Private Type T6", "a As Byte", "b As Variant", "End Type",
            "Private Type T10", "a As Byte", "b() As Long", "End Type",
            "Public Sub Main()", "Dim v1 As T1, v6 As T6, v10 As T10",
            "v1.b = \"hello world\"", "v6.b = \"hello\"", "ReDim v10.b(5)",
            "Debug.Print CStr(Len(v1)) & \" \" & CStr(LenB(v1))",
            "Debug.Print CStr(Len(v6)) & \" \" & CStr(LenB(v6))",
            "Debug.Print CStr(Len(v10)) & \" \" & CStr(LenB(v10))",
            "End Sub")));
}

using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.22</strong> <c>Len</c> and <c>LenB</c> take "any valid string expression, or any valid variable name": a value of a declared type
/// that is neither a <c>String</c> nor a <c>Variant</c> must be a variable, or it is VBC09333, Variable required. Each row is what MS-VBA reported compiling
/// the same statement in Excel (64-bit, en-US).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.22 Len / LenB")]
public sealed class LenOperandCompileErrorTests
{
    private const string VariableRequired = "Variable required";
    private const string Compiles = "";

    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("DfltNum", ClassModule("DfltNum", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 42", "End Property")),
        ("DfltStr", ClassModule("DfltStr", "Public Property Get Value() As String", "Attribute Value.VB_UserMemId = 0", "    Value = \"abc\"", "End Property")),
        ("Taker", ClassModule("Taker", "Public Property Get Num() As Long", "End Property")),
    ];

    private static async Task AssertCompiles(string statement, string expected)
    {
        var errors = await LoadErrorsAsync(Classes,
            "Attribute VB_Name = \"Program\"\r\n"
            + "Private Type TRec\r\n    a As Long\r\nEnd Type\r\n"
            + "Private Function FnLong() As Long\r\nEnd Function\r\nPrivate Function FnStr() As String\r\nEnd Function\r\n"
            + "Private Function FnRec() As TRec\r\nEnd Function\r\n"
            + "Public Sub Main(ByVal pl As Long)\r\n"
            + "    Dim v As Variant, la(3) As Long, r As TRec, l As Long, d As Date\r\n    Const k As Long = 5\r\n"
            + $"    {statement}\r\nEnd Sub\r\n");

        if (expected.Length == 0)
        {
            Assert.IsEmpty(errors, string.Join(" | ", errors));
            return;
        }

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith(expected, errors[0]);
    }

    [TestMethod]
    [DataRow("v = Len(5)")]
    [DataRow("v = LenB(5)")]
    [DataRow("v = Len(1 + 2)")]
    [DataRow("v = Len(-l)")]
    [DataRow("v = Len(True)")]
    [DataRow("v = Len(#1/1/2000#)")]
    [DataRow("v = Len(CLng(5))")]
    [DataRow("v = Len(FnLong())")]
    [DataRow("v = Len(k)")]
    [DataRow("v = Len(vbOK)")]
    [DataRow("Dim o As DfltNum: v = Len(o)")]
    [DataRow("Dim o As DfltNum: v = Len(o.Value)")]
    [DataRow("Dim t As New Taker: v = Len(t.Num)")]
    public Task AValueThatIsNoStringAndNoVariable_IsVariableRequired(string statement) => AssertCompiles(statement, VariableRequired);

    [TestMethod]
    [DataRow("v = Len(l)")]
    [DataRow("v = Len((l))")]
    [DataRow("v = Len(pl)")]
    [DataRow("v = Len(d)")]
    [DataRow("v = Len(la(1))")]
    [DataRow("v = Len(r)")]
    [DataRow("v = Len(r.a)")]
    [DataRow("v = Len(FnRec())")]
    [DataRow("v = Len(FnStr())")]
    [DataRow("v = Len(\"x\" & 1)")]
    [DataRow("v = Len(CStr(5))")]
    [DataRow("v = Len(vbCrLf)")]
    [DataRow("v = Len(Now)")]
    [DataRow("v = Len(Null)")]
    [DataRow("v = Len(Empty)")]
    [DataRow("v = Len(New DfltStr)")]
    [DataRow("Dim o As DfltStr: v = Len(o)")]
    [DataRow("Dim o As Object: v = Len(o)")]
    public Task AStringOrAVariable_Compiles(string statement) => AssertCompiles(statement, Compiles);
}

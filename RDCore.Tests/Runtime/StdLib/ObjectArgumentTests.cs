using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §5.5.1.2.13</strong> An object passed to a function of the <c>Conversion</c> or the <c>Math</c> module stands for its simple data value:
/// what its default member returns.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same expression, in Excel (64-bit, en-US), unless a row says otherwise.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2 Let-coercion")]
public sealed class ObjectArgumentTests
{
    // a class whose default member returns what `body` assigns to Value, declared as `type`.
    private static (string, string) Defaulted(string name, string type, string body)
        => (name, ClassModule(name,
            $"Public Property Get Value(){type}",
            "Attribute Value.VB_UserMemId = 0",
            body,
            "End Property"));

    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        Defaulted("DfltNum", " As Long", "    Value = 42"),
        Defaulted("DfltNeg", " As Long", "    Value = -7"),
        Defaulted("DfltStr", " As String", "    Value = \"duck\""),
        Defaulted("DfltNumStr", " As String", "    Value = \"-2.5\""),
        Defaulted("DfltDbl", " As Double", "    Value = 1.5"),
        Defaulted("DfltNull", string.Empty, "    Value = Null"),
        Defaulted("DfltEmpty", string.Empty, string.Empty),
        Defaulted("DfltChain", " As Object", "    Set Value = New DfltNum"),
        Defaulted("DfltObj", " As Object", "    Set Value = New Collection"),
        Defaulted("DfltRaise", " As Long", "    Err.Raise 5"),
    ];

    // Nothing is passed through a variable: MS-VBA refuses the literal at compile time ("Invalid use of object").
    private const string Declarations = "Private nob As Object";

    private static Task AssertRows(params string[] rows) => ValueRows.AssertRows(Classes, Declarations, rows);

    [TestMethod]
    public Task Conversion()
        => AssertRows(
            "CInt(New DfltNum) => Integer 42",
            "CStr(New DfltNum) => String 42",
            "CBool(New DfltNum) => Boolean True",
            "CByte(New DfltNum) => Byte 42",
            "CCur(New DfltDbl) => Currency 1.5",
            "CSng(New DfltDbl) => Single 1.5",
            "CLng(New DfltNumStr) => Long -2",
            "CLngLng(New DfltNum) => LongLong 42",
            "CDbl(New DfltDbl) => Double 1.5",
            "CDate(New DfltDbl) => Date 1.5",
            "CVDate(New DfltDbl) => Date 1.5",
            "CDec(New DfltNum) => Decimal 42",
            "CVErr(New DfltNum) => Error Error 42",
            "CInt(nob) => err 91",
            "CStr(nob) => err 91",
            "CInt(New DfltRaise) => err 5",
            "CInt(New DfltStr) => err 13",
            "CInt(New DfltNull) => err 94",
            "CStr(New DfltNull) => err 94",
            "CInt(New DfltEmpty) => Integer 0",
            "CStr(New DfltEmpty) => String",
            "Fix(New DfltDbl) => Double 1",
            "Int(New DfltNumStr) => Double -3",
            "Int(New DfltNull) => Null Null",
            "Hex(New DfltNum) => String 2A",
            // Hex(Null) is Null, but a Null read from an object is Let-coerced, and raises.
            "Hex(New DfltNull) => err 94",
            "Oct(New DfltNum) => String 52",
            "Str(New DfltNum) => String  42",
            "Str(New DfltNull) => err 94",
            "Val(New DfltNumStr) => Double -2.5",
            "Error(New DfltNum) => String Application-defined or object-defined error",
            "CStr(Err) => String 0");

    [TestMethod]
    public Task CVar_ReturnsTheObject()
        => AssertRows(
            "TypeName(CVar(New DfltNum)) => String DfltNum",
            "TypeName(CVar(nob)) => String Nothing",
            "TypeName(CVar(Err)) => String ErrObject");

    [TestMethod]
    public Task Math()
        => AssertRows(
            "Abs(New DfltNeg) => Long 7",
            "Abs(New DfltNumStr) => Double 2.5",
            "Abs(New DfltNull) => Null Null",
            "Abs(New DfltEmpty) => Integer 0",
            "Abs(nob) => err 91",
            "Abs(New DfltRaise) => err 5",
            "Abs(New DfltStr) => err 13",
            "Abs(Err) => Long 0",
            "Sgn(New DfltNeg) => Integer -1",
            "Sgn(New DfltNull) => err 94",
            "Round(New DfltDbl) => Double 2",
            "Round(2.567, New DfltNum) => err 5",
            "Sqr(New DfltNum) => Double 6.48074069840786",
            "Exp(nob) => err 91",
            "Rnd(New DfltNeg) => Single 8.832568E-02");

    [TestMethod]
    // MS-VBA raises error 51, an internal error, for a default member that returns an object. RD-VBA raises no internal error for a specified input, and
    // reads that object's simple data value in turn, as Let-coercing the object would (MS-VBAL 5.5.1.2.13). A Collection's default member takes a key,
    // so the Collection has no value: error 13, as MS-VBAL 5.6.2.3 allows and MS-VBA raises for it held in a Variant.
    public Task ADefaultMemberThatReturnsAnObject_StandsForThatObjectsValue()
        => AssertRows(
            "Abs(New DfltChain) => Long 42",
            "Abs(New DfltObj) => err 13");
}

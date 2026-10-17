using static RDCore.Tests.Runtime.StdLib.Information.InformationWorkspace;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §5.6.2.2</strong> An object where a value is expected is evaluated to its simple data value - by a function of the library that reads a
/// value, and by every operator but <c>Is</c> (<strong>§5.6.9.2</strong>) - and what has none is an error: 438 for an object with no default member, 450 for
/// one whose default member takes an argument, and 13 for an object a default member returned that has no value (<strong>§5.6.2.3</strong>).
/// </summary>
/// <remarks>
/// Each object is held in a <c>Variant</c>, which compiles whatever it is given. The expected values are what MS-VBA gives (recorded in Excel), except where a
/// row says otherwise: MS-VBA's own functions do not all raise the same error for the same object.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.2.2 Evaluation to a simple data value")]
public sealed class ObjectValueErrorTests
{
    private static readonly string[] Reads =
    [
        "Left(o, 1)", "Format(o)", "CStr(o)", "Abs(o)", "Len(o)", "o & \"\"", "o + 1.5", "CLng(o)", "Int(o)", "Hex(o)", "Mid(o, 1)", "InStr(o, \"4\")",
    ];

    // what each of Reads gives for the object `setup` holds in o, a line each: its value, or "ERR" and the error.
    private static async Task<string[]> Read(string setup)
    {
        var printed = await Run(string.Empty, [
            "Dim o As Variant, v As Variant",
            setup,
            "On Error Resume Next",
            .. Reads.SelectMany(read => new[] { "Err.Clear: v = Empty", $"v = {read}", "If Err.Number <> 0 Then Debug.Print \"ERR \" & Err.Number Else Debug.Print v" }),
        ]);

        Assert.HasCount(Reads.Length, printed, string.Join(" | ", printed));
        return printed;
    }

    [TestMethod]
    // MS-VBA: 450, but 438 for Abs, Int and +.
    public async Task AnObjectWhoseDefaultMemberTakesAnArgument_IsError450()
        => CollectionAssert.AreEqual(Enumerable.Repeat("ERR 450", Reads.Length).ToArray(), await Read("Set o = New Collection"));

    [TestMethod]
    // MS-VBAL 5.6.2.3: "error 9 (Subscript out of range) or 13 (Type mismatch) during evaluation at runtime". MS-VBA: 13, but 438 for Format and 450 for
    // CStr, CLng and Hex.
    public async Task AnObjectWhoseDefaultMemberReturnsAnObjectWithNoValue_IsError13()
        => CollectionAssert.AreEqual(Enumerable.Repeat("ERR 13", Reads.Length).ToArray(), await Read("Set o = New DfltObj"));

    [TestMethod]
    public async Task AnObjectWithNoDefaultMember_IsError438()
        => CollectionAssert.AreEqual(Enumerable.Repeat("ERR 438", Reads.Length).ToArray(), await Read("Set o = New Class1"));

    [TestMethod]
    // MS-VBA: Len, & and + of this object in a Variant raise 458 (Variable uses an Automation type not supported), where the same object in an Object
    // variable is read as its value (o + 1.5 is 43.5): an artifact of how it calls a private class, which a value read the way the specification reads it
    // has no part of. Len of the value, a Long, is the 4 bytes a Long is stored in.
    public async Task AnObjectWithADefaultMember_IsItsValue()
        => CollectionAssert.AreEqual(new[] { "4", "42", "42", "42", "4", "42", "43.5", "42", "42", "2A", "42", "1" }, await Read("Set o = New DfltNum"));

    [TestMethod]
    // MS-VBAL 5.6.9.2: a simple data operator "first evaluates its operands as simple data values" - MS-VBA: 43.5, whichever side the object is on.
    [DataRow("o + 1.5", "43.5")]
    [DataRow("1.5 + o", "43.5")]
    [DataRow("o * 2", "84")]
    [DataRow("-o", "-42")]
    [DataRow("o = 42", "True")]
    [DataRow("o & \"!\"", "42!")]
    public async Task AnOperandThatIsAnObject_IsItsValue(string expression, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(string.Empty, "Dim o As Object", "Set o = New DfltNum", $"Debug.Print {expression}"));
}

using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>LSet</c> of a record onto a record of another size (<strong>MS-VBAL §5.4.3.6</strong>, whose copy is implementation-defined): MS-VBA copies the bytes
/// both records have and leaves the rest of the destination as it was, a field the copy reaches only part of included (observed in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.6 LSet Statement")]
public sealed class LSetRecordTailTests
{
    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Type TI\r\n    I As Integer\r\nEnd Type\r\n" +
            "Private Type TL\r\n    L As Long\r\nEnd Type\r\n" +
            "Private Type TII\r\n    A As Integer\r\n    B As Integer\r\nEnd Type\r\n" +
            "Public Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static async Task Expect(string[] expected, string program, VBExecutionEngine engine)
        => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", await RunAsync([], program, engine)));

    // the low two bytes of the Long are the Integer's, and its high two bytes are what they were.
    [TestMethod]
    [Ignore("a field LSet reaches only part of: RD-VBAL §5.4.3.6 sets it to its default, so that no place holds half a value; MS-VBA keeps its other bytes (observed in Excel)")]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoALong_KeepsTheBytesItDoesNotReach(VBExecutionEngine engine)
        => await Expect(["L 12340102", "L 102", "L FFFF 65535"], Program(
            "    Dim a As TI, b As TL",
            "    b.L = &H12345678",
            "    a.I = &H102",
            "    LSet b = a",
            "    Debug.Print \"L \" & Hex(b.L)",
            "    b.L = 0",
            "    LSet b = a",
            "    Debug.Print \"L \" & Hex(b.L)",
            "    a.I = -1",
            "    LSet b = a",
            "    Debug.Print \"L \" & Hex(b.L) & \" \" & b.L"), engine);

    // a field the copy does not reach at all is what it was.
    [TestMethod]
    [Ignore("a field LSet does not reach: RD-VBAL §5.4.3.6 sets it to its default; MS-VBA leaves it as it was (observed in Excel)")]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoTwoFields_LeavesTheSecondAsItWas(VBExecutionEngine engine)
        => await Expect(["A 5 B 22"], Program(
            "    Dim a As TI, t As TII",
            "    t.A = 11: t.B = 22",
            "    a.I = 5",
            "    LSet t = a",
            "    Debug.Print \"A \" & t.A & \" B \" & t.B"), engine);

    // a longer record gives the destination the bytes it has room for: RD-VBA and MS-VBA agree.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task ALongerRecord_LSetOntoAnInteger_GivesItTheLowBytes(VBExecutionEngine engine)
        => await Expect(["I 3"], Program(
            "    Dim a As TL, t As TI",
            "    a.L = &H10003",
            "    LSet t = a",
            "    Debug.Print \"I \" & t.I"), engine);
}

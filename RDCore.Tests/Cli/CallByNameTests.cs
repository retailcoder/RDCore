using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>CallByName</c> (<strong>MS-VBAL §6.1.2.8.1.1</strong>) calls a member of an object by its name, late-bound: the member is found on the default interface of the
/// object's class, and invoked as a call written in source invokes it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.1 CallByName")]
public sealed class CallByNameTests
{
    private static readonly (string Name, string Source)[] Calculator =
    [
        ("Calculator", ClassModule("Calculator",
            "Option Explicit",
            "Public Total As Long",
            "Private mName As String",
            "Private mFriend As Object",
            "Public Function Add(ByVal a As Long, ByVal b As Long) As Long",
            "    Add = a + b",
            "End Function",
            "Public Function Greet(Optional ByVal who As String) As String",
            "    Greet = \"Hello \" & who",
            "End Function",
            "Public Sub Accumulate(ByVal amount As Long)",
            "    Total = Total + amount",
            "End Sub",
            "Public Property Get Name() As String",
            "    Name = mName",
            "End Property",
            "Public Property Let Name(ByVal value As String)",
            "    mName = value",
            "End Property",
            "Public Property Get Buddy() As Object",
            "    Set Buddy = mFriend",
            "End Property",
            "Public Property Set Buddy(ByVal value As Object)",
            "    Set mFriend = value",
            "End Property")),
    ];

    private static Task<string[]> Run(params string[] lines)
        => RunAsync(Calculator, $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim c As Calculator\r\nSet c = New Calculator\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AFunction_IsCalledAsAMethod_WithItsArgumentsCoercedToItsParameters()
        => CollectionAssert.AreEqual(new[] { "5", "42" }, await Run(
            "Debug.Print CallByName(c, \"Add\", VbMethod, 2, 3)",
            "Debug.Print CallByName(c, \"add\", VbMethod, \"40\", 2)"));

    [TestMethod]
    public async Task ASub_IsCalledAsAMethod_ForWhatItDoes()
        => CollectionAssert.AreEqual(new[] { "10" }, await Run(
            "CallByName c, \"Accumulate\", VbMethod, 4",
            "CallByName c, \"Accumulate\", VbMethod, 6",
            "Debug.Print c.Total"));

    [TestMethod]
    public async Task AProperty_IsLetAndGot_ByName()
        => CollectionAssert.AreEqual(new[] { "Duck", "Duck" }, await Run(
            "CallByName c, \"Name\", VbLet, \"Duck\"",
            "Debug.Print CallByName(c, \"Name\", VbGet)",
            "Debug.Print c.Name"));

    [TestMethod]
    public async Task AnObjectProperty_IsSet_ByName()
        => CollectionAssert.AreEqual(new[] { "set", "True" }, await Run(
            "Dim other As Calculator",
            "Set other = New Calculator",
            "CallByName c, \"Buddy\", VbSet, other",
            "Debug.Print \"set\"",
            "Dim same As Boolean",
            "same = (c.Buddy Is other)",
            "Debug.Print same"));

    [TestMethod]
    public async Task APublicVariable_IsGot_ByName()
        => CollectionAssert.AreEqual(new[] { "7" }, await Run(
            "c.Total = 7",
            "Debug.Print CallByName(c, \"Total\", VbGet)"));

    [TestMethod]
    public async Task AnOmittedOptionalArgument_IsItsTypesDefault()
        => CollectionAssert.AreEqual(new[] { "Hello", "Hello you" }, await Run(
            "Debug.Print CallByName(c, \"Greet\", VbMethod)",
            "Debug.Print CallByName(c, \"Greet\", VbMethod, \"you\")"));

    [TestMethod]
    public async Task AnObject_PassedToAParamArray_IsTheObject_NotItsDefaultValue()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(Calculator,
            "Attribute VB_Name = \"Program\"\r\n" +
            "Private Function First(ParamArray items() As Variant) As Long\r\nDim item As Calculator\r\nSet item = items(0)\r\nFirst = item.Total\r\nEnd Function\r\n" +
            "Public Sub Main()\r\nDim c As Calculator\r\nSet c = New Calculator\r\nc.Total = 9\r\nDebug.Print First(c)\r\nEnd Sub\r\n"));

    private static async Task<string> FailsWith(params string[] lines)
    {
        string? error = null;
        await InspectAsync(Calculator,
            $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim c As Calculator\r\nSet c = New Calculator\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n",
            (_, result, _) =>
            {
                Assert.AreEqual(RDCore.SDK.Platform.Protocol.ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
                error = result.ErrorCode;
                return Task.CompletedTask;
            });
        return error!;
    }

    [TestMethod]
    public async Task AMemberTheObjectDoesNotHave_IsError438()
        => StringAssert.Contains(await FailsWith("Debug.Print CallByName(c, \"Subtract\", VbMethod, 1, 2)"), "438");

    [TestMethod]
    public async Task AMember_CalledAsWhatItIsNot_IsError438()
        => StringAssert.Contains(await FailsWith("CallByName c, \"Add\", VbLet, 1"), "438");

    [TestMethod]
    public async Task Nothing_IsError91()
        => StringAssert.Contains(await FailsWith("Set c = Nothing", "Debug.Print CallByName(c, \"Add\", VbMethod, 1, 2)"), "91");

    [TestMethod]
    public async Task TooManyArguments_IsError450()
        => StringAssert.Contains(await FailsWith("Debug.Print CallByName(c, \"Add\", VbMethod, 1, 2, 3)"), "450");

    [TestMethod]
    public async Task AMissingArgument_IsError449()
        => StringAssert.Contains(await FailsWith("Debug.Print CallByName(c, \"Add\", VbMethod, 1)"), "449");
}

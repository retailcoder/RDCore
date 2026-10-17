using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.2.20</strong> the arguments of a <c>RaiseEvent</c> are evaluated once, whatever the number of handlers, and "the next invocation's
/// argument for a ByRef parameter is what the parameter last contained": what one handler leaves in it is what the next one starts with.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.20 RaiseEvent Statement")]
public sealed class RaiseEventArgumentTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Src", ClassModule("Src", "Public Event Moved(ByRef x As Long)", "Public Sub Fire()", "RaiseEvent Moved(5)", "End Sub")),
        ("Sink1", ClassModule("Sink1", "Public WithEvents S As Src", "Private Sub S_Moved(ByRef x As Long)", "x = x + 1", "End Sub")),
        ("Sink2", ClassModule("Sink2", "Public WithEvents S As Src", "Public Seen As Long", "Private Sub S_Moved(ByRef x As Long)", "Seen = x", "End Sub")),
    ];

    // an argument that is not a variable - a literal - has a location of its own for the handlers of the one event (recorded in Excel).
    [TestMethod]
    public async Task AByRefArgumentThatIsNotAVariable_IsWhatThePreviousHandlerLeftInIt()
        => CollectionAssert.AreEqual(new[] { "6" }, await RunAsync(Classes,
            "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim src As New Src, one As New Sink1, two As New Sink2\r\nSet one.S = src\r\nSet two.S = src\r\nsrc.Fire\r\nDebug.Print two.Seen\r\nEnd Sub\r\n"));
}

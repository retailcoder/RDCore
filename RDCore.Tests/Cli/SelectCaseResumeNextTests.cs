using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.4.1, §5.4.4.2: after an error, <c>Resume Next</c> goes on at the statement immediately following the one that caused it. A <c>Select Case</c>
/// is one statement up to its <c>End Select</c>: an error in its selector goes on past the whole construct, no <c>Case</c> matched - <c>Case Else</c>
/// included (recorded in Excel, grid <c>oracle\s16</c> C01-C03).
/// </summary>
[TestClass]
public sealed class SelectCaseResumeNextTests
{
    private static string Run(string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("Select Case i + 1 ;; Case Else ;; Debug.Print \"else\" ;; End Select", DisplayName = "C01 Case Else")]
    [DataRow("Select Case i + 1 ;; Case 1 ;; Debug.Print \"one\" ;; Case Else ;; Debug.Print \"else\" ;; End Select", DisplayName = "C02 a Case, then Case Else")]
    [DataRow("Select Case i + 1 ;; Case 1 ;; Debug.Print \"one\" ;; End Select", DisplayName = "C03 a Case")]
    public void AnErrorInTheSelector_UnderOnErrorResumeNext_GoesOnPastEndSelect(string select)
        => Assert.AreEqual("6", Run($"On Error Resume Next ;; Dim i As Integer ;; i = 32767 ;; {select} ;; Debug.Print Err.Number"));

    [TestMethod]
    public void AnErrorInTheSelector_ResumedNext_GoesOnPastEndSelect()
        => Assert.AreEqual("6/after", Run("On Error GoTo Handler ;; Dim i As Integer ;; i = 32767 ;; Select Case i + 1 ;; Case Else ;; Debug.Print \"else\" ;; End Select"
            + " ;; Debug.Print \"after\" ;; Exit Sub ;; Handler: ;; Debug.Print Err.Number ;; Resume Next"));
}

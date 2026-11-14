using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The properties of a Visual Basic 6.0 control that a program can read and not set, which only a form's file and a designer set: a <c>TextBox</c>'s
/// <c>MultiLine</c> and <c>ScrollBars</c>, which VB's library declares with no Property Let. An early-bound assignment is a compile error (measured in VB6).
/// </summary>
[TestClass]
public sealed class VisualBasicFormReadOnlyAtRunTimeTests
{
    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    [TestMethod]
    [DataRow("MultiLine", "True")]
    [DataRow("ScrollBars", "2")]
    public async Task AnEarlyBoundAssignment_IsCantAssignToReadOnlyProperty(string property, string value)
    {
        var program = string.Join("\r\n", "Attribute VB_Name = \"Program\"", "Public Sub Main()", "    Dim t As VB.TextBox", $"    t.{property} = {value}", "End Sub", string.Empty);
        var errors = await ModuleWorkspace.LoadErrorsAsync([], program, Libraries, SupportedLanguages.VB6);

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Can't assign to read-only property");
    }
}

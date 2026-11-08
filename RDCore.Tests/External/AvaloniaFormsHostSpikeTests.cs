using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.External.Client;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.External;

/// <summary>
/// Spike S3 (throwaway, not for merge): a VBA UserForm whose window is an Avalonia one, in an external host of its own (spikes/RDCore.FormsHost.Spike), driven
/// over the forms and automation wire by the environment host's runtime, which runs the form's code. Inconclusive unless RDFORMS_SPIKE_HOST_DIR names the
/// spike host's build output.
/// </summary>
[TestClass]
[TestCategory("Spike")]
public sealed class AvaloniaFormsHostSpikeTests
{
    private const string CodeBehind = """
        Private Sub UserForm_Initialize()
            Debug.Print "Initialize"
        End Sub

        Private Sub AmountBox_KeyPress(ByVal KeyAscii As MSForms.ReturnInteger)
            Debug.Print "KeyPress " & KeyAscii
            KeyAscii = Asc(UCase$(Chr$(KeyAscii)))
        End Sub

        Private Sub AmountBox_Change()
            Debug.Print "AmountBox_Change " & AmountBox.Text
        End Sub

        Private Sub TextBox1_Change()
            Debug.Print "TextBox1_Change " & TextBox1.Text
        End Sub

        Private Sub OkButton_Click()
            TextBox1.Text = "OK:" & AmountBox.Text
            Debug.Print "Click " & TextBox1.Text
            Me.Hide
        End Sub

        """;

    private const string Program = """
        Attribute VB_Name = "Program"
        Option Explicit

        Public Sub Main()
            Debug.Print "Show modal"
            ExampleView.Show
            Debug.Print "after modal: " & ExampleView.TextBox1.Text & " / " & ExampleView.AmountBox.Text
            ExampleView.Show vbModeless
            Debug.Print "modeless shown: " & ExampleView.Visible
            ExampleView.AmountBox.Text = "set by code"
            Unload ExampleView
            Debug.Print "done"
        End Sub

        """;

    [TestMethod]
    public async Task AUserForm_ShownModalAndModeless_RunsInAnAvaloniaHost_WithItsEventsHandledByTheRuntime()
    {
        if (SpikeHost() is not { } executable)
        {
            Assert.Inconclusive("RDFORMS_SPIKE_HOST_DIR does not name the spike host's build output.");
            return;
        }

        Environment.SetEnvironmentVariable("RDFORMS_SPIKE_SCRIPT", "type AmountBox abc; click OkButton");
        var options = Options.Create(new SdkAppOptions());
        using var host = new ExternalHost(
            () => new ChildConnection(
                new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, NullLogger<RDCoreServerProcess>.Instance),
                new RDCorePlatformDefaultTransportLayer(options),
                NullLogger<ChildConnection>.Instance),
            executable,
            NullLogger<ExternalHost>.Instance);

        var output = await ModuleWorkspace.RunAsync(
            [], Program.ReplaceLineEndings("\r\n"),
            new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Cli.UnattendedRunTests.Shipped, Outside: ExternalHosts.WorldOf(host)),
            [Cli.UnattendedRunTests.ExampleView(CodeBehind.ReplaceLineEndings("\r\n"))]);

        Console.WriteLine(string.Join(Environment.NewLine, output));
        CollectionAssert.AreEqual(
            new[]
            {
                "Show modal",
                "Initialize",
                "KeyPress 97", "AmountBox_Change A",
                "KeyPress 98", "AmountBox_Change AB",
                "KeyPress 99", "AmountBox_Change ABC",
                "TextBox1_Change OK:ABC",
                "Click OK:ABC",
                "after modal: OK:ABC / ABC",
                "modeless shown: True",
                "AmountBox_Change set by code",
                "done",
            },
            output.Select(line => line.Trim()).ToArray(),
            string.Join(" | ", output));
    }

    // the spike host's build output, copied under this process's folder: an external host is started only from the platform's root or the starter's own folder.
    private static string? SpikeHost()
    {
        if (Environment.GetEnvironmentVariable("RDFORMS_SPIKE_HOST_DIR") is not { Length: > 0 } built || !Directory.Exists(built))
        {
            return null;
        }

        var target = Path.Combine(AppContext.BaseDirectory, "formshost-spike");
        foreach (var file in Directory.EnumerateFiles(built, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(built, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        return Path.Combine(target, OperatingSystem.IsWindows() ? "RDCore.FormsHost.Spike.exe" : "RDCore.FormsHost.Spike");
    }
}

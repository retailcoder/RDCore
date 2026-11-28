using RDCore.SDK.Platform;
using RDCore.Tests.Client;
using System.Diagnostics;
using System.Text;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc</c> with no arguments, started at the root of its platform, beside <c>rdcore.json</c>: the REPL of that platform, whose root is the folder
/// <c>rdc</c> is in, whatever root its environment names.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RdcAtThePlatformRootTests
{
    // a cap on a hang, not a budget: each wait is on the process itself, which a loaded machine starts and stops as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    [TestMethod]
    [DataRow(false, DisplayName = "no root in its environment")]
    [DataRow(true, DisplayName = "another root in its environment")]
    public async Task Rdc_WithNoArguments_StartedAtItsRoot_RunsTheLinesItIsGiven_AndEndsWithItsInput(bool givenAnotherRoot)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "RDCore", "rdc-at-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var start = new ProcessStartInfo(TestPlatformRoot.Rdc)
        {
            WorkingDirectory = TestPlatformRoot.Directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(scratch, "User");
        start.Environment[PlatformEnvironment.LogsEnvironmentVariable] = Path.Combine(scratch, "Logs");
        start.Environment.Remove(PlatformEnvironment.RootEnvironmentVariable);
        if (givenAnotherRoot)
        {
            // a folder that is not there: a process that took its root from the variable would find no manifest, and no language server, in it.
            start.Environment[PlatformEnvironment.RootEnvironmentVariable] = Path.Combine(scratch, "no-platform");
        }

        using var rdc = Process.Start(start)!;
        try
        {
            var output = rdc.StandardOutput.ReadToEndAsync();
            var errors = rdc.StandardError.ReadToEndAsync();
            await rdc.StandardInput.WriteAsync("Debug.Print 6 * 7\n");
            rdc.StandardInput.Close();
            await rdc.WaitForExitAsync().WaitAsync(Bound);
            var printed = (await output.WaitAsync(Bound)).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var context = $"{string.Join("\n", printed)}\n{await errors.WaitAsync(Bound)}";

            Assert.AreEqual(0, rdc.ExitCode, context);
            CollectionAssert.Contains(printed, "42", context);
            Assert.IsFalse(Directory.Exists(Path.Combine(scratch, "no-platform")), context);
        }
        finally
        {
            if (!rdc.HasExited)
            {
                rdc.Kill(entireProcessTree: true);
            }

            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the folder is a temporary one.
            }
        }
    }
}

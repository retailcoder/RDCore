using RDCore.SDK.Client;

namespace RDCore.Tests.Client;

/// <summary>
/// The pre-launch preparation of a client that starts the language server itself, as an IDE does before it starts a server it registered: what to start, where,
/// with what, from the manifest of the platform root.
/// </summary>
[TestClass]
public sealed class RDCoreLanguageServerLaunchTests
{
    private string _root = null!;

    private static readonly string Executable = OperatingSystem.IsWindows() ? "RDCore.LanguageServer.exe" : "RDCore.LanguageServer";

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "RDCore", "launch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "RDCore.LanguageServer"));
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    private void WithManifest(string json) => File.WriteAllText(Path.Combine(_root, "rdcore.json"), json);

    private void WithLanguageServer() => File.WriteAllText(Path.Combine(_root, "RDCore.LanguageServer", Executable), string.Empty);

    private static string Manifest(string langService) => $$"""
        {
            "generatedUtc":  "2026-10-29T12:41:21.3333457Z",
            "extensionsDirectory":  "Extensions",
            "langService":  "{{langService}}",
            "hostService":  "RDCore.CLI/rdc.exe",
            "platformVersion":  "0.1",
            "parseServer":  "RDCore.Parsing/RDCore.ParseServer.exe"
        }
        """;

    private RDCoreLanguageServerLaunchOptions Options(string? pipe = null, int? clientProcessId = null) => new()
    {
        PlatformRoot = _root,
        Workspace = "file:///c:/workspace",
        PipeName = pipe,
        ClientProcessId = clientProcessId,
    };

    [TestMethod]
    public void TheLanguageServer_IsTheManifests_StartedInItsFolder_WithThePipeAndTheWorkspace_AndTheRootNamed()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "RDCore.Test.Pipe"));

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), launch.ExecutablePath);
        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer"), launch.WorkingDirectory);
        CollectionAssert.AreEqual(new[] { "--pipe-name", "RDCore.Test.Pipe", "--workspace", "file:///c:/workspace" }, launch.Arguments.ToArray());
        Assert.AreEqual("RDCore.Test.Pipe", launch.PipeName);
        Assert.AreEqual(_root, launch.Environment[RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable]);
        Assert.AreEqual("0.1", launch.Manifest.PlatformVersion);
        Assert.AreEqual("RDCore.CLI/rdc.exe", launch.Manifest.HostService);
    }

    [TestMethod]
    public void AManifestPath_WithBackslashes_IsFoundAsWell()
    {
        // a backslash, escaped in the JSON as PlatformPublish.ps1 would write one.
        WithManifest(Manifest($@"RDCore.LanguageServer\\{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options());

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), launch.ExecutablePath);
    }

    [TestMethod]
    public void WithNoPipeName_ANewOneIsMade_EachTime()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var first = RDCoreLanguageServerLaunch.Prepare(Options());
        var second = RDCoreLanguageServerLaunch.Prepare(Options());

        StringAssert.StartsWith(first.PipeName, "RDCore.LanguageServer.Pipe.");
        Assert.AreNotEqual(first.PipeName, second.PipeName);
        Assert.AreEqual(first.PipeName, first.Arguments[1]);
    }

    [TestMethod]
    public void TheClientsProcess_IsPassedWhenThereIsOne()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "p", clientProcessId: 4242));

        CollectionAssert.AreEqual(new[] { "--pipe-name", "p", "--workspace", "file:///c:/workspace", "--client-process-id", "4242" }, launch.Arguments.ToArray());
    }

    [TestMethod]
    public void TheProcessStartInfo_IsTheLaunch()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();
        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "p"));

        var info = launch.ToProcessStartInfo();

        Assert.AreEqual(launch.ExecutablePath, info.FileName);
        Assert.AreEqual(launch.WorkingDirectory, info.WorkingDirectory);
        CollectionAssert.AreEqual(launch.Arguments.ToArray(), info.ArgumentList.ToArray());
        Assert.AreEqual(_root, info.Environment[RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable]);
        Assert.IsFalse(info.UseShellExecute);
    }

    [TestMethod]
    public void ARootThatIsNotThere_IsSaid() => Assert.ThrowsExactly<DirectoryNotFoundException>(
        () => RDCoreLanguageServerLaunch.Prepare(Options() with { PlatformRoot = Path.Combine(_root, "nowhere") }));

    [TestMethod]
    public void ARootWithNoManifest_IsSaid() => Assert.ThrowsExactly<FileNotFoundException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));

    [TestMethod]
    public void AManifestThatIsNotOne_IsSaid()
    {
        WithManifest("{ not json");

        Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
    }

    [TestMethod]
    public void AManifestThatNamesNoLanguageServer_IsSaid()
    {
        WithManifest(Manifest(string.Empty));

        Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
    }

    [TestMethod]
    public void ALanguageServerThatIsNotThere_IsSaid()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));

        var thrown = Assert.ThrowsExactly<FileNotFoundException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), thrown.FileName);
    }
}

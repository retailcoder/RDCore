using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using NSubstitute;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Annotations;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00419</c>: an annotation no installed extension declares; <c>RDC00404</c>, <c>RDC00420</c> to <c>RDC00423</c>: a core annotation written where its
/// declaration does not allow it, twice, in a kind of module it does not apply to, or with too few or too many arguments. Each program's lines are separated
/// by <c>|</c>.
/// </summary>
[TestClass]
public sealed class AnnotationDeclarationsAnalyzerTests
{
    private static readonly Uri Document = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    // an annotation no core annotation is called, which only the fixture extension declares.
    private const string ExtensionAnnotation = "FixtureRoute";

    private static readonly AnnotationDeclaration Declared = new()
    {
        Name = ExtensionAnnotation,
        Targets = AnnotationTargets.Member,
        Parameters = [new AnnotationParameter { Name = "Path", Takes = AnnotationArgumentKind.Literal }],
    };

    // blocks exist only in a file that may have statements outside its procedures; a file's own module has no kind without the host's semantics.
    // the installed annotations are the core's alone, unless the test installs extensions.
    private static AnalyzerFinding[] Findings(string program, bool fileLevelStatements = false, AnnotationCatalogue? installed = null)
    {
        var parser = new ModuleParser(workspaceOptions: Options.Create(new SdkWorkspaceOptions { FileLevelStatements = fileLevelStatements }));
        var parse = parser.Parse(Document, program.Replace("|", "\r\n") + "\r\n");
        Assert.IsEmpty(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var analyzer = new AnnotationDeclarationsAnalyzer(installed ?? AnnotationDeclarationsAnalyzer.Installed([]));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(Document, parse, null))];
    }

    private static ExtensionInfo Extension(string title, params AnnotationDeclaration[] annotations) => new()
    {
        Name = $"{title}.exe",
        Title = title,
        Version = new Version(1, 0),
        Publisher = "Fixture",
        PublisherWebUrl = "https://example.com",
        Description = "A fixture extension.",
        FileHash = string.Empty,
        Capabilities = [],
        SdkVersion = ExtensionManifestValidationService.SdkVersion,
        Annotations = annotations,
    };

    [TestMethod]
    [DataRow("'@Folder \"Parent.Child\"|Option Explicit|Sub Probe()|End Sub", DisplayName = "a module annotation at the top")]
    [DataRow("Option Explicit|'@Description \"Does it.\"|Sub Probe()|End Sub", DisplayName = "a member annotation above the first procedure")]
    [DataRow("Sub First()|End Sub|'@Obsolete|Sub Probe()|End Sub", DisplayName = "a member annotation above another procedure")]
    [DataRow("'@VariableDescription \"The count.\"|Private count As Long", DisplayName = "a variable annotation above a variable")]
    [DataRow("'@Ignore RDC00101, RDC00102|'@Ignore RDC00103|Option Explicit", DisplayName = "an annotation that repeats, written twice")]
    public void AnAnnotationItsDeclarationAllows_IsNotReported(string program)
        => Assert.IsEmpty(Findings(program));

    [TestMethod]
    public void AnAnnotationAnInstalledExtensionDeclares_IsNotReported()
    {
        var program = $"'@{ExtensionAnnotation} \"/orders\"|Sub Probe()|End Sub";
        Assert.AreEqual(
            RDCoreDiagnosticId.UnrecognizedAnnotation, Assert.ContainsSingle(Findings(program)).Id, "premise: the core alone does not declare the annotation");

        Assert.IsEmpty(Findings(program, installed: AnnotationDeclarationsAnalyzer.Installed([Extension("Fixture.Web", Declared)])));
    }

    [TestMethod]
    public void AnAnnotationNoInstalledExtensionDeclares_IsUnrecognized_AndNamedInTheMessage()
    {
        var installed = AnnotationDeclarationsAnalyzer.Installed([Extension("Fixture.Other")]);

        var finding = Assert.ContainsSingle(Findings($"Sub First()|End Sub|'@{ExtensionAnnotation} \"/orders\"|Sub Probe()|End Sub", installed: installed));

        Assert.AreEqual(RDCoreDiagnosticId.UnrecognizedAnnotation, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(2, finding.Range.Start.Line);
        StringAssert.Contains(finding.Message, ExtensionAnnotation);
    }

    [TestMethod]
    [Ignore("needs the platform-level extension allow list (platform-settings.json) in the SDK and Protocols packs this branch builds against")]
    public void AnAnnotationOfAnExtensionOnlyThePlatformSettingsAllow_IsNotReported()
    {
        const string title = "Fixture.Web";
        var root = Path.Combine(Path.GetTempPath(), "platform");
        var extension = Extension(title, Declared);
        var fileSystem = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        fileSystem.AddFile(Path.Combine(root, "Extensions", title, extension.Name), new([]));
        fileSystem.AddFile(Path.Combine(root, "Extensions", title, "extension.manifest.json"), new(System.Text.Json.JsonSerializer.Serialize(extension)));
        fileSystem.AddFile(Path.Combine(root, "platform-settings.json"), new($"{{ \"Extensions\": {{ \"Allowed\": [\"{title}\"] }} }}"));
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.Root.Returns(root);
        environment.Resolve(Arg.Any<string>()).Returns(call => Path.Combine(root, call.Arg<string>()));

        // the app's own settings allow the core extension alone: the platform's settings allow the fixture.
        var options = Options.Create(new SdkAppOptions { Server = new() { UnsafeDevMode = true } });
        Assert.DoesNotContain(title, options.Value.Platform.Extensions.Allowed, "premise: only the platform settings allow the extension");
        var client = new ExtensionsClient(
            options,
            new ExtensionManifestValidationService(options, fileSystem, environment),
            fileSystem,
            environment,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExtensionsClient>.Instance);

        var installed = AnnotationDeclarationsAnalyzer.Installed(client.Discover());

        Assert.IsEmpty(Findings($"'@{ExtensionAnnotation} \"/orders\"|Sub Probe()|End Sub", installed: installed));
    }

    [TestMethod]
    [DataRow("Sub First()|End Sub|'@Folder \"Parent\"|Sub Probe()|End Sub", 2, DisplayName = "a module annotation above a procedure")]
    [DataRow("'@Obsolete|Option Explicit|Sub Probe()|End Sub", 0, DisplayName = "a member annotation that annotates the module")]
    [DataRow("Sub Probe()|    '@Description \"In the body.\"|End Sub", 1, DisplayName = "an annotation in a procedure's body")]
    public void AnAnnotationWrittenWhereItAnnotatesNothingItsDeclarationAllows_IsAnInvalidAnnotation(string program, int line)
    {
        var finding = Assert.ContainsSingle(Findings(program));

        Assert.AreEqual(RDCoreDiagnosticId.InvalidAnnotation, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(line, finding.Range.Start.Line);
    }

    [TestMethod]
    public void AnAnnotationThatDoesNotRepeat_WrittenAgainForItsTarget_IsDuplicated()
    {
        var finding = Assert.ContainsSingle(Findings("'@Folder \"A\"|'@Folder \"B\"|Option Explicit"));

        Assert.AreEqual(RDCoreDiagnosticId.DuplicatedAnnotation, finding.Id);
        Assert.AreEqual(1, finding.Range.Start.Line);
    }

    [TestMethod]
    public void TheSameAnnotation_AboveTwoProcedures_IsNoDuplicate()
        => Assert.IsEmpty(Findings("Sub First()|End Sub|'@Description \"One.\"|Sub Second()|End Sub|'@Description \"Two.\"|Sub Third()|End Sub"));

    [TestMethod]
    [DataRow("'@Interface|Module Helpers|Sub Probe()|End Sub|End Module", DisplayName = "a class's annotation above a standard block")]
    [DataRow("Class Helpers|'@ExcelHotkey \"A\"|Sub Probe()|End Sub|End Class", DisplayName = "a standard module's annotation in a class block")]
    public void AnAnnotationInAKindOfModuleItsDeclarationExcludes_IsReported(string program)
        => Assert.AreEqual(RDCoreDiagnosticId.AnnotationInIncompatibleComponentType, Assert.ContainsSingle(Findings(program, fileLevelStatements: true)).Id);

    [TestMethod]
    public void AnAnnotationAboveABlockOfAKindItAppliesTo_IsNotReported()
        => Assert.IsEmpty(Findings("'@Interface|Class Helpers|Sub Probe()|End Sub|End Class", fileLevelStatements: true));

    [TestMethod]
    public void AFileModuleWithNoSemantics_HasNoKind_AndIsNotReportedForIt()
        => Assert.IsEmpty(Findings("'@Interface|Option Explicit"));

    [TestMethod]
    [DataRow("'@Folder", "Path", DisplayName = "no argument for a required one")]
    [DataRow("'@Ignore", "Codes", DisplayName = "no argument for a required list")]
    public void AnAnnotationWithTooFewArguments_IsMissingOne(string annotation, string parameter)
    {
        var finding = Assert.ContainsSingle(Findings(annotation + "|Option Explicit"));

        Assert.AreEqual(RDCoreDiagnosticId.MissingAnnotationArgument, finding.Id);
        StringAssert.Contains(finding.Message, parameter);
    }

    [TestMethod]
    [DataRow("'@Folder \"A\", \"B\"", "\"B\"", DisplayName = "an argument after the last parameter")]
    [DataRow("'@NoIndent \"A\"", "\"A\"", DisplayName = "an argument of an annotation with no parameter")]
    public void AnAnnotationWithTooManyArguments_IsReportedAtTheOnesItDoesNotTake(string annotation, string superfluous)
    {
        var finding = Assert.ContainsSingle(Findings(annotation + "|Option Explicit"));

        Assert.AreEqual(RDCoreDiagnosticId.SuperfluousAnnotationArgument, finding.Id);
        Assert.AreEqual(annotation.IndexOf(superfluous, StringComparison.Ordinal), finding.Range.Start.Character);
        Assert.AreEqual(annotation.Length, finding.Range.End.Character);
    }
}

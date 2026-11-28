using NSubstitute;
using RDCore.Runtime.Composer;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using static RDCore.Tests.Composer.ComposedProject;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A module loaded into the image of a session (<see cref="ModuleLoader"/>): checked, with the semantic model of its code, or, as code known to have nothing
/// wrong with it is, to run and for nothing else (<see cref="ModuleLoader.Checks"/>).
/// </summary>
[TestClass]
public sealed class ModuleLoaderTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateFolder() => _root = NewFolder();

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_root, recursive: true);

    // the session the composed project was defined in, its Program module, and the parse that module's code was loaded from.
    private async Task<(EnvironmentSessionProvider Provider, VBModuleSymbol Module, ModuleParseResult Parse)> ComposedProgramAsync()
    {
        var composed = await ProgramComposer.ComposeImageAsync(Write(_root), options: null, default);
        var code = composed.Image.Definitions.Single(definition => definition.ModuleName == "Program" && definition.ParseResultJson.Length > 0);
        Assert.IsTrue(composed.Loaded.Session.Symbols.TryGetModule("Program", out var module));
        return (composed.Loaded, module, PlatformJson.Deserialize<ModuleParseResult>(code.ParseResultJson)!);
    }

    private static ModuleLoader Loader(EnvironmentSessionProvider provider, ProgramImage image, bool checks)
        => new(provider.Session, image, Substitute.For<IErrorDetailsRecorder>()) { Checks = checks };

    // what an unchecked load of a module leaves out is what checking its code finds out: the code it loads is the code a checked load does.
    [TestMethod]
    public async Task AModuleLoadedUnchecked_HasTheCodeOfACheckedLoad_AndNoSemanticModel()
    {
        var (provider, module, parse) = await ComposedProgramAsync();
        using var owned = provider;
        var (checkedImage, uncheckedImage) = (new ProgramImage(), new ProgramImage());

        Assert.IsEmpty(Loader(provider, checkedImage, checks: true).Load(module, parse));
        Assert.IsEmpty(Loader(provider, uncheckedImage, checks: false).Load(module, parse));
        await checkedImage.Semantics.WhenAllEvaluatedAsync();

        Assert.IsTrue(checkedImage.Semantics.TryGet(module.Uri, out _), "a checked load keeps the semantic model of the module");
        Assert.IsFalse(uncheckedImage.Semantics.TryGet(module.Uri, out _));
        Assert.IsTrue(uncheckedImage.IsLoaded(module.Uri));
        Assert.IsNotEmpty(checkedImage.ProcedureFingerprints(), "the module has procedures to compare");
        CollectionAssert.AreEquivalent(checkedImage.ProcedureFingerprints().ToArray(), uncheckedImage.ProcedureFingerprints().ToArray());
    }

    // what the parser could not read is no code: a module whose source has a syntax error is not loaded, whether its code is checked or not.
    [TestMethod]
    [DataRow(true, DisplayName = "checked")]
    [DataRow(false, DisplayName = "unchecked")]
    public async Task AModuleWithASyntaxError_IsNotLoaded(bool checks)
    {
        var (provider, module, _) = await ComposedProgramAsync();
        using var owned = provider;
        var broken = new RDCore.Parsing.ModuleParser().Parse(new Uri(Path.Combine(_root, "Program.bas")),
            Program.Replace("Count = Count + 1", "Count = = 1", StringComparison.Ordinal));
        Assert.IsNotEmpty(broken.SyntaxErrors, "the source has a syntax error");
        Assert.IsNotNull(broken.SyntaxTree, "the parser read the rest of it");
        var image = new ProgramImage();

        Assert.IsNotEmpty(Loader(provider, image, checks).Load(module, broken));

        Assert.IsFalse(image.IsLoaded(module.Uri));
    }
}

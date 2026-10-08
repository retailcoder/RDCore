using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the session's memory looks like after each of several runs of the same program, the way the shell runs one: the program is
/// defined and run again, in the same session, every time.
/// </summary>
[TestClass]
public sealed class SessionMemoryAcrossRunsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-memory-ws");

    private static readonly (int Number, string Statement)[] FizzBuzz =
    [
        (10, "N = 30 'TODO INPUT"),
        (20, "FOR I = 1 TO N"),
        (30, "IF I MOD 15 = 0 THEN"),
        (40, "PRINT \"FIZZBUZZ!\""),
        (50, "ELSEIF I MOD 5 = 0 THEN"),
        (60, "PRINT \"BUZZ\""),
        (70, "ELSEIF I MOD 3 = 0 THEN"),
        (80, "PRINT \"FIZZ\""),
        (90, "ELSE"),
        (100, "PRINT I"),
        (110, "END IF"),
        (120, "NEXT"),
    ];

    private static readonly (int Number, string Statement)[] Test =
    [
        (10, "DIM A AS LONG"),
        (20, "A = 42 * 523"),
        (30, "PRINT A"),
    ];

    private static string SourceOf((int Number, string Statement)[] lines)
    {
        var program = new ReplProgram();
        foreach (var (number, statement) in lines)
        {
            program.Store(number, statement);
        }

        return program.ToModuleSource();
    }

    private static async Task<List<SessionMemoryInfo>> RunManyAsync((int Number, string Statement)[] lines, int runs)
    {
        var moduleName = ReplProgram.ModuleName;
        var project = new ProjectFile(Root, new RDCoreProject { Name = moduleName, Modules = [new RDCoreModule { RelativeUri = $"{moduleName}.bas" }] });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) });
        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, SourceLanguage: SupportedLanguages.BASIC), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri;

        var source = SourceOf(lines);
        var infos = new List<SessionMemoryInfo> { sessionProvider.Session.Memory.Info };

        for (var run = 0; run < runs; run++)
        {
            var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{moduleName}.bas")), source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

            var resolver = WorkspaceSymbolResolver.Compose(
                workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver(), implicitScope: SupportedLanguages.BASIC.ImplicitDeclarationScope);
            var symbols = new SyntaxTreeSymbolProvider(
                workspaceRoot, moduleUri, ModuleType.StdModule, parse, resolver, withImplicitDeclarations: true, SupportedLanguages.BASIC.ImplicitDeclarationScope).ProvideSymbols();

            await new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
                .Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = workspaceRoot,
                    ModuleUri = moduleUri,
                    ModuleName = moduleName,
                    Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                    Directives = parse.SyntaxTree.GetModuleDirectives(),
                    Replace = true,
                }, CancellationToken.None);

            var result = await new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
                .Handle(new HostExecuteParams
                {
                    Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
                    ModuleName = moduleName,
                    EntryPoint = ReplProgram.EntryPointName,
                }, CancellationToken.None);
            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage + string.Join("; ", result.Diagnostics) + Environment.NewLine + source);

            infos.Add(sessionProvider.Session.Memory.Info);
        }

        return infos;
    }

    private static string Table(List<SessionMemoryInfo> infos)
        => string.Join(Environment.NewLine, infos.Select((info, run) =>
            $"run {run}: reserved={info.ReservedSegmentBytes} allocated={info.AllocatedBytes} committed={info.CommittedBytes} free={info.FreeBytes} largestFree={info.LargestFreeBlock} available={info.AvailableBytes}"));

    // run 0 is the session before anything ran in it.
    private static void AssertSteady(List<SessionMemoryInfo> infos, int from)
    {
        for (var run = from; run < infos.Count; run++)
        {
            Assert.AreEqual(infos[from], infos[run], $"run {run} left the memory different from run {from}:{Environment.NewLine}{Table(infos)}");
            Assert.AreEqual(0, infos[run].FreeBytes, $"nothing is free once a program has ended and released what it held:{Environment.NewLine}{Table(infos)}");
            Assert.AreEqual(infos[run].AllocatedBytes, infos[run].CommittedBytes, $"nothing is committed that is not allocated:{Environment.NewLine}{Table(infos)}");
        }
    }

    [TestMethod]
    public async Task AProgramThatDeclaresALocal_LeavesTheMemoryAsItFoundIt_RunAfterRun()
    {
        var infos = await RunManyAsync(Test, 8);

        AssertSteady(infos, from: 1);
        Assert.AreEqual(infos[0].AllocatedBytes, infos[1].AllocatedBytes, $"a local does not outlive its activation:{Environment.NewLine}{Table(infos)}");
    }

    [TestMethod]
    public async Task AProgramWithALoop_AllocatesItsVariablesOnce_AndNotAgainOnTheRunsAfter()
    {
        var infos = await RunManyAsync(FizzBuzz, 8);

        // N and I are variables of the session, defined by the first run and kept: they hold their values from one run to the next.
        AssertSteady(infos, from: 1);
        Assert.IsGreaterThan(infos[0].AllocatedBytes, infos[1].AllocatedBytes);
    }
}

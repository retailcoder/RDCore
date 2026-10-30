using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Pipelines;
using System.Text.Json;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Client;

/// <summary>
/// The client logic of the Protocol package (<see cref="RDCorePlatformClient"/>) over OmniSharp's language client (<see cref="OmniSharpPlatformChannel"/>), against a
/// server that serves the platform's methods as the language server does: with OmniSharp, its Newtonsoft serializer, and the SDK's request wrappers.
/// </summary>
[TestClass]
public sealed class RDCorePlatformClientWireTests
{
    private const string LooksLikeADate = "2026-10-30T12:34:56Z";

    private sealed record class Connected(OmniSharpLanguageServer Server, LanguageClient Client, RDCorePlatformClient Platform) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    private static async Task<Connected> ConnectAsync(Action<OmniSharp.Extensions.LanguageServer.Server.LanguageServerOptions>? serve = null)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server =>
        {
            server.WithInput(toServer.Reader).WithOutput(toClient.Writer);
            serve?.Invoke(server);
        });
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer));

        await Task.WhenAll(serving, connecting);
        var languageClient = await connecting;
        return new Connected(await serving, languageClient, new RDCorePlatformClient(new OmniSharpPlatformChannel(languageClient)));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheHandshake_IsAnsweredByTheServersHandler()
    {
        PlatformInitializeRequest? received = null;
        using var connected = await ConnectAsync(server => server.OnRequest<PlatformInitializeRequest, PlatformInitializeResult>(RDCorePlatformHandshake.Method, (request, _) =>
        {
            received = request;
            return Task.FromResult(new PlatformInitializeResult { Component = CoreServerComponent.LanguageServer, Provided = [nameof(SessionExecute)] });
        }));

        var result = await connected.Platform.InitializeAsync(
            CoreServerComponent.LanguageServer, new CorePlatformClientCapabilities { LanguageServer = new() { SessionExecute = new SessionExecute(true) } }, CancellationToken.None);

        Assert.AreEqual(CoreServerComponent.LanguageServer, result.Component);
        CollectionAssert.AreEqual(new[] { nameof(SessionExecute) }, result.Provided.ToArray());
        Assert.IsNotNull(received);
        Assert.AreEqual(CoreServerComponent.LanguageServer, received.ExpectedComponent);
        Assert.IsTrue(received.Expected.LanguageServer!.SessionExecute.IsSupported);
        Assert.IsNull(received.Expected.Parsing, "a group the client expects nothing of is not sent");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ASessionRequest_ReachesTheServer_AndItsResultComesBackAsTheServerWroteIt()
    {
        using var connected = await ConnectAsync(server => server.OnRequest<ExecuteSessionRequest, ExecuteSessionResult>(RDCorePlatformProtocol.SessionExecute, (request, _) =>
            Task.FromResult(new ExecuteSessionResult
            {
                Outcome = ExecutionOutcome.Suspended,
                Output = [request.Source, LooksLikeADate],
                StackTrace = [new ExecuteStackFrame(request.EntryPoint, 3, 4, [7])],
            })));

        var result = await connected.Platform.ExecuteAsync(new ExecuteSessionParams { Source = "Debug.Print 1", ModuleName = "Module1", EntryPoint = "Main", Debug = true }, CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Suspended, result.Outcome);
        CollectionAssert.AreEqual(new[] { "Debug.Print 1", LooksLikeADate }, result.Output.ToArray(), "a string is the string it was, even one that looks like a date");
        Assert.AreEqual("Main", result.StackTrace.Single().Procedure);
        CollectionAssert.AreEqual(new[] { 7 }, result.StackTrace.Single().ReturnLines.ToArray());
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ADebugRequest_ReachesTheServer_WithItsEnumeration()
    {
        StepKind? received = null;
        using var connected = await ConnectAsync(server => server.OnRequest<SessionDebugResumeRequest, ExecuteSessionResult>(RDCorePlatformProtocol.SessionDebugResume, (request, _) =>
        {
            received = request.Step;
            return Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });
        }));

        var result = await connected.Platform.DebugResumeAsync(new SessionDebugResumeParams { Step = StepKind.Out }, CancellationToken.None);

        Assert.AreEqual(StepKind.Out, received);
        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheServersMessageBox_IsShownByTheClientsHandler_UntilItIsDisposed()
    {
        using var connected = await ConnectAsync();
        SessionMessageBoxParams? shown = null;
        var registration = connected.Platform.OnMessageBox((request, _) =>
        {
            shown = request;
            return Task.FromResult(new MessageBoxResult { Handled = true, Result = 6 });
        });

        var answer = await connected.Server.SendRequest(new SessionMessageBoxRequest { Prompt = "Continue?", Title = "VBAProject", Style = 4, Output = ["before"] }, CancellationToken.None);

        Assert.IsTrue(answer.Handled);
        Assert.AreEqual(6, answer.Result);
        Assert.AreEqual("Continue?", shown?.Prompt);
        Assert.AreEqual(4, shown?.Style);
        CollectionAssert.AreEqual(new[] { "before" }, shown?.Output.ToArray());

        registration.Dispose();
        await Assert.ThrowsAsync<Exception>(
            () => connected.Server.SendRequest(new SessionMessageBoxRequest { Prompt = "Again?" }, CancellationToken.None),
            "a client that stopped answering is not asked any more");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheServersInputBox_IsAnsweredByTheClientsHandler()
    {
        using var connected = await ConnectAsync();
        using var registration = connected.Platform.OnInputBox((request, _) => Task.FromResult(new InputBoxResult { Handled = true, Value = request.Default + "!" }));

        var answer = await connected.Server.SendRequest(new SessionInputBoxRequest { Prompt = "Name?", Default = "Rick", XPos = 10 }, CancellationToken.None);

        Assert.IsTrue(answer.Handled);
        Assert.IsFalse(answer.Cancelled);
        Assert.AreEqual("Rick!", answer.Value);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheIndenterSettingsRequests_AreAnsweredByTheClientsHandlers_AsTheLanguageServerReadsThem()
    {
        using var connected = await ConnectAsync();
        RubberduckIndenterSettingsApplyParams? applied = null;
        using var reading = connected.Platform.OnRubberduckIndenterSettings((_, _) => Task.FromResult(new RubberduckIndenterSettingsResult
        {
            Source = RubberduckIndenterSettingsSource.SmartIndenterRegistry,
            Values = new Dictionary<string, JsonElement> { ["IndentProc"] = JsonSerializer.SerializeToElement("Y"), ["AlignDimCol"] = JsonSerializer.SerializeToElement(15) },
            TabWidth = 4,
        }));
        using var applying = connected.Platform.OnRubberduckIndenterSettingsApply((request, _) =>
        {
            applied = request;
            return Task.FromResult(new RubberduckIndenterSettingsApplyResult { Applied = request.Settings.Count });
        });

        var read = await connected.Server.SendRequest(new RubberduckIndenterSettingsRequest(), CancellationToken.None);
        var answer = await connected.Server.SendRequest(
            new RubberduckIndenterSettingsApplyRequest { Source = RubberduckIndenterSettingsSource.SmartIndenterRegistry, Settings = new() { ["Formatting.IndentSpaces"] = "4" } },
            CancellationToken.None);

        Assert.AreEqual(RubberduckIndenterSettingsSource.SmartIndenterRegistry, read.Source);
        Assert.AreEqual("Y", read.Values["IndentProc"].GetString());
        Assert.AreEqual(15, read.Values["AlignDimCol"].GetInt32());
        Assert.AreEqual(4, read.TabWidth);
        Assert.AreEqual(RubberduckIndenterSettingsSource.SmartIndenterRegistry, applied?.Source);
        Assert.AreEqual("4", applied?.Settings["Formatting.IndentSpaces"]);
        Assert.AreEqual(1, answer.Applied);
    }
}

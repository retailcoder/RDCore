using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RDCore.CLI.Host;
using RDCore.External;
using RDCore.External.Interaction;
using RDCore.External.Settings;
using RDCore.LanguageServer.Runtime;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace RDCore.Tests.Cli;

/// <summary>
/// The dialogs of a program that runs in the environment host travel to the client of the language server - the interactive shell - when it shows dialogs, and are
/// the platform's own when it does not; the settings are the workspace's.
/// </summary>
[TestClass]
public sealed class InteractionRelayTests
{
    private static readonly MessageBoxRequest YesNo = new("Sure?", "Project1", VBMsgBoxStyle.VBYesNo);

    private static (RelayedUserInteraction Relay, IEnvironmentSessionProvider Provider, IUserInteraction Fallback, RuntimeOutputBuffer Output) Relay(IHostInteractionChannel? channel)
    {
        var provider = Substitute.For<IEnvironmentSessionProvider>();
        var router = new RuntimeOutputRouter();
        var output = new RuntimeOutputBuffer();
        router.Target = output;
        provider.Output.Returns(router);
        provider.Interaction.Returns(channel);
        var fallback = Substitute.For<IUserInteraction>();
        fallback.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(VBMsgBoxResult.VBYes);
        fallback.Prompt(Arg.Any<InputBoxRequest>(), Arg.Any<CancellationToken>()).Returns("platform");
        return (new RelayedUserInteraction(provider, fallback), provider, fallback, output);
    }

    [TestMethod]
    public void WithNoConnection_TheDialogIsThePlatforms()
    {
        var (relay, _, fallback, _) = Relay(channel: null);
        Assert.AreEqual(VBMsgBoxResult.VBYes, relay.ShowMessage(YesNo));
        Assert.AreEqual("platform", relay.Prompt(new InputBoxRequest("x", "t")));
        fallback.Received(1).ShowMessage(YesNo, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void AClientThatShowsDialogs_AnswersThem_AndIsShownWhatWasPrintedBefore()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = (int)VBMsgBoxResult.VBNo });
        var (relay, _, fallback, output) = Relay(channel);
        output.Write("before");
        output.WriteLine();
        output.Write("pending");

        Assert.AreEqual(VBMsgBoxResult.VBNo, relay.ShowMessage(YesNo));

        channel.Received().MessageBoxAsync(Arg.Is<HostMessageBoxParams>(request =>
            request.Prompt == "Sure?" && request.Title == "Project1" && request.Style == (int)VBMsgBoxStyle.VBYesNo && request.Output.SequenceEqual(new[] { "before" })),
            Arg.Any<CancellationToken>());
        CollectionAssert.AreEqual(new[] { "pending" }, output.Lines.ToArray(), "what the client was shown is not said again, and an open line is still the run's");
        fallback.DidNotReceiveWithAnyArgs().ShowMessage(default!, default);
    }

    [TestMethod]
    public void AClientThatDoesNotShowDialogs_LeavesThemToThePlatform_AndWhatWasPrintedToTheRun()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(MessageBoxResult.NotHandled);
        var (relay, _, _, output) = Relay(channel);
        output.Write("before");
        output.WriteLine();

        Assert.AreEqual(VBMsgBoxResult.VBYes, relay.ShowMessage(YesNo));
        CollectionAssert.AreEqual(new[] { "before" }, output.Lines.ToArray());
    }

    [TestMethod]
    public void AnAnswerThatIsNoButtonOfTheDialog_IsNotTakenFromTheClient()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = (int)VBMsgBoxResult.VBAbort });
        Assert.AreEqual(VBMsgBoxResult.VBYes, Relay(channel).Relay.ShowMessage(YesNo));
    }

    [TestMethod]
    public void AConnectionThatFails_LeavesTheDialogToThePlatform()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.InputBoxAsync(Arg.Any<HostInputBoxParams>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("lost"));
        Assert.AreEqual("platform", Relay(channel).Relay.Prompt(new InputBoxRequest("x", "t")));
    }

    [TestMethod]
    public void AnInputBoxTheClientCancelled_IsCancelled_AndOneConfirmedEmptyIsEmpty()
    {
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.InputBoxAsync(Arg.Any<HostInputBoxParams>(), Arg.Any<CancellationToken>()).Returns(
            new InputBoxResult { Handled = true, Cancelled = true },
            new InputBoxResult { Handled = true, Value = string.Empty });
        var (relay, _, _, _) = Relay(channel);
        var request = new InputBoxRequest("Name?", "t", "you", 10, 20);

        Assert.IsNull(relay.Prompt(request));
        Assert.AreEqual(string.Empty, relay.Prompt(request));
        channel.Received().InputBoxAsync(Arg.Is<HostInputBoxParams>(sent => sent.Default == "you" && sent.XPos == 10 && sent.YPos == 20), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void ARunThatIsStopped_StopsWaitingForTheClient()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var channel = Substitute.For<IHostInteractionChannel>();
        channel.MessageBoxAsync(Arg.Any<HostMessageBoxParams>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => Relay(channel).Relay.ShowMessage(YesNo, stop.Token));
    }

    // ---- the session's outside world ----

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-relay-ws");

    private static EnvironmentSessionProvider Compose(bool legacyRegistrySettings = false)
    {
        var project = new ProjectFile(Root, new RDCoreProject { Name = "Ducks", Modules = [] });
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) });
        var outside = ExternalWorld.None with { Interaction = Substitute.For<IUserInteraction>() };
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, LegacyRegistrySettings: legacyRegistrySettings), files, NullLogger<EnvironmentSessionProvider>.Instance, outside: outside);
        provider.Compose(project.ProjectInfo, new Uri(Root));
        return provider;
    }

    [TestMethod]
    public void TheSessionsWorld_IsTitledAfterItsProject_AndItsDialogsAreRelayed()
    {
        var provider = Compose();
        Assert.AreEqual("Ducks", provider.Outside.DialogTitle);
        Assert.IsInstanceOfType<RelayedUserInteraction>(provider.Outside.Interaction);
    }

    [TestMethod]
    public void TheSessionsSettings_AreItsWorkspacesAppSettings()
    {
        var settings = (JsonWorkspaceSettings)Compose().Outside.WorkspaceSettings;
        Assert.AreEqual(Path.Combine(Root, "appsettings.json"), settings.PathOf(null));
    }

    [TestMethod]
    public void TheLegacySettings_AreTheWorkspaces_UnlessTheRegistryIsAskedForOnWindows()
    {
        Assert.IsInstanceOfType<JsonProgramSettings>(Compose().Outside.ProgramSettings);
        var asked = Compose(legacyRegistrySettings: true).Outside.ProgramSettings;
        Assert.AreEqual(OperatingSystem.IsWindows() ? "RegistryProgramSettings" : nameof(JsonProgramSettings), asked.GetType().Name);
    }

    [TestMethod]
    public void TheKnob_IsBoundFromAppSettings()
    {
        Assert.IsFalse(RuntimeEnvironmentProfile.From(new SDK.Server.Configuration.SdkEnvironmentOptions()).LegacyRegistrySettings);
        Assert.IsTrue(RuntimeEnvironmentProfile.From(new SDK.Server.Configuration.SdkEnvironmentOptions { LegacyRegistrySettings = true }).LegacyRegistrySettings);
    }

    // ---- the language server ----

    private static IInteractionClient Client(bool showsDialogs)
    {
        var client = Substitute.For<IInteractionClient>();
        client.ShowsDialogs.Returns(showsDialogs);
        client.MessageBoxAsync(Arg.Any<SessionMessageBoxParams>(), Arg.Any<CancellationToken>()).Returns(new MessageBoxResult { Handled = true, Result = 7 });
        client.InputBoxAsync(Arg.Any<SessionInputBoxParams>(), Arg.Any<CancellationToken>()).Returns(new InputBoxResult { Handled = true, Value = "typed" });
        return client;
    }

    [TestMethod]
    public async Task TheLanguageServer_RelaysADialog_ToAClientThatShowsThem()
    {
        var relay = new ClientInteractionRelay();
        var client = Client(showsDialogs: true);
        relay.Attach(client);

        var answer = await new HostMessageBoxHandler(relay).Handle(new HostMessageBoxParams { Prompt = "p", Title = "t", Style = 4, Output = ["line"] }, CancellationToken.None);
        var typed = await new HostInputBoxHandler(relay).Handle(new HostInputBoxParams { Prompt = "p", Default = "d", XPos = 1 }, CancellationToken.None);

        Assert.AreEqual(7, answer.Result);
        Assert.AreEqual("typed", typed.Value);
        await client.Received().MessageBoxAsync(Arg.Is<SessionMessageBoxParams>(sent => sent.Prompt == "p" && sent.Title == "t" && sent.Style == 4 && sent.Output.Single() == "line"), Arg.Any<CancellationToken>());
        await client.Received().InputBoxAsync(Arg.Is<SessionInputBoxParams>(sent => sent.Default == "d" && sent.XPos == 1), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheLanguageServer_AnswersThatNobodyWasAsked_WhenItsClientDoesNotShowDialogs_OrThereIsNone()
    {
        var relay = new ClientInteractionRelay();
        Assert.IsFalse((await relay.MessageBoxAsync(new HostMessageBoxParams(), CancellationToken.None)).Handled);

        var client = Client(showsDialogs: false);
        relay.Attach(client);
        Assert.IsFalse((await relay.MessageBoxAsync(new HostMessageBoxParams(), CancellationToken.None)).Handled);
        Assert.IsFalse((await relay.InputBoxAsync(new HostInputBoxParams(), CancellationToken.None)).Handled);
        await client.DidNotReceiveWithAnyArgs().MessageBoxAsync(default!, default);
    }
}

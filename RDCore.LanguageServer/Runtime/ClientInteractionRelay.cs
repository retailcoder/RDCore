using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// The client of the language server, as the dialogs of a program reach it: whether it shows them, and how it is asked to.
/// </summary>
internal interface IInteractionClient
{
    /// <summary>Whether the client said it answers a program's dialogs (<see cref="SDK.Client.SessionInteraction"/>).</summary>
    bool ShowsDialogs { get; }

    /// <summary>Asks the client to answer a <c>MsgBox</c> (<c>rdcore/session/interaction/msgBox</c>).</summary>
    Task<MessageBoxResult> MessageBoxAsync(SessionMessageBoxParams request, CancellationToken token);

    /// <summary>Asks the client to answer an <c>InputBox</c> (<c>rdcore/session/interaction/inputBox</c>).</summary>
    Task<InputBoxResult> InputBoxAsync(SessionInputBoxParams request, CancellationToken token);
}

/// <summary>
/// Relays the dialogs of the program that runs in the environment host to the client of the language server, when the client shows dialogs.
/// </summary>
/// <remarks>
/// The host asks whatever its language server's client is; a client that did not expect <see cref="SDK.Client.SessionInteraction"/> - or no client yet - is never
/// asked, and the answer is that nobody was (<see cref="MessageBoxResult.NotHandled"/>), so that the host shows the dialog of the platform it runs on.
/// </remarks>
internal interface IClientInteractionRelay
{
    /// <summary>Attaches the client, once the connection to it is live.</summary>
    void Attach(IInteractionClient client);

    /// <summary>Relays a <c>MsgBox</c> from the host.</summary>
    Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token);

    /// <summary>Relays an <c>InputBox</c> from the host.</summary>
    Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token);
}

internal sealed class ClientInteractionRelay : IClientInteractionRelay
{
    private IInteractionClient? _client;

    public void Attach(IInteractionClient client) => _client = client;

    public async Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token)
        => _client is { ShowsDialogs: true } client
            ? await client.MessageBoxAsync(
                new SessionMessageBoxParams { Prompt = request.Prompt, Title = request.Title, Style = request.Style, Output = request.Output }, token)
            : MessageBoxResult.NotHandled;

    public async Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token)
        => _client is { ShowsDialogs: true } client
            ? await client.InputBoxAsync(
                new SessionInputBoxParams
                {
                    Prompt = request.Prompt, Title = request.Title, Default = request.Default, XPos = request.XPos, YPos = request.YPos, Output = request.Output,
                }, token)
            : InputBoxResult.NotHandled;
}

/// <summary>
/// The client of a running language server: what it expects is what it said in the platform handshake.
/// </summary>
/// <param name="server">The language server, connected to its client.</param>
internal sealed class LanguageServerInteractionClient(ILanguageServer server) : IInteractionClient
{
    public bool ShowsDialogs => server.Services.GetService<IPlatformClientCapabilitiesService>()?.Expects(capabilities => capabilities.SessionInteraction) == true;

    public Task<MessageBoxResult> MessageBoxAsync(SessionMessageBoxParams request, CancellationToken token) => server.SendRequest(request, token);

    public Task<InputBoxResult> InputBoxAsync(SessionInputBoxParams request, CancellationToken token) => server.SendRequest(request, token);
}

/// <summary>
/// Handles <c>rdcore/host/interaction/msgBox</c>: the program that runs in the environment host opened a <c>MsgBox</c>.
/// </summary>
internal sealed class HostMessageBoxHandler(IClientInteractionRelay relay) : IJsonRpcRequestHandler<HostMessageBoxParams, MessageBoxResult>
{
    public Task<MessageBoxResult> Handle(HostMessageBoxParams request, CancellationToken cancellationToken) => relay.MessageBoxAsync(request, cancellationToken);
}

/// <summary>
/// Handles <c>rdcore/host/interaction/inputBox</c>: the program that runs in the environment host opened an <c>InputBox</c>.
/// </summary>
internal sealed class HostInputBoxHandler(IClientInteractionRelay relay) : IJsonRpcRequestHandler<HostInputBoxParams, InputBoxResult>
{
    public Task<InputBoxResult> Handle(HostInputBoxParams request, CancellationToken cancellationToken) => relay.InputBoxAsync(request, cancellationToken);
}

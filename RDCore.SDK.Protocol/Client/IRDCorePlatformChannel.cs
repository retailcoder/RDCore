using System.Text.Json;

namespace RDCore.SDK.Client;

/// <summary>
/// A JSON-RPC connection to the RDCore language server, as far as its <c>rdcore/*</c> messages go: what a client implements over whatever carries its messages,
/// so that <see cref="RDCorePlatformClient"/> speaks the platform's protocol through it.
/// </summary>
/// <remarks>
/// <para>
/// The channel moves JSON and nothing else: the messages are written and read by the callers, with <see cref="RDCorePlatformJson.Options"/>
/// (<see cref="RDCorePlatformChannelExtensions"/>). An IDE whose own LSP client owns the connection implements it over that client's custom-message API;
/// <c>RDCore.SDK</c> implements it over OmniSharp's language client, which is what <c>rdc.exe</c> speaks through.
/// </para>
/// <para>
/// A request the server answers with a JSON-RPC error faults the returned task with whatever exception the implementation raises for one; the channel adds no
/// error model of its own.
/// </para>
/// </remarks>
public interface IRDCorePlatformChannel
{
    /// <summary>
    /// Sends a request to the server and returns its result.
    /// </summary>
    /// <param name="method">The JSON-RPC method: one of <see cref="RDCorePlatformProtocol"/>.</param>
    /// <param name="parameters">The <c>params</c> of the request; <see langword="null"/> to send none.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The <c>result</c> of the response; <see langword="null"/> when it is JSON <c>null</c>.</returns>
    Task<JsonElement?> SendRequestAsync(string method, JsonElement? parameters, CancellationToken token);

    /// <summary>
    /// Sends a notification to the server.
    /// </summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">The <c>params</c> of the notification; <see langword="null"/> to send none.</param>
    /// <param name="token">Cancels before the notification is sent.</param>
    Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken token);

    /// <summary>
    /// Answers the requests of a method the server sends the client, until the returned registration is disposed.
    /// </summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="handler">Answers a request: given its <c>params</c> (<see langword="null"/> when it has none), returns its <c>result</c>.</param>
    /// <returns>The registration; disposing it stops answering the method.</returns>
    IDisposable OnRequest(string method, Func<JsonElement?, CancellationToken, Task<JsonElement?>> handler);

    /// <summary>
    /// Handles the notifications of a method the server sends the client, until the returned registration is disposed.
    /// </summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="handler">Handles a notification, given its <c>params</c> (<see langword="null"/> when it has none).</param>
    /// <returns>The registration; disposing it stops handling the method.</returns>
    IDisposable OnNotification(string method, Func<JsonElement?, CancellationToken, Task> handler);
}

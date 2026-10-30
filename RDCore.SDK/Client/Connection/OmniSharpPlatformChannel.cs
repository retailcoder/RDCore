using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using System.Text.Json;

namespace RDCore.SDK.Client.Connection;

/// <summary>
/// The <see cref="IRDCorePlatformChannel"/> of an OmniSharp language client: what <c>rdc.exe</c> and every <see cref="RDCoreClientApp"/> speak the platform's
/// protocol through, so that they run the same client logic (<see cref="RDCorePlatformClient"/>) as a client that has only <c>RDCore.SDK.Protocol</c>.
/// </summary>
/// <remarks>
/// The JSON is handed to OmniSharp as written by <see cref="RDCorePlatformJson.Options"/>, and handed back as OmniSharp read it: Newtonsoft only carries it, so the
/// wire is the Protocol package's whatever OmniSharp's serializer would have made of the types.
/// </remarks>
/// <param name="client">The language client, connected.</param>
/// <param name="logger">Logs each request and how it ended.</param>
public sealed class OmniSharpPlatformChannel(ILanguageClient client, ILogger? logger = null) : IRDCorePlatformChannel
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    // LSP 3.17 ErrorCodes: the request was valid and refused; the content it was about changed while it was answered.
    private const int RequestFailedCode = -32803;
    private const int ContentModifiedCode = -32801;

    /// <inheritdoc/>
    public async Task<JsonElement?> SendRequestAsync(string method, JsonElement? parameters, CancellationToken token)
    {
        _logger.LogInformation("→ {method}", method);
        try
        {
            var result = parameters is { } json
                ? await client.SendRequest(method, ToToken(json)).Returning<JToken?>(token)
                : await client.SendRequest(method).Returning<JToken?>(token);
            _logger.LogInformation("← {method} completed", method);
            return ToElement(result);
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "← {method} cancelled", method);
            throw;
        }
        catch (OmniSharp.Extensions.JsonRpc.Server.JsonRpcException exception) when (exception.ErrorCode is RequestFailedCode or ContentModifiedCode)
        {
            // answered, not faulted: the server refused the request, or an edit overtook it.
            _logger.LogDebug("← {method} answered {code}: {message}", method, exception.ErrorCode, exception.Message);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "← {method} faulted", method);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (parameters is { } json)
        {
            client.SendNotification(method, ToToken(json));
        }
        else
        {
            client.SendNotification(method);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IDisposable OnRequest(string method, Func<JsonElement?, CancellationToken, Task<JsonElement?>> handler)
        => client.Register(registry => registry.OnJsonRequest(method, async (JToken parameters, CancellationToken token) =>
            await handler(ToElement(parameters), token) is { } result ? ToToken(result) : JValue.CreateNull()));

    /// <inheritdoc/>
    public IDisposable OnNotification(string method, Func<JsonElement?, CancellationToken, Task> handler)
        => client.Register(registry => registry.OnJsonNotification(method, (JToken parameters, CancellationToken token) => handler(ToElement(parameters), token)));

    private static JToken ToToken(JsonElement json)
    {
        // read as written: a string that looks like a date stays the string it is.
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json.GetRawText())) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }

    private static JsonElement? ToElement(JToken? token)
        => token is null || token.Type is JTokenType.Null or JTokenType.Undefined
            ? null
            : JsonSerializer.Deserialize<JsonElement>(token.ToString(Newtonsoft.Json.Formatting.None));
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using System.Collections.Concurrent;

namespace RDCore.LanguageServer.Parsing;

/// <summary>
/// Sends <c>rdcore/parser/document</c> requests to the parsing server and caches the returned
/// <see cref="ModuleParseResult"/> per document. This is the language server's only entry point to
/// the parser transport.
/// </summary>
internal interface IParsingClientService
{
    /// <summary>
    /// Parses one workspace document, waiting for the parsing server to be ready first, and caches the result.
    /// </summary>
    /// <remarks>
    /// Sends the document's current in-memory text (not necessarily what is saved to disk) as the
    /// request's <c>Fragment</c> — the parser never reads from the filesystem. A URI with no loaded
    /// <see cref="WorkspaceDocument"/> degrades to a failed <see cref="ModuleParseResult"/> rather than
    /// contacting the parser.
    /// </remarks>
    Task<ModuleParseResult> ParseDocumentAsync(Uri documentUri, CancellationToken token);
    Task<ModuleParseResult> ParseFragmentAsync(SourceLocation location, string content, CancellationToken token);

    /// <summary>
    /// Parses every currently-loaded workspace source document. Failures are logged, not thrown.
    /// </summary>
    Task ParseWorkspaceAsync(CancellationToken token);

    /// <summary>
    /// Gets the last <see cref="ModuleParseResult"/> cached for <paramref name="documentUri"/>.
    /// </summary>
    bool TryGetCached(Uri documentUri, out ModuleParseResult result);
}

internal sealed class ParsingClientService(
    IOptions<SdkServerOptions> options,
    IPlatformOrchestrationService orchestration,
    IWorkspaceDocumentService documents,
    ILogger<ParsingClientService> logger) : IParsingClientService
{
    private readonly ConcurrentDictionary<Uri, ModuleParseResult> _cache = new();

    public bool TryGetCached(Uri documentUri, out ModuleParseResult result)
        => _cache.TryGetValue(documentUri, out result!);

    private bool ValidateWorkspaceUri(Uri documentUri, out WorkspaceDocument document, out ModuleParseResult failedParseResult)
    {
        if (!documents.TryGetDocument(documentUri, out document))
        {
            var message = "No workspace document is loaded for the specified URI.";
            failedParseResult = ModuleParseResult.Failed(new SourceLocation(documentUri, SourceRange.Empty), message);
            _cache[documentUri] = failedParseResult;
            logger.LogWarning("❌ Parse request skipped: {message} {uri}", message,
                (options.Value?.Verbose ?? false) ? documentUri : string.Empty);
            return false;
        }

        failedParseResult = null!;
        return true;
    }

    public async Task<ModuleParseResult> ParseDocumentAsync(Uri documentUri, CancellationToken token)
    {
        if (!ValidateWorkspaceUri(documentUri, out var document, out var failedParseResult))
        {
            return failedParseResult;
        }

        await orchestration.ParsingService.WaitForReadyAsync(token);

        var envelope = await orchestration.ParsingService.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            new ParseDocumentParams
            {
                DocumentUri = documentUri,
                Fragment = document.Text
            }, token);

        var result = envelope is not null
            ? envelope.Unwrap<ModuleParseResult>()
            : ModuleParseResult.Failed(new SourceLocation(documentUri, SourceRange.Empty), "Parser returned no result");

        _cache[documentUri] = result;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("📄 Parsed {uri}: {status}", documentUri,
                result.IsSuccess ? "✅" : $"❌ {result.SyntaxErrors.Length} syntax error(s)");
        }
        if (!result.IsSuccess && (options.Value?.Verbose ?? false) && logger.IsEnabled(LogLevel.Warning))
        {
            foreach (var error in result.SyntaxErrors)
            {
                logger.LogWarning("   ↳ {detail}", error.Verbose);
            }
        }
        return result;
    }

    public async Task<ModuleParseResult> ParseFragmentAsync(SourceLocation location, string content, CancellationToken token)
    {
        var documentUri = location.Uri;
        if (!ValidateWorkspaceUri(documentUri, out _, out var failedParseResult))
        {
            return failedParseResult;
        }

        await orchestration.ParsingService.WaitForReadyAsync(token);

        var envelope = await orchestration.ParsingService.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            new ParseDocumentParams
            {
                DocumentUri = documentUri,
                Fragment = content,
                AnchorOffset = location.Range.Start
            }, token);

        var result = envelope is not null
            ? envelope.Unwrap<ModuleParseResult>()
            : ModuleParseResult.Failed(location, "Parser returned no result");

        SynchronizeAstFragment(location, result);
        return result;
    }

    private void SynchronizeAstFragment(SourceLocation location, ModuleParseResult result)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        // TODO locate and replace the AST node at the specified location with the fragment AST result.
    }

    public async Task ParseWorkspaceAsync(CancellationToken token)
    {
        try
        {
            await orchestration.ParsingService.WaitForReadyAsync(token);

            var parsed = 0;
            var failed = 0;
            foreach (var document in documents.GetAllDocuments())
            {
                token.ThrowIfCancellationRequested();
                var uri = document.Id.Uri.ToUri();
                try
                {
                    await ParseDocumentAsync(uri, token);
                    parsed++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // a parser failure on one module must not abort the whole workspace parse.
                    failed++;
                    logger.LogError(exception, "❌ Parse failed for {uri}.", uri);
                }
            }

            LogIfEnabled(LogLevel.Information, $"✅ Workspace parse completed ({parsed} ok, {failed} failed)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, "Workspace parse was cancelled; language server is shutting down.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ Workspace parse failed.");
        }
    }

    internal static ModuleType ModuleTypeOf(WorkspaceDocument document)
        => ModuleHeader.IsClassModule(document.Text) ? ModuleType.ClassModule : ModuleType.StdModule;

    private void LogIfEnabled(LogLevel level, string message)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "{message}", message);
        }
    }
}

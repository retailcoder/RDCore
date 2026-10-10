using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// The libraries the workspace's project references, and their symbols.
/// </summary>
internal interface IReferencedLibraryService
{
    /// <summary>
    /// The libraries the project references that were loaded, and the ones that could not be.
    /// </summary>
    ReferencedLibraries Libraries { get; }

    /// <summary>
    /// The symbols of the libraries, addressed under a workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace.</param>
    IReadOnlyList<Symbol> SymbolsUnder(Uri workspaceRoot);
}

/// <inheritdoc cref="IReferencedLibraryService"/>
/// <remarks>
/// The libraries are read when they are first asked for, and again when the references of the project are not the ones they were read for: the files that
/// describe them are the platform's, and do not change while it runs. A library that cannot be loaded is said once, in the log, each time the references
/// change - the names that use its types are what tell the author, and say that a reference may be missing.
/// </remarks>
internal sealed class ReferencedLibraryService(
    IProjectFileService project,
    ILibrarySource source,
    IOptions<SdkAppOptions> options,
    ILogger<ReferencedLibraryService> logger) : IReferencedLibraryService
{
    private readonly object _loading = new();
    private (string Key, ReferencedLibraries Libraries)? _loaded;
    private (Uri Root, IReadOnlyList<Symbol> Symbols)? _symbols;

    public ReferencedLibraries Libraries
    {
        get
        {
            var references = project.Project?.ProjectInfo?.References ?? [];
            var key = string.Join('\n', references.Select(reference => reference.Name));

            lock (_loading)
            {
                if (_loaded is not { } loaded || loaded.Key != key)
                {
                    var libraries = LibrarySymbolProvider.Load(source, references);
                    foreach (var problem in libraries.Problems)
                    {
                        logger.LogWarning("📚 The library '{library}' could not be loaded ({kind}{related}); a type of it is not a type of the project.",
                            problem.Name, problem.Kind, problem.Related.IsEmpty ? string.Empty : ": " + string.Join(" → ", problem.Related));
                    }

                    _loaded = loaded = (key, libraries);
                    _symbols = null;
                }

                return loaded.Libraries;
            }
        }
    }

    public IReadOnlyList<Symbol> SymbolsUnder(Uri workspaceRoot)
    {
        var libraries = Libraries;
        lock (_loading)
        {
            if (_symbols is not { } cached || cached.Root != workspaceRoot)
            {
                _symbols = cached = (workspaceRoot, [.. new LibrarySymbolProvider(workspaceRoot, libraries, options.Value.Environment.Is64Bit).ProvideSymbols()]);
            }

            return cached.Symbols;
        }
    }
}

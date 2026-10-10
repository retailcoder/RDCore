using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// The symbols of the libraries a project references: the ones that are not the standard library, which is the platform's own
/// (<see cref="StdLibSymbolProvider"/>).
/// </summary>
/// <remarks>
/// The language server and the environment host each build their symbols from the same libraries, with this, so that a name the language server bound
/// binds to the same symbol in the host - which is why the symbols are addressed under the workspace and nothing else decides their address.
/// </remarks>
public sealed class LibrarySymbolProvider : ISymbolProvider
{
    private readonly Uri _workspaceRoot;
    private readonly bool _is64Bit;

    /// <summary>
    /// Creates the provider for libraries that are loaded.
    /// </summary>
    /// <param name="workspaceRoot">The workspace the symbols are addressed under.</param>
    /// <param name="libraries">The libraries (<see cref="Load"/>).</param>
    /// <param name="is64Bit">The pointer width of the environment.</param>
    public LibrarySymbolProvider(Uri workspaceRoot, ReferencedLibraries libraries, bool is64Bit = true)
    {
        _workspaceRoot = workspaceRoot;
        _is64Bit = is64Bit;
        Libraries = libraries;
    }

    /// <summary>
    /// The libraries, and the ones that could not be loaded.
    /// </summary>
    public ReferencedLibraries Libraries { get; }

    /// <summary>
    /// Loads the libraries a project references.
    /// </summary>
    /// <param name="source">Where the descriptions are.</param>
    /// <param name="references">The references of the project.</param>
    public static ReferencedLibraries Load(ILibrarySource source, IEnumerable<RDCoreReference> references)
        => ReferencedLibraries.Load(source, references.Select(reference => reference.Name), [StdLibSymbolProvider.LibraryName]);

    /// <inheritdoc/>
    public IEnumerable<Symbol> ProvideSymbols() => new LibrarySymbolReader(_workspaceRoot, _is64Bit).Read(Libraries.Libraries, Libraries.Priorities);
}

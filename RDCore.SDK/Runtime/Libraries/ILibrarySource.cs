using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// Where the descriptions of libraries are found, by the name of the library.
/// </summary>
public interface ILibrarySource
{
    /// <summary>
    /// Gets the description of a library.
    /// </summary>
    /// <param name="name">The name of the library; the case of it does not matter, as it does not in VBA.</param>
    /// <param name="description">The description.</param>
    /// <returns><see langword="false"/> when the source has no library of the name.</returns>
    /// <exception cref="InvalidDataException">The source has one, and it cannot be read.</exception>
    bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description);
}

/// <summary>
/// A source of library descriptions that is a folder of files: <c>Scripting.json</c> describes the library <c>Scripting</c>.
/// </summary>
/// <remarks>
/// It is the <c>Symbols</c> folder of the platform (<see cref="Platform.IPlatformEnvironment.Resolve"/>), where the descriptions that ship with it are deployed.
/// A folder that is not there has no libraries, and is not an error: a platform with none to ship is a platform whose projects reference none.
/// </remarks>
/// <param name="fileSystem">The file system the folder is read from.</param>
/// <param name="directory">The folder.</param>
public sealed class DirectoryLibrarySource(IFileSystem fileSystem, string directory) : ILibrarySource
{
    /// <summary>
    /// The extension of a description file.
    /// </summary>
    public const string Extension = ".json";

    /// <inheritdoc/>
    public bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description)
    {
        description = null;
        if (string.IsNullOrWhiteSpace(name) || !fileSystem.Directory.Exists(directory))
        {
            return false;
        }

        var file = fileSystem.Directory.EnumerateFiles(directory, "*" + Extension)
            .FirstOrDefault(path => string.Equals(fileSystem.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return false;
        }

        try
        {
            description = LibraryJson.Read(fileSystem.File.ReadAllText(file));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{fileSystem.Path.GetFileName(file)}: {exception.Message}", exception);
        }

        // a file is the library its content says it is: one that is named for another is a copy that was not renamed, and is not either.
        return string.Equals(description.Name, name, StringComparison.OrdinalIgnoreCase)
            ? true
            : throw new InvalidDataException($"{fileSystem.Path.GetFileName(file)}: the file describes the library '{description.Name}'.");
    }
}

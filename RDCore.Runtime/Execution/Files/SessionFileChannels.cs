using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions;

namespace RDCore.Runtime.Execution.Files;

/// <inheritdoc cref="IFileChannels"/>
/// <remarks>
/// Over an <see cref="IFileSystem"/>, which the platform already abstracts — so a session composed with a
/// fake one does real VBA file I/O against nothing on disk, which is what a test and a CI run both want.
/// <para>
/// A channel holds an open <see cref="Stream"/> for the lifetime of the association, because that is what
/// VBA's own semantics describe: the file is held open, and the lock the <c>Open</c> asked for is held with
/// it. Closing the channel is what releases both.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system the channels are opened on.</param>
internal sealed class SessionFileChannels(IFileSystem fileSystem) : IFileChannels, IDisposable
{
    private sealed record class Channel(
        int FileNumber,
        string Path,
        VBFileMode Mode,
        VBFileAccessMode Access,
        VBFileLockMode Lock,
        int RecordLength,
        Stream Stream) : IFileChannel;

    private readonly Dictionary<int, Channel> _channels = [];

    /// <inheritdoc/>
    public IEnumerable<IFileChannel> Open => _channels.Values;

    /// <inheritdoc/>
    public bool TryGet(int fileNumber, out IFileChannel? channel)
    {
        var found = _channels.TryGetValue(fileNumber, out var open);
        channel = open;
        return found;
    }

    /// <inheritdoc/>
    public VBRuntimeErrorId? TryOpen(
        int fileNumber, string path, VBFileMode mode, VBFileAccessMode access, VBFileLockMode @lock, int recordLength)
    {
        // "An error (number 55, 'File already open') is generated if the <file-number> integer value already
        // has an external file association" (MS-VBAL 5.4.5.1).
        if (_channels.ContainsKey(fileNumber))
        {
            return VBRuntimeErrorId.FileAlreadyOpen;
        }

        // "If <mode> is Append or Output, the path specification MUST NOT identify an external file that
        // currently has a file number association" - the one cross-channel rule the specification states
        // outright rather than leaving implementation-defined.
        if (mode is VBFileMode.Append or VBFileMode.Output
            && _channels.Values.Any(channel => PathsMatch(channel.Path, path)))
        {
            return VBRuntimeErrorId.FileAlreadyOpen;
        }

        // "If the external file... does not exist, an attempt is made to create the external file unless
        // <mode> is the keyword Input, in which case an error is generated."
        var exists = fileSystem.File.Exists(path);
        if (!exists && mode is VBFileMode.Input)
        {
            return VBRuntimeErrorId.FileNotFound;
        }

        try
        {
            _channels[fileNumber] = new Channel(
                fileNumber, path, mode, access, @lock, recordLength, OpenStream(path, mode, access, @lock));
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // the lock could not be had, or the file refuses the access: "an error (number 70, 'Permission
            // denied') is generated".
            return VBRuntimeErrorId.PermissionDenied;
        }
        catch (IOException)
        {
            // already held by another process is also 70; anything else about the path or the device is
            // "number 75, 'Path/File access error'". IOException covers both, and a sharing violation is the
            // one worth telling apart, since a program can retry it.
            return VBRuntimeErrorId.PathOrFileAccessError;
        }
    }

    /// <inheritdoc/>
    public bool Close(int fileNumber)
    {
        if (!_channels.Remove(fileNumber, out var channel))
        {
            return false;
        }

        channel.Stream.Dispose();
        return true;
    }

    /// <inheritdoc/>
    public int CloseAll()
    {
        var closed = _channels.Count;
        foreach (var channel in _channels.Values)
        {
            channel.Stream.Dispose();
        }

        _channels.Clear();
        return closed;
    }

    /// <summary>
    /// Closes every channel the session still holds.
    /// </summary>
    /// <remarks>
    /// A session ending is not a <c>Close</c> statement, but a file held open by a process that has gone is
    /// worse than one closed without being asked to be.
    /// </remarks>
    public void Dispose() => CloseAll();

    private Stream OpenStream(string path, VBFileMode mode, VBFileAccessMode access, VBFileLockMode @lock)
        => fileSystem.FileStream.New(
            path,
            mode switch
            {
                // Output "truncates" in VBA terms: data can only be written, from the start.
                VBFileMode.Output => FileMode.Create,
                VBFileMode.Append => FileMode.Append,
                VBFileMode.Input => FileMode.Open,
                _ => FileMode.OpenOrCreate,
            },
            access switch
            {
                VBFileAccessMode.Read => FileAccess.Read,
                VBFileAccessMode.Write => FileAccess.Write,
                _ => FileAccess.ReadWrite,
            },
            // the VBA lock says what *others* may still do, which is what FileShare expresses - Shared denies
            // nothing, and each Lock denies that operation to everyone else.
            @lock switch
            {
                VBFileLockMode.Read => FileShare.Write,
                VBFileLockMode.Write => FileShare.Read,
                VBFileLockMode.ReadWrite => FileShare.None,
                _ => FileShare.ReadWrite,
            });

    // the same external file reached by two spellings is the same file. Case-insensitively on Windows, and
    // the platform runs on Linux too, so this compares the way the file system it was given does.
    private bool PathsMatch(string left, string right)
        => fileSystem.Path.GetFullPath(left).Equals(
            fileSystem.Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

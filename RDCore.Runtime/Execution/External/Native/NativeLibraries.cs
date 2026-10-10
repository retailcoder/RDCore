using RDCore.SDK.Model.Errors;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RDCore.Runtime.Execution.External.Native;

/// <summary>
/// The native libraries a session's <c>Declare</c> statements name, and the functions in them (<strong>MS-VBAL §5.2.3.5</strong>).
/// </summary>
/// <remarks>
/// MS-VBAL leaves both to the implementation: the <c>Lib</c> string "is used in an implementation-defined manner to identify a set of available procedures",
/// and the <c>Alias</c>, or the name, to select one of them. This is MS-VBA's: the string is a library the operating system loads by its name, searching where
/// it searches (a name without an extension is a <c>.dll</c> on Windows); an alias that starts with <c>#</c> is an ordinal; anything else is the exported name,
/// and is not guessed at - there is no <c>A</c> or <c>W</c> appended to a name the library does not export. A library is loaded once, and stays loaded.
/// </remarks>
internal sealed class NativeLibraries
{
    private readonly Dictionary<string, IntPtr> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Library, string EntryPoint), IntPtr> _functions = [];

    /// <summary>
    /// Finds the function a <c>Declare</c> names.
    /// </summary>
    /// <param name="library">The <c>Lib</c> string.</param>
    /// <param name="entryPoint">The <c>Alias</c> string, or the name of the procedure when there is none.</param>
    /// <param name="function">The address of the function.</param>
    /// <param name="error">Why it was not found: error 53 when the library is not, and 453 when it has no such function.</param>
    public bool TryResolve(string library, string entryPoint, out IntPtr function, out VBRuntimeErrorId error)
    {
        error = VBRuntimeErrorId.None;
        lock (_functions)
        {
            if (_functions.TryGetValue((library, entryPoint), out function))
            {
                return true;
            }

            if (!_libraries.TryGetValue(library, out var handle))
            {
                if (!NativeLibrary.TryLoad(library, out handle))
                {
                    error = VBRuntimeErrorId.FileNotFound;
                    return false;
                }

                _libraries[library] = handle;
            }

            if (!TryGetFunction(handle, entryPoint, out function))
            {
                error = VBRuntimeErrorId.SpecifiedDllFunctionNotFound;
                return false;
            }

            _functions[(library, entryPoint)] = function;
            return true;
        }
    }

    // MS-VBAL §5.2.3.5: an alias that starts with "#" is an ordinal, an integer from 0 to 32,767; the name of an export otherwise.
    private static bool TryGetFunction(IntPtr library, string entryPoint, out IntPtr function)
    {
        if (entryPoint.StartsWith('#'))
        {
            function = IntPtr.Zero;
            return OperatingSystem.IsWindows()
                && int.TryParse(entryPoint.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal)
                && ordinal is >= 0 and <= short.MaxValue
                && (function = GetProcAddressByOrdinal(library, ordinal)) != IntPtr.Zero;
        }

        return NativeLibrary.TryGetExport(library, entryPoint, out function);
    }

    [DllImport("kernel32", EntryPoint = "GetProcAddress", ExactSpelling = true)]
    private static extern IntPtr GetProcAddressByOrdinal(IntPtr module, nint ordinal);
}

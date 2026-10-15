using System.Collections;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// The environment variables a program reads with <c>Environ</c>: the environment of the process the program runs in, unless a test says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// A name is found without regard to case, as it is on Windows - where it cannot be otherwise - and on a platform that tells <c>Path</c> from <c>PATH</c>, the one
/// whose case is exact is preferred. A name written as a macro, <c>%TEMP%</c>, is the variable it names.
/// </para>
/// <para>
/// On Windows a value is a string the shell expands, and the <c>%NAME%</c> macros in it are the values of those variables: <c>%SystemRoot%\system32</c> reads as
/// <c>C:\Windows\system32</c>. A macro that names no variable is left as it is, as Windows leaves it. Elsewhere a <c>%</c> is a character like any other.
/// </para>
/// <para>
/// The table <c>Environ(n)</c> numbers is the variables in order of their names, so that the same environment always numbers them the same way.
/// </para>
/// </remarks>
/// <param name="variables">Reads the variables, each time they are asked for.</param>
/// <param name="expandsMacros">Whether the <c>%NAME%</c> macros of a value are expanded.</param>
public sealed partial class ProcessEnvironment(Func<IReadOnlyDictionary<string, string>> variables, bool expandsMacros)
{
    /// <summary>
    /// The environment of this process, with its macros expanded on Windows.
    /// </summary>
    public static ProcessEnvironment Current { get; } = new(ReadProcess, OperatingSystem.IsWindows());

    private static IReadOnlyDictionary<string, string> ReadProcess()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            table[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        return table;
    }

    /// <summary>
    /// The value of a variable, or <see langword="null"/> when there is no such variable.
    /// </summary>
    /// <param name="name">The name of the variable, or a macro naming it (<c>%TEMP%</c>).</param>
    public string? Get(string name)
    {
        var table = variables();
        if (name.Length > 2 && name[0] == '%' && name[^1] == '%')
        {
            name = name[1..^1];
        }

        return Find(table, name) is { } value ? Expand(table, value) : null;
    }

    /// <summary>
    /// The <c>NAME=value</c> of the variable at a one-based position of the table, or <see langword="null"/> past its end.
    /// </summary>
    /// <param name="ordinal">The position, from <c>1</c>.</param>
    public string? GetAt(int ordinal)
    {
        var table = variables();
        var entry = table.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair.Key, StringComparer.Ordinal).ElementAtOrDefault(ordinal - 1);
        return ordinal < 1 || entry.Key is null ? null : $"{entry.Key}={Expand(table, entry.Value)}";
    }

    private static string? Find(IReadOnlyDictionary<string, string> table, string name)
        => table.TryGetValue(name, out var exact)
            ? exact
            : table.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private string Expand(IReadOnlyDictionary<string, string> table, string value)
        => expandsMacros && value.Contains('%')
            ? Macro().Replace(value, match => Find(table, match.Groups[1].Value) ?? match.Value)
            : value;

    [GeneratedRegex("%([^%]+)%")]
    private static partial Regex Macro();
}

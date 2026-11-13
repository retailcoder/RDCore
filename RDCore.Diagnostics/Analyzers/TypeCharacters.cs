namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// The type-declaration characters of <strong>MS-VBAL §3.3.5.3</strong>, and the types they stand for.
/// </summary>
internal static class TypeCharacters
{
    private static readonly Dictionary<string, string> TypeNames = new()
    {
        ["%"] = "Integer", ["&"] = "Long", ["^"] = "LongLong", ["!"] = "Single", ["#"] = "Double", ["@"] = "Currency", ["$"] = "String",
    };

    /// <summary>
    /// The name of the type a type-declaration character stands for, or <see langword="null"/> when it is not one.
    /// </summary>
    /// <param name="character">The character, as the syntax tree has it.</param>
    public static string? TypeNameOf(string? character) => character is not null && TypeNames.TryGetValue(character, out var name) ? name : null;
}

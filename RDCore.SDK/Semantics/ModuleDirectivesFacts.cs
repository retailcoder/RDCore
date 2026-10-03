using System.Collections.Immutable;

namespace RDCore.SDK.Semantics;

public enum ModuleOptionKind
{
    /// <summary>
    /// <c>Option Explicit</c> is on or off.
    /// </summary>
    /// <remarks>
    /// Implicit default is off.
    /// </remarks>
    OptionExplicit,
    /// <summary>
    /// <c>Option Base</c> is 0 or 1.
    /// </summary>
    /// <remarks>
    /// Implicit default is 0.
    /// </remarks>
    OptionBase,
    /// <summary>
    /// <c>Option Private Module</c> is on or off.
    /// </summary>
    /// <remarks>
    /// Implicit default is off.
    /// </remarks>
    OptionPrivateModule,
    /// <summary>
    /// <c>Option Compare Text</c> is on or off.
    /// </summary>
    /// <remarks>
    /// <c>Option Compare</c> is either <c>Text</c> or <c>Binary</c>; implicit default is <c>Binary</c>.
    /// </remarks>
    OptionCompareText,
    /// <summary>
    /// <c>Option Compare Binary</c> is on or off.
    /// </summary>
    /// <remarks>
    /// <c>Option Compare</c> is either <c>Text</c> or <c>Binary</c>; implicit default is <c>Binary</c>.
    /// </remarks>
    OptionCompareBinary,
    /// <summary>
    /// <c>Option Strict</c> is on or off.
    /// </summary>
    /// <remarks>
    /// Implicit default is off.
    /// </remarks>
    OptionStrict,
}

public sealed record class ModuleOptionDirectiveFact(ModuleOptionKind Kind, bool Value, bool IsImplicit);
public sealed record class ModuleAttributeFacts(
    string VBName,
    string VBDescription,
    bool VBPredeclaredId,
    bool VBExposed,
    bool VBGlobalNamespace,
    bool VBCreatable,
    bool VBExtensible
    );

public sealed record class ModuleDirectivesFacts
{
    public string NameAttribute { get; init; } = string.Empty;
    public string? DescriptionAttribute { get; init; }
    public bool GlobalNamespaceAttribute { get; init; }
    public bool CreatableAttribute { get; init; }
    public bool PredeclaredIdAttribute { get; init; }
    public bool ExposedAttribute { get; init; }

    public bool ExtensibleAttribute { get; init; }

    public ImmutableArray<ModuleOptionDirectiveFact> Options { get; init; } = [];
    public ImmutableArray<string> Implements { get; init; } = [];
}

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

using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// A Variant's own value-type tag — a deliberately partial slice of the COM <c>VARIANT</c> <c>VT_*</c>
/// tag space; only the shapes an actual caller needs so far are represented.
/// </summary>
public enum VBVariantValueType
{
    Empty,
    Integer,
    Dispatch,
    BString,
}

/// <summary>
/// Wraps a <see cref="VBVariantValue"/>'s own wrapped <see cref="VBTypedValue"/> for storage inside an
/// <see cref="IRuntimeValue"/>, so a Variant round-trips through <c>ISessionStorage</c> like any other
/// value — the same pattern <see cref="VBRuntimeArrayValue"/> uses for an array.
/// </summary>
/// <param name="ValueType">The variant <em>value type</em> tag.</param>
/// <param name="WrappedValue">The Variant's own wrapped value, cells/type info intact.</param>
public sealed record class VBRuntimeVariantValue(VBVariantValueType ValueType, VBTypedValue WrappedValue) : IRuntimeValue
{
    /// <inheritdoc/>
    public object BoxedValue => WrappedValue.RuntimeValue.BoxedValue;
}

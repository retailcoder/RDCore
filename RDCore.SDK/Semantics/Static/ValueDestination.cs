namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a place that expects a value expects of an object, at compile time: what <see cref="ObjectValueStaticSemantics"/> checks the object it is given
/// against (<strong>MS-VBAL §5.5.1.1</strong>, as MS-VBA compiles it).
/// </summary>
/// <remarks>
/// Where a value is expected, an object stands for its <em>simple data value</em>, what its default member returns (<strong>MS-VBAL §5.5.1.2.13</strong>).
/// The <c>Nothing</c> literal has none, and neither has an object whose default member takes an argument; what else is checked depends on the place.
/// </remarks>
public enum ValueDestination
{
    /// <summary>
    /// A value of whatever type it turns out to be: the argument of <c>Abs</c>, <c>Int</c>, <c>Fix</c> or <c>Len</c>. An object whose default member returns
    /// an object is read at run time.
    /// </summary>
    AnyValue,

    /// <summary>
    /// A value of a declared type: a variable or a parameter of a value type, an operand, the argument of a conversion function such as <c>CInt</c>. An
    /// object whose default member returns an object is a type mismatch.
    /// </summary>
    TypedValue,
}

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
    /// A value of whatever type it turns out to be: the argument of <c>Abs</c>, <c>Int</c> or <c>Fix</c>. An object whose default member returns an object is
    /// read at run time.
    /// </summary>
    AnyValue,

    /// <summary>
    /// A value of a declared type: a variable or a parameter of a value type, an operand, the argument of a conversion function such as <c>CInt</c>. An
    /// object whose default member returns an object is a type mismatch.
    /// </summary>
    TypedValue,

    /// <summary>
    /// "Any valid string expression, or any valid variable name": the argument of <c>Len</c> and <c>LenB</c> (<strong>MS-VBAL §6.1.2.11.1.22</strong>).
    /// </summary>
    /// <remarks>
    /// A <c>String</c>, a <c>Variant</c>, a late-bound object and a record are read as a value; a value of any other declared type must be a variable, and
    /// anything else is <see cref="Model.Errors.VBCompileErrorId.VariableRequired"/>: <c>Len(5)</c>, <c>Len(FnLong())</c>, an object whose default member
    /// returns neither a <c>String</c> nor a <c>Variant</c> (recorded in Excel).
    /// </remarks>
    StringOrVariable,
}

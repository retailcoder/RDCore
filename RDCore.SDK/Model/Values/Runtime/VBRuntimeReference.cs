using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// The address of a variable, passed where the variable itself is: the argument of a <c>ByRef</c> parameter that names one.
/// </summary>
/// <param name="Value">The address of the variable.</param>
/// <param name="DeclaredType">
/// The declared type of the variable, when the parameter it is passed to does not have it: a <c>Variant</c> parameter given a variable of another type
/// (<strong>MS-VBAL §5.3.1.11</strong>), which reads as a <c>Variant</c> but is assigned as what the variable is declared to be. <see langword="null"/>
/// when the parameter's own type is the variable's.
/// </param>
/// <param name="Element">
/// The array and the subscripts of the element the variable is, when it is an element of an array passed to a native function: MS-VBA passes the address of
/// the element where it is in the array's data, so the function sees the elements after it too. <see langword="null"/> for any other variable.
/// </param>
public readonly record struct VBRuntimeReference(MemoryAddress Value, VBType? DeclaredType = null, VBArrayElementLocation? Element = null) : IRuntimeValue
{
    public static readonly VBRuntimeReference NullRef = new(MemoryAddress.Zero);

    public object BoxedValue => Value;
}

/// <summary>
/// Where an element of an array is: the array, and the subscripts that select the element in it.
/// </summary>
/// <param name="Array">The array.</param>
/// <param name="Subscripts">The subscripts of the element, one for each dimension.</param>
public sealed record class VBArrayElementLocation(VBArrayValue Array, int[] Subscripts);

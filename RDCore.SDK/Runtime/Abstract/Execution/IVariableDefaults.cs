using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What the storage of a declared variable starts as.
/// </summary>
/// <remarks>
/// For most variables that is the default value of the declared type. For an array it is more than the type can say: a
/// fixed-size array is as big as its declaration's bounds (<strong>MS-VBAL §5.2.3.1.3</strong>), constant expressions that may
/// name a <c>Const</c> and so have to be reduced, and an array of <c>Long</c> that has no dimensions yet is still an array of
/// <c>Long</c>. Whatever allocates storage for a variable asks this, which is what keeps that in one place.
/// <para>
/// An optional parameter is a variable too, and the call that omits its argument is what allocates it: it starts as its
/// declared default (<strong>MS-VBAL §5.3.1.5</strong>), or, declaring none, as the Missing value when it is a <c>Variant</c>
/// (<strong>MS-VBAL §6.1.2.7.1.6</strong>) and its type's default value otherwise.
/// </para>
/// </remarks>
public interface IVariableDefaults
{
    /// <summary>
    /// The value the storage of <paramref name="variable"/> starts as: its type's default value, except for an array and an
    /// optional parameter whose argument is omitted.
    /// </summary>
    /// <param name="variable">The variable, which is typed.</param>
    VBTypedValue DefaultValueOf(Symbol variable);

    /// <summary>
    /// What each element of an array of <paramref name="itemType"/> starts as, when it is more than the type's own default value: a record,
    /// whose fields start as declared variables of them do - a fixed-size array field with its dimensions (<strong>MS-VBAL §5.2.3.3</strong>).
    /// </summary>
    /// <param name="itemType">The element type of the array.</param>
    /// <returns>Makes one element; <see langword="null"/> when the type's own default value is what an element starts as.</returns>
    Func<VBTypedValue>? ElementDefaultOf(VBType itemType)
        => itemType is VBUserDefinedType record ? () => new VBUserDefinedTypeValue(record, this) : null;
}

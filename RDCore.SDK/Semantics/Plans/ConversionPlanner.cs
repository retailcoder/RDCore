using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using System.Collections.Concurrent;

namespace RDCore.SDK.Semantics.Plans;

/// <summary>
/// Makes the <see cref="ConversionPlan"/> of a let-coercion from the type of the value and the type it is let-coerced to: the one table of the rules of
/// <strong>MS-VBAL §5.5.1.2</strong>, consulted when a conversion is bound from declared types and when it is evaluated on a value (IR lowering §3.2).
/// </summary>
/// <remarks>
/// <para>
/// A plan depends on nothing but the two types, so each is made once and kept, and the plans last asked for are also kept by the identity of the types
/// (<see cref="RecentPlans{TPlan}"/>): a variable assigned in a loop is let-coerced from the same type every time.
/// </para>
/// <para>
/// An object, <c>Null</c>, <c>Empty</c> and an <c>Error</c> are let-coerced by rules of their own, whatever the destination is - but for a <c>Variant</c>
/// or a fixed-size array, which hold <c>Null</c>, <c>Empty</c> and an <c>Error</c> as they are, or have no rule for them. Anything else is let-coerced by
/// the rule of its destination. Whether that rule takes the value is the conversion's to say: the plan names the rule, and the rule decides.
/// </para>
/// <para>
/// 🚧 The static semantics still have a table of their own (<strong>§5.5.1.1</strong>), keyed by declared types, which says which let-coercions are
/// valid at all; binding from declared types (S0.5) is what reconciles the two.
/// </para>
/// </remarks>
public static class ConversionPlanner
{
    private static readonly ConcurrentDictionary<(VBType Source, VBType Destination), ConversionPlan> _plans = new();
    private static readonly RecentPlans<ConversionPlan> _recent = new();

    /// <summary>
    /// The plan of the let-coercion of a value of this type: final, whatever the types are.
    /// </summary>
    /// <param name="source">The value type of the source: the type of what a Variant holds.</param>
    /// <param name="destination">The declared type the value is let-coerced to.</param>
    public static ConversionPlan ForValue(VBType source, VBType destination)
    {
        if (_recent.TryGet(source, destination, null, out var plan))
        {
            return plan;
        }

        plan = _plans.GetOrAdd((source, destination), key => Plan(key.Source, key.Destination));
        _recent.Add(source, destination, null, plan);
        return plan;
    }

    /// <summary>
    /// The plan of the let-coercion of a source of this declared type: <see cref="ConversionPlan.IsDeferred"/> when it is declared <c>Variant</c>, whose
    /// value type is not known until the conversion is evaluated, and the plan of its value otherwise.
    /// </summary>
    /// <param name="source">The declared type of the source.</param>
    /// <param name="destination">The declared type the value is let-coerced to.</param>
    /// <remarks>
    /// A fixed-length string holds a <c>String</c>, an Enum a <c>Long</c>, a <c>LongPtr</c> what it is an alias of, and a class an object: those are the
    /// value types the plan is made for.
    /// </remarks>
    public static ConversionPlan ForDeclared(VBType source, VBType destination)
        => source is VBVariantType
            ? new ConversionPlan(source, DestinationOf(destination), ConversionRule.None, IsDeferred: true)
            : ForValue(ValueTypeOf(source), destination);

    private static VBType ValueTypeOf(VBType declared) => declared switch
    {
        VBFixedStringType => VBStringType.TypeInfo,
        VBEnumType => VBLongType.TypeInfo,
        VBClassType => VBObjectType.TypeInfo,
        _ => AliasedTypeOf(declared),
    };

    // MS-VBAL §2.2: LongPtr is an alias, of LongLong in a 64-bit environment and of Long in a 32-bit one.
    private static VBType AliasedTypeOf(VBType type) => type switch
    {
        VBLongPtrType_x64 => VBLongLongType.TypeInfo,
        VBLongPtrType_x86 => VBLongType.TypeInfo,
        _ => type,
    };

    // MS-VBAL §5.2.3.4: what is let-coerced to an enumeration is let-coerced to a Long.
    private static VBType DestinationOf(VBType declared) => declared is VBEnumType ? VBLongType.TypeInfo : AliasedTypeOf(declared);

    private static ConversionPlan Plan(VBType source, VBType declaredDestination)
    {
        source = AliasedTypeOf(source);
        var destination = DestinationOf(declaredDestination);
        var rule = source switch
        {
            VBObjectType => ConversionRule.FromObject,
            VBNullType when HasRulesForNullEmptyAndError(destination) => ConversionRule.FromNull,
            VBEmptyType when HasRulesForNullEmptyAndError(destination) => ConversionRule.FromEmpty,
            VBErrorType when HasRulesForNullEmptyAndError(destination) => ConversionRule.FromError,
            _ => RuleOf(destination),
        };

        return new ConversionPlan(source, destination, rule);
    }

    // MS-VBAL §5.5.1.2.9-11: Null, Empty and an Error have rules of their own to any destination "except a fixed-size array or Variant".
    private static bool HasRulesForNullEmptyAndError(VBType destination) => destination is not VBVariantType and not VBFixedSizeArrayType;

    // the rule of each destination type, the more specific type first: a fixed-length string is a String, and a Byte() a resizable array.
    private static ConversionRule RuleOf(VBType destination) => destination switch
    {
        VBFixedStringType => ConversionRule.FixedString,
        VBStringType => ConversionRule.String,
        VBNumericType => ConversionRule.Numeric,
        VBBooleanType => ConversionRule.Boolean,
        VBDateType => ConversionRule.Date,
        VBVariantType => ConversionRule.Variant,
        VBObjectType => ConversionRule.Object,
        VBEmptyType => ConversionRule.Empty,
        VBNullType => ConversionRule.Null,
        VBErrorType => ConversionRule.Error,
        VBUserDefinedType => ConversionRule.UserDefinedType,
        VBResizableByteArrayType => ConversionRule.ByteArray,
        VBResizableArrayType => ConversionRule.Array,
        _ => ConversionRule.None,
    };
}

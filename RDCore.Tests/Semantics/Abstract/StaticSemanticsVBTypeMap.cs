using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Semantics.Abstract;

/// <summary>
/// Defines the static semantic test inputs and corresponding expectations
/// </summary>
public abstract class StaticSemanticsVBTypeMap
{
    /// <summary>
    /// Gets a <c>DataRow</c>-ready map of unary operator <c>[operand,expected]</c> arrays with the specified overrides.
    /// </summary>
    /// <param name="overrides">The <c>(operand,expected)</c> pairs to override.</param>
    public static IEnumerable<object[]> OverrideUnaryOperatorMap(params (VBType operand, VBType expected)[] overrides)
    {
        var map = new Dictionary<VBType, VBType>(UnaryOperatorsBaseMap);
        foreach (var (operand, expected) in overrides)
        {
            map[operand] = expected;
        }
        return map.Select(kvp => new object[] { kvp.Key, kvp.Value });
    }

    /// <summary>
    /// Gets a <c>DataRow</c>-ready map of unary operator <c>[lhs,rhs,expected]</c> arrays with the specified overrides.
    /// </summary>
    /// <param name="overrides">The <c>(operand,expected)</c> pairs to override.</param>
    public static IEnumerable<object[]> OverrideBinaryOperatorMap(params (VBType lhs, VBType rhs, VBType expected)[] overrides)
    {
        var map = new Dictionary<(VBType,VBType), VBType>(BinaryOperatorsBaseMap);
        foreach (var (lhs, rhs, expected) in overrides)
        {
            map[(lhs, rhs)] = expected;
        }
        return map.Select(kvp => new object[] { kvp.Key.Item1, kvp.Key.Item2, kvp.Value });
    }

    /// <summary>
    /// Encodes <strong>MS-VBAL 5.6.9.3</strong> Unary Operators base static semantics.
    /// <para>
    /// 👉 A Decimal operand makes a Decimal of a Currency or a Date (MS-VBAL 5.6.9.3), as the plan of the operation has it; no declaration has the type.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Unary operators may override these mappings.
    /// </remarks>
    public static Dictionary<VBType, VBType> UnaryOperatorsBaseMap { get; } = new()
    {
        [VBByteType.TypeInfo] = VBByteType.TypeInfo,
        [VBBooleanType.TypeInfo] = VBIntegerType.TypeInfo,
        [VBIntegerType.TypeInfo] = VBIntegerType.TypeInfo,
        [VBLongType.TypeInfo] = VBLongType.TypeInfo,
        [VBLongLongType.TypeInfo] = VBLongLongType.TypeInfo,
        [VBSingleType.TypeInfo] = VBSingleType.TypeInfo,
        [VBDoubleType.TypeInfo] = VBDoubleType.TypeInfo,
        [VBStringType.TypeInfo] = VBDoubleType.TypeInfo,
        [VBCurrencyType.TypeInfo] = VBCurrencyType.TypeInfo,
        [VBDateType.TypeInfo] = VBDateType.TypeInfo,
        [VBVariantType.TypeInfo] = VBVariantType.TypeInfo,
    };

    /// <summary>
    /// Encodes <strong>MS-VBAL 5.6.9.3</strong> Binary Operators base static semantics.
    /// </summary>
    /// <remarks>
    /// Binary operators may override these mappings.
    /// </remarks>
    public static Dictionary<(VBType, VBType), VBType> BinaryOperatorsBaseMap { get; } = new()
    {
        [(VBByteType.TypeInfo, VBByteType.TypeInfo)] = VBByteType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBByteType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBBooleanType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBIntegerType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBByteType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBBooleanType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBIntegerType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBByteType.TypeInfo, VBBooleanType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBByteType.TypeInfo, VBIntegerType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBLongType.TypeInfo, VBByteType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBBooleanType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBByteType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBByteType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBByteType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBSingleType.TypeInfo, VBByteType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBBooleanType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBIntegerType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBByteType.TypeInfo, VBSingleType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBSingleType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBSingleType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBByteType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBStringType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBByteType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBLongType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBSingleType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo)] = VBDecimalType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBStringType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBByteType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBSingleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDecimalType.TypeInfo,
        [(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBByteType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBIntegerType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBLongType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBLongLongType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBSingleType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBDoubleType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBDecimalType.TypeInfo)] = VBDecimalType.TypeInfo,
        [(VBDateType.TypeInfo, VBStringType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBByteType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBLongType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBSingleType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDecimalType.TypeInfo, VBDateType.TypeInfo)] = VBDecimalType.TypeInfo,
        [(VBStringType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBVariantType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBByteType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBBooleanType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBIntegerType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBLongType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBLongLongType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBSingleType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBDoubleType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBCurrencyType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBDecimalType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBDateType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBStringType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBObjectType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBNullType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBEmptyType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBErrorType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBByteType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBLongType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBSingleType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBDecimalType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBDateType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBStringType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBObjectType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBNullType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
    };
}


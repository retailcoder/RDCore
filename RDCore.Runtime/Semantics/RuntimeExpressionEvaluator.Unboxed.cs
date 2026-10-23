using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Semantics;

// IR lowering S1.2: expressions of numeric scalar variables evaluated on payloads (ScalarPayload), with no value made of what they evaluate to. A node the
// payload evaluation does not do - or one that would raise an error - declines it, and the boxed evaluation evaluates the expression as it always has: the
// nodes it does are literals, variables and operators, which can be evaluated again with no other effect, so the error the boxed evaluation raises is the
// one the expression raises.
public sealed partial class RuntimeExpressionEvaluator
{
    /// <summary>
    /// Let-assigns <paramref name="value"/> to <paramref name="target"/> on payloads, when the target is a variable that holds a payload and the value is an
    /// expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the assignment is not done here, and is to be done the way it always is.</returns>
    internal bool TryAssignUnboxed(IRuntimeSession session, ExpressionNode target, ExpressionNode value, RuntimeEvaluationContext context)
    {
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || BoundVariableOf(session, target, context) is not { } variable
            || BindingOf(session, variable) is not (ScalarCell cell, null)
            || !TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var payload)
            || !TryConvertPayload(payload, cell.Type, out var converted))
        {
            return false;
        }

        cell.Write(converted);
        return true;
    }

    /// <summary>
    /// Steps a For loop whose counter holds a payload (MS-VBAL §5.4.2.3): the counter is given its value plus the step, and compared with the end - in
    /// the counter's type, which the end and the step are of.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="state">The loop.</param>
    /// <param name="context">Where the counter is resolved.</param>
    /// <param name="isOutOfRange">Whether the counter is past the end, and the loop ends.</param>
    /// <returns><see langword="false"/> when the loop is not stepped here - the sum would raise an error, or a value is not a payload - and is to be stepped the way it always is.</returns>
    internal bool TryStepForUnboxed(IRuntimeSession session, ForLoopState state, RuntimeEvaluationContext context, out bool isOutOfRange)
    {
        isOutOfRange = false;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || BoundVariableOf(session, state.ControlExpression, context) is not { } counter
            || BindingOf(session, counter) is not (ScalarCell cell, null)
            || !cell.TryRead(out var value)
            || state.Step.IsIndeterminate || state.End.IsIndeterminate
            || !ScalarPayload.TryOf(cell.Type, state.Step.RuntimeValue, out var step) || !ScalarPayload.TryOf(cell.Type, state.End.RuntimeValue, out var end)
            || !TryOperate(Tokens.AdditionOp, cell.Type, value, step, out var sum))
        {
            return false;
        }

        cell.Write(sum);
        isOutOfRange = sum.IsIntegral
            ? state.CountsDown ? sum.Integral < end.Integral : sum.Integral > end.Integral
            : state.CountsDown ? sum.Floating < end.Floating : sum.Floating > end.Floating;
        return true;
    }

    private bool TryEvaluatePayload(IRuntimeSession session, BoundNode node, out ScalarPayload payload)
    {
        payload = default;
        switch (node)
        {
            case BoundLiteral literal:
                return ScalarPayload.TryOf(literal.Literal.StaticValue.TypeInfo, literal.Literal.StaticValue.RuntimeValue, out payload);
            case BoundVariable variable:
                return BindingOf(session, variable) is (ScalarCell cell, null) && cell.TryRead(out payload);
            // MS-VBAL §5.6.6: what is in parentheses is a value - of a payload, the payload, whether the parentheses group an operand (the precedence
            // operator) or coerce what they enclose (the let-coercion operator, which converts nothing of a scalar).
            case BoundUnary { Operation.Token: SDK.Model.Symbols.Operators.OperatorSymbolNames.UnaryArithmeticPrecedenceOp
                or SDK.Model.Symbols.Operators.OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return TryEvaluatePayload(session, enclosed, out payload);
            case BoundUnary { Plan: { IsDeferred: false, EffectiveType: { } effective } plan, Operand: var operand } when plan.Operator == Tokens.NegationOp:
                return TryEvaluatePayload(session, operand, out var negated) && TryConvertPayload(negated, effective, out var operandValue)
                    && TryNegate(operandValue, out payload);
            case BoundUnary { Plan: { IsDeferred: false, EffectiveType: { } effective } plan, Operand: var operand } when plan.Operator == Tokens.LogicalNotOp:
                return TryEvaluatePayload(session, operand, out var inverted) && TryConvertPayload(inverted, effective, out var bits)
                    && TryNot(bits, out payload);
            case BoundBinary { Plan: { IsDeferred: false, OperandDestination: { } destination } plan, Left: var left, Right: var right }:
                return TryEvaluatePayload(session, left, out var lhs) && TryEvaluatePayload(session, right, out var rhs)
                    && TryConvertPayload(lhs, destination, out var l) && TryConvertPayload(rhs, destination, out var r)
                    && (TryOperate(plan.Operator, plan.EffectiveType!, l, r, out payload) || TryCompare(plan.Operator, l, r, out payload)
                        || TryLogical(plan.Operator, plan.EffectiveType!, l, r, out payload));
            default:
                return false;
        }
    }

    /// <summary>
    /// Evaluates a condition on payloads - of <c>If</c>, a loop, <c>Debug.Assert</c> - when it is an expression evaluated on them: its value let-coerced to a
    /// <c>Boolean</c> is whether it is not zero (MS-VBAL §5.5.1.2.2).
    /// </summary>
    /// <returns><see langword="false"/> when the condition is not evaluated here, and is to be evaluated the way it always is.</returns>
    internal bool TryEvaluateConditionUnboxed(IRuntimeSession session, ExpressionNode condition, RuntimeEvaluationContext context, out bool isTrue)
    {
        isTrue = false;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || !TryEvaluatePayload(session, BoundTreeOf(session, condition, context), out var payload))
        {
            return false;
        }

        isTrue = payload.IsTrue;
        return true;
    }

    /// <summary>
    /// Compares the selector of a <c>Select Case</c> with the value of a <c>Case</c> on payloads (MS-VBAL §5.4.2.10): <c>selector operator value</c>, when the
    /// selector is a payload and the value an expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the comparison is not made here, and is to be made the way it always is.</returns>
    internal bool TryCompareUnboxed(IRuntimeSession session, string comparisonOperator, VBTypedValue selector, ExpressionNode value, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables || selector.IsIndeterminate
            || !ScalarPayload.TryOf(selector.TypeInfo, selector.RuntimeValue, out var lhs)
            || !TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var rhs)
            || OperationPlanner.ForValues(comparisonOperator, lhs.Type, rhs.Type) is not { OperandDestination: { } destination }
            || !TryConvertPayload(lhs, destination, out var l) || !TryConvertPayload(rhs, destination, out var r)
            || !TryCompare(comparisonOperator, l, r, out var result))
        {
            return false;
        }

        isMatch = result.IsTrue;
        return true;
    }

    // the conversions that are exact - what a value of one numeric scalar type is in another, with no rounding to decide: an integer in the range of an
    // integer type, an integer as a floating-point value, a Single as a Double. Anything else is the boxed coercion's (MS-VBAL §5.5.1.2.1), which rounds.
    private static bool TryConvertPayload(ScalarPayload payload, VBType destination, out ScalarPayload converted)
    {
        converted = payload;
        if (ReferenceEquals(payload.Type, destination) || payload.Type.Equals(destination))
        {
            return true;
        }

        // MS-VBAL §5.5.1.2.2: any number is True when it is not zero, and a Boolean is the -1 or 0 it holds - which a Byte does not hold, its True being
        // 255: the boxed coercion's.
        if (destination is VBBooleanType)
        {
            converted = ScalarPayload.OfBoolean(payload.IsTrue);
            return true;
        }

        return (payload.IsIntegral, destination) switch
        {
            (true, VBByteType or VBIntegerType or VBLongType or VBLongLongType) => ScalarPayload.TryIntegral(destination, payload.Integral, out converted),
            (true, VBDoubleType) => ScalarPayload.TryFloating(destination, payload.Integral, out converted),
            (true, VBSingleType) when payload.Type is not VBLongLongType => ScalarPayload.TryFloating(destination, payload.Integral, out converted),
            (false, VBDoubleType) => ScalarPayload.TryFloating(destination, payload.Floating, out converted),
            _ => false,
        };
    }

    // MS-VBAL §5.6.9.3: the operators on their effective type, as the boxed evaluation does them (checked, in the type's own width); a result that does not
    // fit, a division by zero and a result that is not finite are errors, which the boxed evaluation raises.
    private static bool TryOperate(string token, VBType effective, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        result = default;
        try
        {
            return (effective, token) switch
            {
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.AdditionOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral + r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.SubtractionOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral - r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.MultiplicationOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral * r.Integral), out result),
                // §5.6.9.3.5-6: a quotient that is truncated, and the remainder that has the dividend's sign - in the integer effective type the
                // operands were coerced to (a floating-point operand is rounded to it, which is the boxed coercion's).
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.IntegerDivisionOp) when r.Integral != 0 => ScalarPayload.TryIntegral(effective, checked(l.Integral / r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.ModuloOp) when r.Integral != 0 => ScalarPayload.TryIntegral(effective, checked(l.Integral % r.Integral), out result),
                (VBSingleType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, (float)l.Floating + (float)r.Floating, out result),
                (VBSingleType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, (float)l.Floating - (float)r.Floating, out result),
                (VBSingleType, Tokens.MultiplicationOp) => ScalarPayload.TryFloating(effective, (float)l.Floating * (float)r.Floating, out result),
                (VBSingleType, Tokens.DivisionOp) when r.Floating != 0 => ScalarPayload.TryFloating(effective, (float)l.Floating / (float)r.Floating, out result),
                (VBDoubleType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, l.Floating + r.Floating, out result),
                (VBDoubleType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, l.Floating - r.Floating, out result),
                (VBDoubleType, Tokens.MultiplicationOp) => ScalarPayload.TryFloating(effective, l.Floating * r.Floating, out result),
                (VBDoubleType, Tokens.DivisionOp) when r.Floating != 0 => ScalarPayload.TryFloating(effective, l.Floating / r.Floating, out result),
                _ => false,
            };
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    // §5.6.9.8: the bits of the operands, in the effective type - a Boolean's -1 or 0 giving -1 or 0 - which no operation takes out of its range. Apart from
    // the arithmetic operators, which would otherwise test these tokens too.
    private static bool TryLogical(string token, VBType effective, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        result = default;
        if (!l.IsIntegral)
        {
            return false;
        }

        long? bits = token switch
        {
            Tokens.LogicalAndOp => l.Integral & r.Integral,
            Tokens.LogicalOrOp => l.Integral | r.Integral,
            Tokens.LogicalXOrOp => l.Integral ^ r.Integral,
            Tokens.LogicalEqvOp => Invert(effective, l.Integral ^ r.Integral),
            Tokens.LogicalImpOp => Invert(effective, l.Integral) | r.Integral,
            _ => null,
        };

        return bits is { } value && ScalarPayload.TryIntegral(effective, value, out result);
    }

    // MS-VBAL §5.6.9.5: a comparison of two operands of its effective type, which they were let-coerced to, is a Boolean - the payloads are finite, so any
    // two compare.
    private static bool TryCompare(string token, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        var order = l.IsIntegral ? l.Integral.CompareTo(r.Integral) : l.Floating.CompareTo(r.Floating);
        bool? isTrue = token switch
        {
            Tokens.CompareEqualOp => order == 0,
            Tokens.CompareNotEqualOp => order != 0,
            Tokens.CompareLessThanOp => order < 0,
            Tokens.CompareLessThanOrEqualOp => order <= 0,
            Tokens.CompareGreaterThanOp => order > 0,
            Tokens.CompareGreaterThanOrEqualOp => order >= 0,
            _ => null,
        };

        result = isTrue is { } value ? ScalarPayload.OfBoolean(value) : default;
        return isTrue is not null;
    }

    // §5.6.9.8.1: the bits of the operand inverted, in its type - the 8 bits of a Byte, a Boolean's -1 or 0 giving 0 or -1.
    private static bool TryNot(ScalarPayload operand, out ScalarPayload result)
    {
        result = default;
        return operand.IsIntegral && ScalarPayload.TryIntegral(operand.Type, Invert(operand.Type, operand.Integral), out result);
    }

    private static long Invert(VBType type, long value) => type is VBByteType ? ~value & byte.MaxValue : ~value;

    private static bool TryNegate(ScalarPayload operand, out ScalarPayload result)
    {
        result = default;
        if (!operand.IsIntegral)
        {
            return ScalarPayload.TryFloating(operand.Type, -operand.Floating, out result);
        }

        // the negation of the least value of an integer type is not in its range (MS-VBA: Overflow), which the boxed evaluation raises.
        return operand.Integral != long.MinValue && ScalarPayload.TryIntegral(operand.Type, -operand.Integral, out result);
    }
}

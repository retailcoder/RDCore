using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Matches a <c>Select Case</c>'s already-evaluated selector value against one <c>Case</c> block's range
/// clauses (<strong>MS-VBAL §5.4.2.10</strong>).
/// </summary>
/// <remarks>
/// Each range-clause shape is evaluated exactly the way the spec's own runtime semantics phrase it — as a
/// real comparison/logical expression built from the selector and the clause's own operand expression(s),
/// through the operator semantics (<see cref="IOperatorRuntimeSemanticsProvider"/>) — never a hand-rolled
/// equality/range check. The comparison has no operator expression of its own in source: the
/// location-bearing node it is evaluated for is always the clause's own real operand expression, never a
/// fabricated stand-in for the already-evaluated selector.
/// </remarks>
public sealed class CaseMatchEvaluator(RuntimeExpressionEvaluator expressionEvaluator, IOperatorRuntimeSemanticsProvider operators)
{
    /// <summary>
    /// Evaluates a <c>Select Case</c>'s own selector (<c>select-expression</c>) — once, ahead of every
    /// <c>Case</c> header's own range-clause matching against the result (<strong>MS-VBAL §5.4.2.10</strong>).
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateSelector(IRuntimeSession session, ExpressionNode controlExpression, RuntimeEvaluationContext context)
        => expressionEvaluator.Evaluate(session, controlExpression, context);

    /// <summary>
    /// <see cref="EvaluateSelector"/> on payloads (IR lowering S1.6), when the selector is an expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the selector is to be evaluated the way it always is.</returns>
    internal bool TryEvaluateSelectorUnboxed(IRuntimeSession session, ExpressionNode controlExpression, RuntimeEvaluationContext context, out ScalarPayload selector)
        => expressionEvaluator.TryEvaluateSelectorUnboxed(session, controlExpression, context, out selector);

    /// <summary>
    /// <see cref="Evaluate"/> on payloads, for a selector evaluated on them: each clause's comparison made as <see cref="Evaluate"/> makes it, in the same
    /// order, short-circuiting the same way.
    /// </summary>
    /// <returns><see langword="false"/> when a clause is not compared on payloads: the whole <c>Case</c> is then matched the way it always is, which
    /// evaluates again only clauses that have no other effect.</returns>
    internal bool TryMatchUnboxed(IRuntimeSession session, CaseExpressionStatementNode caseBlock, ScalarPayload selector, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        foreach (var clause in caseBlock.RangeClauses)
        {
            var compared = clause switch
            {
                CaseValueRangeClauseNode value => expressionEvaluator.TryCompareUnboxed(session, Tokens.CompareEqualOp, selector, value.Value, context, out isMatch),
                CaseComparisonRangeClauseNode comparison
                    => expressionEvaluator.TryCompareUnboxed(session, comparison.ComparisonOperator, selector, comparison.Value, context, out isMatch),
                CaseToRangeClauseNode range => TryMatchRangeUnboxed(session, range, selector, context, out isMatch),
                _ => false,
            };

            if (!compared)
            {
                return false;
            }

            if (isMatch)
            {
                return true;
            }
        }

        return true;
    }

    // both ends are compared, as the And of the two comparisons (EvaluateRange) evaluates both.
    private bool TryMatchRangeUnboxed(IRuntimeSession session, CaseToRangeClauseNode range, ScalarPayload selector, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        if (!expressionEvaluator.TryCompareUnboxed(session, Tokens.CompareGreaterThanOrEqualOp, selector, range.Start, context, out var above)
            || !expressionEvaluator.TryCompareUnboxed(session, Tokens.CompareLessThanOrEqualOp, selector, range.End, context, out var below))
        {
            return false;
        }

        isMatch = above && below;
        return true;
    }

    /// <summary>
    /// Evaluates <paramref name="caseBlock"/>'s range clauses, in source order, against
    /// <paramref name="selector"/> — short-circuiting on the first clause that matches, per spec ("any
    /// subsequent range-clause in the case-clause is not evaluated").
    /// </summary>
    public RuntimeSemanticsEvaluationResult Evaluate(IRuntimeSession session, CaseExpressionStatementNode caseBlock, VBTypedValue selector, RuntimeEvaluationContext context)
    {
        foreach (var clause in caseBlock.RangeClauses)
        {
            var result = EvaluateClause(session, clause, selector, context);
            if (!result.IsSuccess)
            {
                return result;
            }
            if (((VBBooleanValue)result.Result!).Value.StoredValue != 0)
            {
                return result;
            }
        }
        return RuntimeSemanticsEvaluationResult.Success(VBBooleanValue.False);
    }

    private RuntimeSemanticsEvaluationResult EvaluateClause(IRuntimeSession session, CaseRangeClauseNode clause, VBTypedValue selector, RuntimeEvaluationContext context) => clause switch
    {
        // "the expression is evaluated and its result is compared with the value of select-expression" (5.4.2.10)
        CaseValueRangeClauseNode value => EvaluateComparison(session, Tokens.CompareEqualOp, selector, value.Value, context),

        // "the expression <select-expression> <comparison-operator> <expression> is evaluated"
        CaseComparisonRangeClauseNode comparison => EvaluateComparison(session, comparison.ComparisonOperator, selector, comparison.Value, context),

        // "the expression ((select-expression) >= (start-value)) And ((select-expression) <= (end-value)) is evaluated"
        CaseToRangeClauseNode range => EvaluateRange(session, selector, range, context),

        _ => RuntimeSemanticsEvaluationResult.InternalError(),
    };

    // The selector was already evaluated once (by EvaluateSelector); operand is the clause's own real
    // expression, evaluated fresh. The location-bearing node passed to the operator is always operand -
    // a real, source-derived node - never a stand-in fabricated for the already-known selector value.
    private RuntimeSemanticsEvaluationResult EvaluateComparison(IRuntimeSession session, string comparisonOperator, VBTypedValue selector, ExpressionNode operand, RuntimeEvaluationContext context)
    {
        if (expressionEvaluator.TryCompareUnboxed(session, comparisonOperator, selector, operand, context, out var isMatch))
        {
            return RuntimeSemanticsEvaluationResult.Success(isMatch ? VBBooleanValue.True : VBBooleanValue.False);
        }

        var operandResult = expressionEvaluator.Evaluate(session, operand, context);
        if (!operandResult.IsSuccess)
        {
            return operandResult;
        }
        return operators.EvaluateBinaryOperator(session, comparisonOperator, operand, selector, operandResult.Result!, ConversionSite.CaseTest);
    }

    private RuntimeSemanticsEvaluationResult EvaluateRange(IRuntimeSession session, VBTypedValue selector, CaseToRangeClauseNode range, RuntimeEvaluationContext context)
    {
        var lowerBound = EvaluateComparison(session, Tokens.CompareGreaterThanOrEqualOp, selector, range.Start, context);
        if (!lowerBound.IsSuccess)
        {
            return lowerBound;
        }

        var upperBound = EvaluateComparison(session, Tokens.CompareLessThanOrEqualOp, selector, range.End, context);
        if (!upperBound.IsSuccess)
        {
            return upperBound;
        }

        return operators.EvaluateBinaryOperator(session, Tokens.LogicalAndOp, range.Start, lowerBound.Result!, upperBound.Result!, ConversionSite.CaseTest);
    }
}

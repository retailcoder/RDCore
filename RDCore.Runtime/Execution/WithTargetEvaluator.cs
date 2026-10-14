using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>With</c> statement's target expression and produces the value that would be stored in
/// its anonymous block variable (<strong>MS-VBAL §5.4.2.21</strong>).
/// </summary>
/// <param name="expressionEvaluator">Evaluates the target expression.</param>
/// <param name="withStatement">The runtime semantics of the statement.</param>
/// <param name="observation">Told what the statement is (<see cref="WithStatementFact"/>), when it is evaluated for an analysis.</param>
public sealed class WithTargetEvaluator(RuntimeExpressionEvaluator expressionEvaluator, WithStatementRuntimeSemantics withStatement, AnalysisObservation? observation = null)
{
    /// <summary>
    /// Evaluates <paramref name="node"/>'s target expression and Set/Let-coerces it, per
    /// <see cref="WithStatementRuntimeSemantics"/>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Evaluate(IRuntimeSession session, WithStatementNode node, RuntimeEvaluationContext context)
    {
        var targetResult = expressionEvaluator.Evaluate(session, node.WithExpression, context);
        if (!targetResult.IsSuccess)
        {
            return targetResult;
        }

        var target = targetResult.Result!;
        var result = withStatement.Evaluate(session, new(), node, target);
        if (observation is { IsSuspended: false })
        {
            Observe(observation, session, node, target, result);
        }

        return result;
    }

    // what the statement is, as its semantics analyze it. That it fails is only said of a target that is known, and of an error that is the code's.
    private void Observe(AnalysisObservation into, IRuntimeSession session, WithStatementNode node, VBTypedValue target, RuntimeSemanticsEvaluationResult result)
    {
        var builder = new SemanticContextFlagsBuilder<WithStatementSemanticContext, WithStatementSemanticFlags>();
        withStatement.Analyze(session, new ConversionOperationSemanticContext(), builder, node, target);

        var flags = builder.Build().Flags;
        if (!target.IsIndeterminate && !result.IsSuccess && result.ErrorInfo?.ErrorId != (int)VBRuntimeErrorId.InternalError)
        {
            flags |= WithStatementSemanticFlags.Failed;
        }

        into.OnStatement(new WithStatementFact(node.Identity, node.SourceLocation, Tokens.With, flags, IsValueKnown: !target.IsIndeterminate));
    }
}

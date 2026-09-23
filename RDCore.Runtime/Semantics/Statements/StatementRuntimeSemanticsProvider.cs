using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Executes one <see cref="StatementNode"/> against a real session/frame, dispatching by the node's own
/// C# type — the statement analogue of <see cref="RDCore.Runtime.Semantics.RuntimeExpressionEvaluator"/>.
/// </summary>
public interface IStatementRuntimeSemanticsProvider
{
    /// <summary>
    /// Executes <paramref name="statement"/>, producing the control-flow outcome the executor loop
    /// reacts to.
    /// </summary>
    RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement);
}

/// <inheritdoc cref="IStatementRuntimeSemanticsProvider"/>
public sealed class StatementRuntimeSemanticsProvider : IStatementRuntimeSemanticsProvider
{
    private readonly RuntimeExpressionEvaluator _expressionEvaluator;
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment;

    public StatementRuntimeSemanticsProvider(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercionProvider, IVerboseMessageBuilder formatterService)
    {
        _expressionEvaluator = expressionEvaluator;
        _letAssignment = new(letCoercionProvider, formatterService);
    }

    /// <inheritdoc/>
    public RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement)
        => statement switch
        {
            AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment => ExecuteLetAssignment(session, context, assignment),
            _ => RuntimeExecutionOutcome.InternalError,
        };

    // MS-VBAL §5.4.3.8. Scoped to a target that already resolves to a plain Symbol, same as
    // BinaryLetAssignmentOperatorRuntimeSemantics itself documents - a member-access or indexed target
    // needs procedure-invocation machinery that doesn't exist yet.
    private RuntimeExecutionOutcome ExecuteLetAssignment(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        if (assignment.Target is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var targetResult = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (targetResult.Symbol is not { } target)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var valueResult = _expressionEvaluator.Evaluate(session, assignment.Value, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(valueResult.ErrorInfo!);
        }

        // the reserved synthetic "__let_op" binary operator - the same shape its own test suite
        // exercises it with: a throwaway node carrying this statement's own identity/location, operands
        // passed directly rather than read back off the node's Children.
        var syntheticOperator = new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, assignment.Identity, assignment.SourceLocation,
            assignment.Target, assignment.Value);
        var result = _letAssignment.Evaluate(session, new(), syntheticOperator, new VBSymbolDescValue(target), valueResult.Result!);

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }
}

using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// Tells on how many of the code paths of a function or a property getter the return value is assigned: the function result variable (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// <para>
/// The return value is assigned by a statement that writes to the name of the procedure (<see cref="ValueExpressionSemanticFlags.AssignmentTarget"/> on an expression
/// that is bound to the procedure itself), which is a fact of the expressions the static pass analyzed: the question is then whether
/// <see cref="ControlFlowGraph.CanReach"/> finds a code path to the end of the activation that does not go through one.
/// </para>
/// <para>
/// A fact is stated only when it is true, so nothing is stated when it could not be established: when the static pass found something wrong with the procedure or did not
/// analyze every expression of it (an assignment may be in what it did not see), or when the graph approximates a jump (<see cref="ControlFlowImprecision.UnresolvedJump"/>,
/// <see cref="ControlFlowImprecision.Subroutines"/>). <see cref="ControlFlowImprecision.Resumption"/> only takes paths away, and a path that is not in the graph cannot
/// be reported as one that is.
/// </para>
/// </remarks>
public static class ReturnValueAnalysis
{
    /// <summary>
    /// Tells about the return value of a procedure.
    /// </summary>
    /// <param name="declaration">The declaration of the procedure.</param>
    /// <param name="model">What the static pass found out about its body.</param>
    /// <param name="instructions">Its lowered body.</param>
    /// <returns>
    /// The fact, or <see langword="null"/> when the procedure does not return a value (it is not a function or a property getter) or the fact could not be established.
    /// </returns>
    public static ReturnValueFact? Of(MemberDeclarationNode declaration, ProcedureSemanticModel model, InstructionList instructions)
    {
        if (declaration.MemberKind is not (MemberKind.Function or MemberKind.PropertyGet) || !model.IsFullyAnalyzed)
        {
            return null;
        }

        var graph = ControlFlowGraph.Of(instructions);
        if (graph.Imprecision.HasFlag(ControlFlowImprecision.UnresolvedJump) || graph.Imprecision.HasFlag(ControlFlowImprecision.Subroutines))
        {
            return null;
        }

        var assigns = instructions.Items.Select(instruction => Assigns(instruction, model)).ToArray();
        var assignment = !graph.CanReach(graph.Exit, instruction => assigns[instruction.Offset])
            ? ReturnValueAssignment.Always
            : instructions.Items.Any(instruction => assigns[instruction.Offset] && graph.IsReachable(instruction.Offset))
                ? ReturnValueAssignment.Sometimes
                : ReturnValueAssignment.Never;

        var name = new SourceLocation(declaration.SourceLocation.Uri, declaration.NameRange ?? declaration.SourceLocation.Range);
        return new ReturnValueFact(assignment, declaration.Name, name, IsEmpty: instructions.Items.IsEmpty);
    }

    // what the statement itself writes to, not what is in the blocks it guards: those are instructions of their own.
    private static bool Assigns(Instruction instruction, ProcedureSemanticModel model)
        => instruction.Node is { } statement
           && ExpressionsOf(statement).Any(node =>
               model.Expressions.TryGetValue(node.Identity, out var fact)
               && fact.Binding == model.Procedure
               && fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget));

    private static IEnumerable<SyntaxNode> ExpressionsOf(SyntaxNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is StatementNode)
            {
                continue;
            }

            yield return child;
            foreach (var descendant in ExpressionsOf(child))
            {
                yield return descendant;
            }
        }
    }
}

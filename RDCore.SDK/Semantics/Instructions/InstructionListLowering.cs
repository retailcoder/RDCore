using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics.Static;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// Lowers a procedure body into an <see cref="InstructionList"/> — <strong>RD-VBAL §3.5</strong>. This
/// is the statement-tree analogue of <see cref="StatementStaticSemanticsEvaluator"/>: where that walker
/// recurses through the whole tree checking types and label references, lowering produces the flat,
/// offset-addressable list a future interpreter drives with a program counter, because <c>GoTo</c>/
/// <c>GoSub</c>/<c>On…GoTo</c>/<c>Resume</c> can jump anywhere in the procedure and a recursive tree
/// walk cannot express that.
/// </summary>
/// <remarks>
/// <strong>Scope of this pass.</strong> Only <paramref name="body"/>'s own direct children are lowered:
/// line labels/numbers (a key, no instruction), and every <see cref="StatementNode"/> as one
/// <see cref="InstructionKind.Simple"/> instruction, except <c>GoTo</c>, <c>On…GoTo</c>, <c>Exit
/// Sub</c>/<c>Exit Function</c>/<c>Exit Property</c>, <c>End</c> and <c>Stop</c>, which get their own
/// <see cref="InstructionKind"/>. A block statement (<c>If</c>/<c>Select Case</c>/a loop/<c>With</c>)
/// therefore lowers as a single opaque <see cref="InstructionKind.Simple"/> instruction today — its
/// nested <c>Body</c> is not walked, and none of its own statements are lowered. Block-statement
/// lowering, synthesized block closers, and <c>GoSub</c>/<c>Return</c>/error-handling instructions are
/// a later slice; this pass never fails on them, it just does not yet give them a dedicated shape.
/// <para>
/// Lowering doubles as a validator for the one static-semantics rule it needs to resolve jump targets
/// at all: every label a jump names must be defined exactly once in the procedure
/// (<strong>MS-VBAL §5.4.1.1</strong>). A jump whose target does not resolve gets a <c>null</c>
/// <see cref="Instruction.Target"/>/<see cref="Instruction.Targets"/> entry and a
/// <see cref="VBCompileErrorId.LabelNotDefined"/> diagnostic; a repeated label definition gets a
/// <see cref="VBCompileErrorId.DuplicateLabelDefinition"/> diagnostic and keeps its first offset. This
/// pass needs no symbol resolver: a label is not a symbol, so <see cref="LabelOperands"/> reads a jump's
/// operand directly off the expression tree, the same way <see cref="StatementStaticSemanticsEvaluator"/>
/// does.
/// </para>
/// </remarks>
public static class InstructionListLowering
{
    /// <summary>
    /// Lowers <paramref name="body"/> — a procedure's top-level statement list — into an
    /// <see cref="InstructionList"/>.
    /// </summary>
    /// <param name="body">
    /// A procedure body: a <see cref="MemberDeclarationNode"/>'s own <c>Children</c>, wrapped in a
    /// <see cref="StatementBlock"/>. A label is scoped to the whole procedure, so passing a nested
    /// block on its own would under-resolve every jump into or out of it.
    /// </param>
    public static InstructionListLoweringResult Lower(StatementBlock body)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var statements = new List<StatementNode>();

        // First pass: assign every statement its dense offset and collect the label table. A label may
        // reference a statement that appears later in source, so no target can be resolved until every
        // label in the procedure has been seen.
        foreach (var child in body.Children)
        {
            switch (child)
            {
                case LineLabelNode label:
                    if (!labels.TryAdd(label.Name, statements.Count))
                    {
                        errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateLabelDefinition, label.SourceLocation, label.Name));
                    }
                    break;
                case StatementNode statement:
                    statements.Add(statement);
                    break;
            }
        }

        // Second pass: build each instruction, resolving jump targets against the now-complete label table.
        var byNode = new Dictionary<SyntaxNodeId, int>();
        var items = ImmutableArray.CreateBuilder<Instruction>(statements.Count);
        for (var offset = 0; offset < statements.Count; offset++)
        {
            var statement = statements[offset];
            byNode[statement.Identity] = offset;
            items.Add(LowerStatement(offset, statement, labels, errors));
        }

        return new InstructionListLoweringResult(new InstructionList(items.MoveToImmutable(), labels, byNode), errors.ToImmutable());
    }

    private static Instruction LowerStatement(int offset, StatementNode statement, IReadOnlyDictionary<string, int> labels, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        switch (statement)
        {
            case GoToStatementNode goTo:
                return new Instruction(offset, statement, InstructionKind.Jump, ResolveLabel(goTo.LabelExpression, labels, errors), ImmutableArray<int?>.Empty);

            case OnGoToStatementNode onGoTo:
                var targets = onGoTo.Labels.Select(label => ResolveLabel(label, labels, errors)).ToImmutableArray();
                return new Instruction(offset, statement, InstructionKind.JumpTable, null, targets);

            case KeywordStatementNode { Token: Tokens.ExitSub or Tokens.ExitFunction or Tokens.ExitProperty }:
                return new Instruction(offset, statement, InstructionKind.ExitProcedure, null, ImmutableArray<int?>.Empty);

            case KeywordStatementNode { Token: Tokens.End }:
                return new Instruction(offset, statement, InstructionKind.Halt, null, ImmutableArray<int?>.Empty);

            case KeywordStatementNode { Token: Tokens.Stop }:
                return new Instruction(offset, statement, InstructionKind.Break, null, ImmutableArray<int?>.Empty);

            default:
                return new Instruction(offset, statement, InstructionKind.Simple, null, ImmutableArray<int?>.Empty);
        }
    }

    private static int? ResolveLabel(ExpressionNode operand, IReadOnlyDictionary<string, int> labels, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (!LabelOperands.TryGetLabelName(operand, out var name))
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, operand.Location,
                "A jump target must be a line label or a line number."));
            return null;
        }

        if (labels.TryGetValue(name, out var target))
        {
            return target;
        }

        errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, operand.Location, name));
        return null;
    }
}

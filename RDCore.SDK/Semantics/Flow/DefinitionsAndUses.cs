using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// Where each variable of a procedure body may be given a value, and where its value may be read: what the data-flow analyses of the body
/// (<see cref="LiveVariables"/>, <see cref="ReachingDefinitions"/>) are made of.
/// </summary>
/// <remarks>
/// <para>
/// A variable is what an expression the static pass bound names, when it is classified as one (<see cref="ExpressionClassification.Variable"/>): a local, a parameter,
/// a field, an implicitly declared name. The result of the procedure (an assignment to the name of a function or a property getter, <strong>MS-VBAL §5.3.1</strong>)
/// is one too, which is only written.
/// </para>
/// <para>
/// The definitions are those of the statements, not what their text looks like: an assignment, the control variable of a loop, <c>ReDim</c>, <c>Erase</c>, a
/// <c>Mid</c> statement, <c>Input #</c>, <c>Line Input #</c>, <c>Get #</c>, and <em>every argument a procedure may take by reference</em>, since the procedure may write
/// to it (<see cref="ValueExpressionSemanticFlags.PassedAsArgument"/>: an argument written <c>ByVal</c>, or in parentheses, is evaluated and passed by value, and is
/// none). A write to a part of a variable - an element, a member of a <c>Type</c> - is a definition that leaves the rest of its value as it was
/// (<see cref="VariableDefinition.IsComplete"/>); a member of an object is the object's to write, and a read of the variable that holds it.
/// </para>
/// <para>
/// Every variable also has a definition at the start of the activation (<see cref="VariableDefinitionKind.Entry"/>): its argument, its initial value, or the value it
/// has there.
/// </para>
/// </remarks>
public sealed class DefinitionsAndUses
{
    private readonly ILookup<int, VariableDefinition> _definitionsAt;
    private readonly ILookup<int, VariableUse> _usesAt;

    private DefinitionsAndUses(InstructionList instructions, ImmutableArray<VariableDefinition> definitions, ImmutableArray<VariableUse> uses, ImmutableHashSet<SemanticId> outliveTheActivation)
    {
        Instructions = instructions;
        Definitions = definitions;
        Uses = uses;
        OutliveTheActivation = outliveTheActivation;
        Variables = [.. definitions.Select(definition => definition.Variable)];
        _definitionsAt = definitions.ToLookup(definition => definition.Offset);
        _usesAt = uses.ToLookup(use => use.Offset);
    }

    /// <summary>The instructions the offsets are of.</summary>
    public InstructionList Instructions { get; }

    /// <summary>Every definition, those at the start of the activation first.</summary>
    public ImmutableArray<VariableDefinition> Definitions { get; }

    /// <summary>Every use.</summary>
    public ImmutableArray<VariableUse> Uses { get; }

    /// <summary>Every variable the body names.</summary>
    public ImmutableHashSet<SemanticId> Variables { get; }

    /// <summary>
    /// The variables whose value may be read once the activation ends: a <c>ByRef</c> parameter (its argument is the caller's), a field or a global, a <c>Static</c>
    /// local, and the result of the procedure.
    /// </summary>
    public ImmutableHashSet<SemanticId> OutliveTheActivation { get; }

    /// <summary>The definitions of the instruction at <paramref name="offset"/>; <c>-1</c> for those at the start of the activation.</summary>
    /// <param name="offset">The offset of an instruction.</param>
    public IEnumerable<VariableDefinition> DefinitionsAt(int offset) => _definitionsAt[offset];

    /// <summary>The uses of the instruction at <paramref name="offset"/>.</summary>
    /// <param name="offset">The offset of an instruction.</param>
    public IEnumerable<VariableUse> UsesAt(int offset) => _usesAt[offset];

    /// <summary>
    /// Finds the definitions and uses of a procedure body.
    /// </summary>
    /// <param name="instructions">The lowered body.</param>
    /// <param name="model">What the static pass found out about its expressions: what each one is bound to.</param>
    /// <param name="scopes">The scopes of the workspace, which tell what kind of variable each binding is.</param>
    public static DefinitionsAndUses Of(InstructionList instructions, ProcedureSemanticModel model, ScopeTree scopes)
        => new Finder(instructions, model, scopes).Find();

    private sealed class Finder(InstructionList instructions, ProcedureSemanticModel model, ScopeTree scopes)
    {
        private readonly List<VariableDefinition> _definitions = [];
        private readonly List<VariableUse> _uses = [];

        // the names the instruction being visited reads.
        private Dictionary<SimpleNameExpressionNode, SemanticId> _collected = [];

        public DefinitionsAndUses Find()
        {
            foreach (var instruction in instructions.Items)
            {
                Visit(instruction);
            }

            var variables = _definitions.Select(definition => definition.Variable).Concat(_uses.Select(use => use.Variable)).Distinct().ToArray();
            var entries = variables.Select(variable => new VariableDefinition(variable, -1, VariableDefinitionKind.Entry, IsComplete: true, Expression: null));
            return new DefinitionsAndUses(instructions, [.. entries, .. _definitions], [.. _uses], [.. variables.Where(OutlivesTheActivation)]);
        }

        private void Visit(Instruction instruction)
        {
            _collected = [];

            // a loop's closer has no statement of its own: it advances the control variable its opener names.
            switch (instruction.Kind)
            {
                case InstructionKind.ForNext when OpenerOf(instruction) is ForStatementNode loop:
                    Write(instruction, loop.ControlExpression, VariableDefinitionKind.LoopControl, complete: true, reads: true);
                    return;
                case InstructionKind.ForEachNext when OpenerOf(instruction) is ForEachStatementNode loop:
                    // past the last element, nothing is assigned.
                    Write(instruction, loop.ControlExpression, VariableDefinitionKind.LoopControl, complete: false, reads: false);
                    return;
            }

            if (instruction.Node is not { } statement)
            {
                return;
            }

            var names = _collected;
            var notRead = new HashSet<SimpleNameExpressionNode>();
            foreach (var expression in ExpressionsOf(statement))
            {
                Collect(instruction, expression, names, isMember: false);
            }

            foreach (var (target, kind, complete, reads) in WritesOf(instruction, statement))
            {
                if (Write(instruction, target, kind, complete, reads) is { } root)
                {
                    notRead.Add(root);
                }
            }

            foreach (var (name, variable) in names.Where(name => !notRead.Contains(name.Key)))
            {
                _uses.Add(new VariableUse(variable, instruction.Offset, name));
            }
        }

        private StatementNode? OpenerOf(Instruction closer)
            => closer.Matching is { } opener ? instructions.Items[opener].Node : null;

        // what a statement evaluates: its own expressions, which are its children (the blocks it guards are instructions of their own), and the target of a ReDim, which is not one.
        private static IEnumerable<SyntaxNode> ExpressionsOf(StatementNode statement)
            => statement is RedimDeclarationNode redim ? [redim.Target, .. redim.Children] : statement.Children;

        private static IEnumerable<(ExpressionNode Target, VariableDefinitionKind Kind, bool Complete, bool Reads)> WritesOf(Instruction instruction, StatementNode statement)
        {
            switch (statement)
            {
                case AssignmentStatementNode assignment:
                    yield return (assignment.Target, VariableDefinitionKind.Assignment, true, false);
                    break;
                case ForStatementNode loop when instruction.Kind is InstructionKind.ForOpener:
                    yield return (loop.ControlExpression, VariableDefinitionKind.LoopControl, true, false);
                    break;
                case ForEachStatementNode loop when instruction.Kind is InstructionKind.ForEachOpener:
                    // a collection with no element assigns nothing.
                    yield return (loop.ControlExpression, VariableDefinitionKind.LoopControl, false, false);
                    break;
                case MidStatementNode mid:
                    yield return (mid.Target, VariableDefinitionKind.PartialWrite, false, true);
                    break;
                case RedimDeclarationNode redim:
                    // ReDim Preserve keeps the elements that are still in bounds.
                    yield return (redim.Target, VariableDefinitionKind.Redimension, !redim.IsPreserve, redim.IsPreserve);
                    break;
                case KeywordStatementNode { Token: Tokens.Erase } erase:
                    foreach (var operand in erase.Inputs.OfType<ExpressionNode>())
                    {
                        yield return (operand, VariableDefinitionKind.Redimension, true, false);
                    }

                    break;
                case KeywordStatementNode { Token: Tokens.Input } input:
                    foreach (var variable in input.Inputs.Skip(1).OfType<ExpressionNode>())
                    {
                        yield return (variable, VariableDefinitionKind.FileInput, true, false);
                    }

                    break;
                case KeywordStatementNode { Token: Tokens.LineInput or Tokens.Get } read when read.Inputs.Length > 1 && read.Inputs[^1] is ExpressionNode variable:
                    yield return (variable, VariableDefinitionKind.FileInput, true, false);
                    break;
            }
        }

        // the names of variables an expression reads, and the arguments it may pass by reference: the names a member access is written with are its owner's to resolve.
        private void Collect(Instruction instruction, SyntaxNode node, Dictionary<SimpleNameExpressionNode, SemanticId> names, bool isMember)
        {
            if (node is StatementNode)
            {
                return;
            }

            if (node is SimpleNameExpressionNode name && !isMember && VariableOf(name, asTarget: false) is { } variable)
            {
                names[name] = variable;
            }

            foreach (var child in node.Children)
            {
                var member = node is MemberAccessExpressionNode access && ReferenceEquals(child, access.Member)
                    || node is DictionaryAccessExpressionNode dictionary && ReferenceEquals(child, dictionary.Member);
                Collect(instruction, child, names, member);
            }

            // once the names it is written with are collected: the variable it passes is read as well.
            if (node is ExpressionNode expression && model.Expressions.TryGetValue(node.Identity, out var fact) && fact.Flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument))
            {
                Write(instruction, expression, VariableDefinitionKind.ByRefArgument, complete: false, reads: true);
            }
        }

        // records the definition a write to the target is, when it is a write to a variable; returns the name of the variable when the write replaces it without reading it.
        private SimpleNameExpressionNode? Write(Instruction instruction, ExpressionNode target, VariableDefinitionKind kind, bool complete, bool reads)
        {
            if (RootOf(instruction, target) is not ({ } name, { } variable, var whole))
            {
                return null;
            }

            var isComplete = complete && whole;
            _definitions.Add(new VariableDefinition(variable, instruction.Offset, whole ? kind : VariableDefinitionKind.PartialWrite, isComplete, target));
            if (reads || !isComplete)
            {
                // the rest of the value is kept, or what is passed is read: the name stays the use it was collected as. What was not collected from this instruction
                // is a use of its own: the counter a loop's closer advances, the variable a With block names for the member written here.
                if ((reads || !whole) && !_collected.ContainsKey(name))
                {
                    _uses.Add(new VariableUse(variable, instruction.Offset, name));
                }

                return null;
            }

            return name;
        }

        // the variable an assignable expression writes to, and whether it writes all of it: an element of an array, a member of a Type, is a part.
        private (SimpleNameExpressionNode? Name, SemanticId? Variable, bool Whole) RootOf(Instruction instruction, ExpressionNode target)
        {
            switch (target)
            {
                case SimpleNameExpressionNode name when VariableOf(name, asTarget: true) is { } variable:
                    return (name, variable, true);
                case IndexExpressionNode index when RootOf(instruction, index.Callee) is ({ } name, { } variable, true) && HoldsElements(name):
                    return (name, variable, false);
                case MemberAccessExpressionNode { Owner: { } owner } when RootOf(instruction, owner) is ({ } name, { } variable, _) && IsUserDefined(name):
                    return (name, variable, false);
                case MemberAccessExpressionNode { Owner: null } when instruction.EnclosingWith is { } with
                                                                    && instructions.Items[with].Node is WithStatementNode block
                                                                    && RootOf(instruction, block.WithExpression) is ({ } name, { } variable, _) && IsUserDefined(name):
                    // a member of the Type the With block is of is written through the variable the block names.
                    return (name, variable, false);
                default:
                    return (null, null, false);
            }
        }

        private SemanticId? VariableOf(SimpleNameExpressionNode name, bool asTarget)
        {
            if (!model.Expressions.TryGetValue(name.Identity, out var fact) || fact.Binding is not { } binding)
            {
                return null;
            }

            if (fact.Classification is ExpressionClassification.Variable)
            {
                return binding;
            }

            // the name of the function, written to, is its result; read, it is a call of it.
            return asTarget && binding == model.Procedure ? binding : null;
        }

        private VBType? TypeOf(SimpleNameExpressionNode name) => model.Expressions.TryGetValue(name.Identity, out var fact) ? fact.DeclaredType : null;

        // an array, or a Variant that may hold one, has elements to write; an object's default member is the object's to write.
        private bool HoldsElements(SimpleNameExpressionNode name) => TypeOf(name) is null or VBArrayType or VBVariantType;

        private bool IsUserDefined(SimpleNameExpressionNode name) => TypeOf(name) is VBUserDefinedType;

        private bool OutlivesTheActivation(SemanticId variable)
        {
            if (variable == model.Procedure)
            {
                return true;
            }

            return SymbolOf(variable) switch
            {
                VBParameterSymbol parameter => parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef,
                VBLocalVariableSymbol local => local.IsStatic,
                // a field, a global, or what is not known: its value is someone else's to read.
                _ => true,
            };
        }

        private Symbol? SymbolOf(SemanticId id)
            => scopes.TryGetScope(id.Uri, out var scope)
                ? scope.SelfAndAncestors().SelectMany(outer => outer.Declarations).FirstOrDefault(symbol => symbol.SemanticId == id)
                : null;
    }
}

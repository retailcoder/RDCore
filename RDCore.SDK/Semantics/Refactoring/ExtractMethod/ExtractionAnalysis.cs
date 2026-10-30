using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.SDK.Semantics.Refactoring.ExtractMethod;

/// <summary>
/// Plans the extraction of a selection: which of the source procedure's variables the new procedure is given, which it gives back, and which it declares as its
/// own - from the definitions and uses on the code paths, never from where the names are on the page (<strong>Extract Method §3.2, §3.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A variable the selection reads, on a path where its value comes from outside the selection, is an <em>input</em>; one the selection writes, whose value is read
/// after it on some path - a later iteration of the loop the selection is in included - is an <em>output</em>; one that is both is passed <c>ByRef</c>. This is
/// what legacy Rubberduck got wrong: it looked at where the names were written, and passed by value a variable that the loop around the selection read again.
/// </para>
/// <para>
/// The variables are the source procedure's own: its locals and parameters, and its result. A field or a global is the same variable in the new procedure, and is
/// not passed.
/// </para>
/// </remarks>
public static class ExtractionAnalysis
{
    /// <summary>
    /// Plans the extraction of a selection.
    /// </summary>
    /// <param name="selection">The statements to extract.</param>
    /// <param name="graph">The code paths of the source procedure.</param>
    /// <param name="accesses">The definitions and uses of its variables.</param>
    /// <param name="model">What the static pass found out about the source procedure: its own identity, what its result is bound to, and its expressions.</param>
    /// <param name="scopes">The scopes of the workspace, which say what each variable is declared as.</param>
    public static ExtractionPlan Plan(ExtractionSelection selection, ControlFlowGraph graph, DefinitionsAndUses accesses, ProcedureSemanticModel model, ScopeTree scopes)
    {
        var region = selection.Region;
        var procedure = model.Procedure;
        var states = ErrorHandlingStates.Of(graph);
        var live = LiveVariables.Of(graph, accesses, states);
        var reaching = ReachingDefinitions.Of(graph, accesses, states);
        var liveAfter = live.LiveAfter(region);

        var uses = accesses.Uses.Where(use => region.Contains(use.Offset)).ToArray();
        var definitions = accesses.Definitions.Where(definition => region.Contains(definition.Offset)).ToArray();
        var touched = uses.Select(use => (use.Variable, use.Offset)).Concat(definitions.Select(definition => (definition.Variable, definition.Offset)))
            .OrderBy(access => access.Offset)
            .Select(access => access.Variable)
            .Distinct();

        // a value given outside the selection - before it, or on an earlier iteration through it - that a read in it may see.
        bool ReadsFromOutside(SemanticId variable, bool startsInside) => uses.Any(use => use.Variable == variable
            && reaching.ReachingBefore(use.Offset, variable).Any(definition => !region.Contains(definition.Offset) && !(startsInside && definition.Kind is VariableDefinitionKind.Entry)));

        var parameters = new List<ExtractionParameter>();
        var locals = new List<ExtractionLocal>();
        var movedOut = new List<SemanticId>();
        var refusals = new List<ExtractionRefusal>();
        var assignsResult = false;
        var readsResult = false;
        foreach (var variable in touched)
        {
            if (variable == procedure)
            {
                assignsResult |= definitions.Any(definition => definition.Variable == variable);
                readsResult |= ReadsFromOutside(variable, startsInside: false);
                continue;
            }

            if (FlowSymbols.SymbolOf(scopes, variable) is not VBLocalVariableSymbol local)
            {
                // a field, a global: the new procedure names the same variable.
                continue;
            }

            var isUndeclared = local is not VBParameterSymbol && local.DeclaredBy.IsImplicit();
            var declaredInside = local is not VBParameterSymbol && !isUndeclared
                && local.Range.Start >= selection.Range.Start && local.Range.End <= selection.Range.End;

            // a variable declared in the selection starts with its initial value, which moves in with the declaration.
            var input = ReadsFromOutside(variable, startsInside: declaredInside);
            var output = definitions.Any(definition => definition.Variable == variable) && liveAfter.Contains(variable);
            var usedOutside = accesses.Uses.Any(use => use.Variable == variable && !region.Contains(use.Offset))
                || accesses.Definitions.Any(definition => definition.Variable == variable && definition.Offset >= 0 && !region.Contains(definition.Offset));

            // a Static's lifetime is the source procedure's: its declaration stays there, wherever it is used.
            if (declaredInside && (usedOutside || local.IsStatic))
            {
                movedOut.Add(variable);
            }

            var type = local.ResolvedType;
            if (local is ParamArrayParameterSymbol)
            {
                // its elements are the caller's variables, which a copy cannot write.
                refusals.AddRange(definitions.Where(definition => definition.Variable == variable)
                    .Select(definition => new ExtractionRefusal(ExtractionRefusalKind.ParamArrayWritten, definition.Expression?.SourceLocation, local.Name)));
                parameters.Add(new ExtractionParameter(variable, local.Name, null, ExtractionPassing.ByVal, ExtractionPassingReason.ParamArray, input, output, MayBeMissing: true));
            }
            else if (local.IsStatic)
            {
                parameters.Add(new ExtractionParameter(variable, local.Name, type, ExtractionPassing.ByRef, ExtractionPassingReason.Static, input, output));
            }
            else if (input || output)
            {
                var (passing, reason) = PassingOf(type, input, output);
                parameters.Add(new ExtractionParameter(variable, local.Name, isUndeclared ? null : type, passing, reason, input, output,
                    MayBeMissing: local is VBParameterSymbol { IsOptional: true, DefaultValue: null, ResolvedType: VBVariantType }, IsUndeclared: isUndeclared));
            }
            else if (!declaredInside || usedOutside)
            {
                // nothing comes in or goes out: the new procedure has a local of its own, which takes the declaration when nothing else uses it.
                locals.Add(new ExtractionLocal(variable, local.Name, isUndeclared ? null : type,
                    MovesDeclaration: !usedOutside && local is not VBParameterSymbol && !isUndeclared, IsUndeclared: isUndeclared));
            }
        }

        var withTarget = WithTargetOf(selection, accesses.Instructions, model, refusals);
        return new ExtractionPlan(selection, [.. parameters], [.. locals], [.. movedOut], assignsResult, readsResult, withTarget, [.. refusals]);
    }

    // arrays and variables of a Type are passed by reference only; a fixed-length string is passed as a String, by reference, so that what is written to it comes back.
    private static (ExtractionPassing Passing, ExtractionPassingReason Reason) PassingOf(VBType? type, bool input, bool output) => type switch
    {
        VBArrayType => (ExtractionPassing.ByRef, ExtractionPassingReason.Array),
        VBUserDefinedType => (ExtractionPassing.ByRef, ExtractionPassingReason.UserDefinedType),
        VBFixedStringType => (ExtractionPassing.ByRef, ExtractionPassingReason.FixedLengthString),
        _ when input && output => (ExtractionPassing.ByRef, ExtractionPassingReason.ReadAndWritten),
        _ when output => (ExtractionPassing.ByRef, ExtractionPassingReason.WrittenOnly),
        _ => (ExtractionPassing.ByVal, ExtractionPassingReason.ReadOnly),
    };

    // the With block the selection is inside of, when an instruction of it uses a member relative to that block: the statements of a selection are siblings of one
    // block, so there is one at most. A With block the selection holds whole is extracted as it is.
    private static ExtractionWithTarget? WithTargetOf(
        ExtractionSelection selection, InstructionList instructions, ProcedureSemanticModel model, List<ExtractionRefusal> refusals)
    {
        var used = selection.Region
            .Select(offset => instructions.Items[offset])
            .Where(instruction => instruction.EnclosingWith is { } with && !selection.Region.Contains(with) && instruction.Node is { } node && UsesAMemberOfTheBlock(node))
            .Select(instruction => instruction.EnclosingWith!.Value)
            .FirstOrDefault(-1);
        if (used < 0 || instructions.Items[used].Node is not WithStatementNode block)
        {
            return null;
        }

        if (block.WithExpression is not SimpleNameExpressionNode name || !model.Expressions.TryGetValue(name.Identity, out var fact) || fact.Binding is not { } variable
            || fact.Classification is not Semantics.ExpressionClassification.Variable)
        {
            refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.WithTargetIsNotAVariable, block.WithExpression.SourceLocation, null));
            return null;
        }

        var passing = fact.DeclaredType is VBUserDefinedType ? ExtractionPassing.ByRef : ExtractionPassing.ByVal;
        return new ExtractionWithTarget(block, variable, name.IdentifierName, fact.DeclaredType, passing);
    }

    // a member written relative to a With block (`.Name`), among the expressions of the statement itself: those of the statements of its blocks are theirs.
    private static bool UsesAMemberOfTheBlock(SyntaxNode node)
        => node is MemberAccessExpressionNode { Owner: null } || node.Children.Any(child => child is not StatementNode && UsesAMemberOfTheBlock(child));
}

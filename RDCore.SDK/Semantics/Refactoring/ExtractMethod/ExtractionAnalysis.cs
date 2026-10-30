using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Flow;

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
    /// <param name="procedure">The source procedure's own identity: what its result is bound to.</param>
    /// <param name="scopes">The scopes of the workspace, which say what each variable is declared as.</param>
    public static ExtractionPlan Plan(ExtractionSelection selection, ControlFlowGraph graph, DefinitionsAndUses accesses, SemanticId procedure, ScopeTree scopes)
    {
        var region = selection.Region;
        var live = LiveVariables.Of(graph, accesses);
        var reaching = ReachingDefinitions.Of(graph, accesses);
        var liveAfter = live.LiveAfter(region);

        var uses = accesses.Uses.Where(use => region.Contains(use.Offset)).ToArray();
        var definitions = accesses.Definitions.Where(definition => region.Contains(definition.Offset)).ToArray();
        var touched = uses.Select(use => (use.Variable, use.Offset)).Concat(definitions.Select(definition => (definition.Variable, definition.Offset)))
            .OrderBy(access => access.Offset)
            .Select(access => access.Variable)
            .Distinct();

        var parameters = new List<ExtractionParameter>();
        var locals = new List<ExtractionLocal>();
        var movedOut = new List<SemanticId>();
        var assignsResult = false;
        foreach (var variable in touched)
        {
            if (variable == procedure)
            {
                assignsResult |= definitions.Any(definition => definition.Variable == variable);
                continue;
            }

            if (FlowSymbols.SymbolOf(scopes, variable) is not VBLocalVariableSymbol local)
            {
                // a field, a global: the new procedure names the same variable.
                continue;
            }

            var declaredInside = local is not VBParameterSymbol && local.DeclaredBy is not LocalDeclarationKind.Implicit
                && local.Range.Start >= selection.Range.Start && local.Range.End <= selection.Range.End;

            // a value given outside the selection - before it, or on an earlier iteration through it - that a read in it may see; a variable declared in it starts
            // with its initial value, which moves in with the declaration.
            var input = uses.Any(use => use.Variable == variable
                && reaching.ReachingBefore(use.Offset, variable).Any(definition => !region.Contains(definition.Offset) && !(declaredInside && definition.Kind is VariableDefinitionKind.Entry)));
            var output = definitions.Any(definition => definition.Variable == variable) && liveAfter.Contains(variable);
            var usedOutside = accesses.Uses.Any(use => use.Variable == variable && !region.Contains(use.Offset))
                || accesses.Definitions.Any(definition => definition.Variable == variable && definition.Offset >= 0 && !region.Contains(definition.Offset));

            if (declaredInside && usedOutside)
            {
                movedOut.Add(variable);
            }

            var type = local.ResolvedType;
            if (local.IsStatic)
            {
                parameters.Add(new ExtractionParameter(variable, local.Name, type, ExtractionPassing.ByRef, ExtractionPassingReason.Static, input, output));
            }
            else if (input || output)
            {
                var (passing, reason) = PassingOf(type, input, output);
                parameters.Add(new ExtractionParameter(variable, local.Name, type, passing, reason, input, output));
            }
            else if (!declaredInside || usedOutside)
            {
                // nothing comes in or goes out: the new procedure has a local of its own, which takes the declaration when nothing else uses it.
                locals.Add(new ExtractionLocal(variable, local.Name, type, MovesDeclaration: !usedOutside && local is not VBParameterSymbol));
            }
        }

        return new ExtractionPlan(selection, [.. parameters], [.. locals], [.. movedOut], assignsResult);
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
}

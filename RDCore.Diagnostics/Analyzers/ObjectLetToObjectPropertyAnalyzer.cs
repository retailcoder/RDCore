using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11047</c>: a <c>Let</c> gives an object to a property whose value parameter is a class or <c>Object</c>. MS-VBA passes the object itself, as a
/// <c>Set</c> would, where <strong>MS-VBAL §5.5.1.1</strong> makes the assignment a type mismatch, and <strong>§5.4.3.8</strong> with
/// <strong>§5.5.1.2.13</strong> evaluates the object's default member (error 424, 91 for <c>Nothing</c>).
/// </summary>
/// <remarks>
/// The analyzer finds the <c>Let</c> in the syntax tree, and reads the <c>ExpressionFactDto</c> of the property its target names (the callee of an index, or
/// the target itself): a property bound early, outside the procedure the <c>Let</c> is in, whose <c>Property Let</c> takes a class or <c>Object</c>
/// (<c>LetValueTypeKind</c>). The value's fact is of a class or <c>Object</c>, not an array. A value declared <c>Variant</c> is not reported: whether it holds
/// an object is known only when it runs.
/// </remarks>
internal sealed class ObjectLetToObjectPropertyAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        // the property each Let names, and the value it gives it.
        var lets = module.Descendants().OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet)
            .Select(assignment => (Property: assignment.Target is IndexExpressionNode { Callee: var called } ? called.Identity : assignment.Target.Identity,
                Value: assignment.Value.Identity))
            .ToArray();

        return semantics.Procedures.SelectMany(procedure =>
            {
                var facts = procedure.Expressions.DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
                return lets
                    .Select(let => facts.TryGetValue(let.Property, out var property) && facts.TryGetValue(let.Value, out var value) ? (property, value) : default)
                    .Where(let => let.property is not null && GivesAnObject(procedure, let.property, let.value))
                    .Select(let => let.property);
            })
            .GroupBy(target => target.Location.Range.Start)
            .Select(target => target.First())
            .OrderBy(target => target.Location.Range.Start)
            .Select(target => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectLetToObjectProperty.ToCode(),
                target.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToObjectProperty_Message, SymbolUri.NameOf(target.Binding!))));
    }

    private static bool GivesAnObject(ProcedureSemanticsDto procedure, ExpressionFactDto property, ExpressionFactDto value)
        => property is { Error: null, Classification: ExpressionClassification.Property, LetValueTypeKind: DeclaredTypeKind.Class or DeclaredTypeKind.Object, Binding: { } binding }
            && binding.AbsoluteUri != procedure.Procedure.AbsoluteUri
            && value is { Error: null, TypeKind: DeclaredTypeKind.Class or DeclaredTypeKind.Object, IsArray: false };
}

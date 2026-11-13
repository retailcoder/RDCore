using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00201</c>: an <c>As Integer</c> clause, the 16-bit type (<strong>MS-VBAL §2.1</strong> Data Values and Value Types).
/// </summary>
/// <remarks>
/// <para>
/// A <c>Declare</c> statement is not looked into: the types it states are the ones the external procedure takes, which are not the declaring module's to choose.
/// Neither are the parameters and the return type of a member that implements an interface or handles an event, which the interface or the event states. That
/// a member is one is a fact of the declaration (<see cref="DeclarationFact.Role"/>) that the host vouches for; the variables of its body are reported.
/// </para>
/// <para>
/// The fix (<see cref="DiagnosticFixes.Edit"/>) changes the type to <c>Long</c>, which is what it is for, where no other code has to change with it: no
/// expression passes the variable or the parameter as an argument, which a <c>ByRef</c> parameter of type <c>Integer</c> takes only as an
/// <c>Integer</c>; a parameter is <c>ByVal</c>, which any argument it was given still converts to; and the member is neither a property whose other
/// procedures state the type too, nor a public member of a class, which another class may implement. A member of a <c>Type</c> and a <c>ReDim</c> are not fixed.
/// </para>
/// </remarks>
internal sealed class IntegerDataTypeAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            return [];
        }

        var declarations = context.Semantics?.Declarations ?? [];
        var chosenElsewhere = declarations
            .Where(declaration => declaration is
                { Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler, Kind: DeclarationKind.Procedure or DeclarationKind.Property })
            .Select(declaration => declaration.Location.Range.Start)
            .ToArray();
        var fixes = context.Semantics is null ? null : new Fixes(module, declarations, IsStandardModule(context.Document));

        return module.Children
            .Where(member => member is not ExternalMemberDeclarationNode)
            .SelectMany(member => member is MemberDeclarationNode procedure && chosenElsewhere.Any(start => Within(procedure.SourceLocation.Range, start))
                ? procedure.Children.Except(procedure.HeaderChildren).SelectMany(child => child.Descendants().Prepend(child))
                : member.Descendants())
            .OfType<AsTypeExpressionNode>()
            .Where(IsInteger)
            .Select(asType => new AnalyzerFinding(
                RDCoreDiagnosticId.IntegerDataTypeDeclaration.ToCode(), asType.Location.Range, DiagnosticSeverity.Information, RDCoreDiagnosticsResources.IntegerDataTypeDeclaration_Message)
            {
                Fixes = fixes?.Of(asType) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
            });
    }

    // `VBA.Integer` is the same type; any other qualifier names a type of some other library that happens to be called that.
    private static bool IsInteger(AsTypeExpressionNode asType)
        => string.Equals(asType.TypeName, VBTypeNames.VBInteger, StringComparison.OrdinalIgnoreCase)
           && (asType.QualifierName is null || string.Equals(asType.QualifierName, "VBA", StringComparison.OrdinalIgnoreCase));

    private static bool IsStandardModule(Uri document)
        => string.Equals(Path.GetExtension(document.AbsolutePath), ".bas", StringComparison.OrdinalIgnoreCase);

    private static bool Within(SourceRange range, SourcePosition position)
        => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;

    /// <summary>
    /// Where the type of a clause changes to <c>Long</c> with nothing else to change: the declaration it belongs to, found by the node that holds the clause.
    /// </summary>
    private sealed class Fixes
    {
        private readonly Dictionary<SyntaxNode, SyntaxNode> _parents = new(ReferenceEqualityComparer.Instance);
        private readonly DeclarationFactDto[] _declarations;
        private readonly MemberDeclarationNode[] _properties;
        private readonly bool _isStandardModule;

        public Fixes(ModuleNode module, IEnumerable<DeclarationFactDto> declarations, bool isStandardModule)
        {
            _declarations = [.. declarations];
            _isStandardModule = isStandardModule;
            _properties = [.. module.Children.OfType<MemberDeclarationNode>().Where(IsProperty)];
            foreach (var node in module.Descendants().Prepend(module))
            {
                foreach (var child in node.Children)
                {
                    _parents.TryAdd(child, node);
                }
            }
        }

        public EditFix? Of(AsTypeExpressionNode asType)
        {
            var holder = _parents.GetValueOrDefault(asType);
            var member = Enclosing(asType);
            var changes = holder switch
            {
                // the return type: a caller that is given a Long where it was given an Integer compiles as it did.
                MemberDeclarationNode procedure when ReferenceEquals(procedure, member) => IsChosenHere(procedure),
                ParameterDeclarationNode parameter => member is not null && IsChosenHere(member)
                    && parameter.ParameterKind is ParameterKind.ExplicitByVal or ParameterKind.ImplicitByVal
                    && NeverPassed(DeclarationKind.Parameter, parameter.SourceLocation.Range),
                ConstantDeclarationNode => member?.MemberKind is not MemberKind.UserDefinedType,
                VariableDeclarationNode or VBTypedDeclarationExpressionNode => member?.MemberKind is not MemberKind.UserDefinedType
                    && (member is not null || _isStandardModule || holder is VariableDeclarationNode { AccessModifier: AccessModifier.Private })
                    && NeverPassed(DeclarationKind.Variable, holder.SourceLocation.Range),
                _ => false,
            };

            if (!changes)
            {
                return null;
            }

            var range = asType.Location.Range;
            return new EditFix
            {
                Title = RDCoreDiagnosticsResources.IntegerDataTypeDeclaration_Fix,
                Edits =
                [
                    new TextEdit
                    {
                        Range = new Range(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character),
                        NewText = $"As {(asType.QualifierName is { } qualifier ? qualifier + "." : string.Empty)}{VBTypeNames.VBLong}{(asType.IsArrayDef ? "()" : string.Empty)}",
                    },
                ],
            };
        }

        private MemberDeclarationNode? Enclosing(SyntaxNode node)
        {
            for (var parent = _parents.GetValueOrDefault(node); parent is not null; parent = _parents.GetValueOrDefault(parent))
            {
                if (parent is MemberDeclarationNode member)
                {
                    return member;
                }
            }

            return null;
        }

        // the signature is the member's own: not an event's, not one that the other procedures of a property state too, and not one of a class's public
        // members, which a class that implements it states as well.
        private bool IsChosenHere(MemberDeclarationNode member)
            => member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet
               && (_isStandardModule || member.AccessModifier is AccessModifier.Private)
               && !(IsProperty(member) && _properties.Any(other => !ReferenceEquals(other, member) && string.Equals(other.Name, member.Name, StringComparison.OrdinalIgnoreCase)));

        private static bool IsProperty(MemberDeclarationNode member)
            => member.MemberKind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

        // every reference is known, and none passes it as an argument, which a ByRef parameter of type Integer takes only as an Integer.
        private bool NeverPassed(DeclarationKind kind, SourceRange declared)
            => _declarations.Any(declaration => declaration.Kind == kind
                && Within(declared, declaration.Location.Range.Start)
                && declaration.References is { PassedAsArguments: 0 });
    }
}

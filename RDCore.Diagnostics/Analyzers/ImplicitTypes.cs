using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// What the syntax tree says of the type of a declaration that is written without one (<strong>MS-VBAL §5.2.2</strong>).
/// </summary>
internal static class ImplicitTypes
{
    /// <summary>
    /// Whether a declaration states its type, with an <c>As</c> clause or with a type-declaration character.
    /// </summary>
    /// <param name="declaration">The declaration, whose <c>As</c> clause is among its children.</param>
    /// <param name="typeHint">The type-declaration character of its name, if there is one.</param>
    public static bool IsTypeStated(SyntaxNode declaration, string? typeHint)
        => !string.IsNullOrEmpty(typeHint) || declaration.Children.OfType<AsTypeExpressionNode>().Any();

    /// <summary>
    /// Whether a name that is declared without a type is a <c>Variant</c>: it is, unless a <c>Def&lt;Type&gt;</c> directive of the module covers the name.
    /// </summary>
    /// <param name="module">The module the name is declared in.</param>
    /// <param name="name">The declared name.</param>
    public static bool IsVariant(ModuleNode module, string name)
    {
        var directive = TypeDefDirectiveNode.Covering(module.Children.OfType<TypeDefDirectiveNode>(), name);
        return directive is null || directive.TypeName == VBTypeNames.VBVariant;
    }
}

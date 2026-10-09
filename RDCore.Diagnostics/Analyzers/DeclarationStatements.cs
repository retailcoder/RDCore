using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// The declarations of a <c>Dim</c>, <c>Private</c>, <c>Public</c>, <c>Static</c> or <c>Const</c> statement, which the syntax tree has as siblings: one node for each name.
/// </summary>
/// <remarks>
/// The first name of a statement is written with the keywords in front of it, and a name that follows is written alone (<see cref="VariableDeclarationNode"/>,
/// <see cref="ConstantDeclarationNode"/>): a declaration whose name is where it begins belongs to the statement of the declaration before it.
/// </remarks>
internal static class DeclarationStatements
{
    /// <summary>
    /// The declarations among <paramref name="siblings"/> that one statement makes, for each statement.
    /// </summary>
    /// <param name="siblings">The children of a node, in the order they are written.</param>
    public static IEnumerable<IReadOnlyList<SyntaxNode>> In(IEnumerable<SyntaxNode> siblings)
    {
        List<SyntaxNode>? statement = null;
        foreach (var node in siblings)
        {
            if (!TryGetRanges(node, out var name, out var range))
            {
                if (statement is not null)
                {
                    yield return statement;
                    statement = null;
                }

                continue;
            }

            if (statement is not null && name.Start == range.Start)
            {
                statement.Add(node);
                continue;
            }

            if (statement is not null)
            {
                yield return statement;
            }

            statement = [node];
        }

        if (statement is not null)
        {
            yield return statement;
        }
    }

    /// <summary>
    /// Where a statement that makes the <paramref name="declarations"/> is written: from the first of them to the end of the last.
    /// </summary>
    public static SourceRange RangeOf(IReadOnlyList<SyntaxNode> declarations)
        => new(declarations[0].SourceLocation.Range.Start, declarations[^1].SourceLocation.Range.End);

    // an enum member is declared by its name alone, and is not a statement of its own.
    private static bool TryGetRanges(SyntaxNode node, out SourceRange name, out SourceRange range)
    {
        switch (node)
        {
            case VariableDeclarationNode { NameRange: { } variableName } variable:
                name = variableName;
                range = variable.SourceLocation.Range;
                return true;

            case ConstantDeclarationNode { ConstKind: not ConstKind.EnumMember, NameRange: { } constantName } constant:
                name = constantName;
                range = constant.Location.Range;
                return true;

            default:
                name = range = default;
                return false;
        }
    }
}

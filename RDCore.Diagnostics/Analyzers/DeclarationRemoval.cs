using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// The fix that removes a declaration of a variable or a constant that nothing refers to: the name and its clauses from a statement that declares others, or
/// the statement from a line of its own.
/// </summary>
internal static class DeclarationRemoval
{
    /// <summary>
    /// The edit that removes <paramref name="declaration"/>; <see langword="null"/> where it cannot be removed alone: its statement shares its line with
    /// another statement, or its name is not where the syntax tree says.
    /// </summary>
    /// <param name="parse">The parse of the module, with its trivia.</param>
    /// <param name="declaration">A <see cref="VariableDeclarationNode"/> or a <see cref="ConstantDeclarationNode"/>.</param>
    /// <param name="title">The title of the fix.</param>
    public static EditFix? FixOf(ModuleParseResult parse, SyntaxNode declaration, string title)
    {
        if (parse.SyntaxTree is not { } module
            || module.Descendants().Prepend(module).FirstOrDefault(node => !node.Children.IsDefaultOrEmpty && node.Children.Contains(declaration)) is not { } parent
            || DeclarationStatements.In(parent.Children).FirstOrDefault(statement => statement.Contains(declaration)) is not { } declarations)
        {
            return null;
        }

        var index = IndexOf(declarations, declaration);
        Range? removed = declarations.Count switch
        {
            1 => LinesOf(parse, module, DeclarationStatements.RangeOf(declarations)),

            // ", Name As Type" after the one before it; "Name As Type, " before the one after it, which the keywords then declare.
            _ when index > 0 => RangeOf(declarations[index - 1].SourceLocation.Range.End, declaration.SourceLocation.Range.End),
            _ => NameOf(declarations[0]) is { } first && NameOf(declarations[1]) is { } second ? RangeOf(first.Start, second.Start) : null,
        };

        return removed is null ? null : new EditFix { Title = title, Edits = [new TextEdit { Range = removed, NewText = string.Empty }] };
    }

    private static int IndexOf(IReadOnlyList<SyntaxNode> declarations, SyntaxNode declaration)
    {
        for (var index = 0; index < declarations.Count; index++)
        {
            if (Equals(declarations[index], declaration))
            {
                return index;
            }
        }

        return -1;
    }

    // the lines of a statement that is written on lines of its own, with a comment on them at most; a comment there goes with it.
    private static Range? LinesOf(ModuleParseResult parse, ModuleNode module, SourceRange statement)
    {
        bool IsOnTheLines(SourcePosition position) => position.Line >= statement.Start.Line && position.Line <= statement.End.Line;
        bool IsInTheStatement(SourceRange range) => range.Start.CompareTo(statement.Start) >= 0 && range.End.CompareTo(statement.End) <= 0;

        var shared = module.Descendants().Any(node => !IsInTheStatement(node.SourceLocation.Range)
            && (IsOnTheLines(node.SourceLocation.Range.Start) || IsOnTheLines(node.SourceLocation.Range.End)));
        return shared ? null : new Range(statement.Start.Line, 0, statement.End.Line + 1, 0);
    }

    private static SourceRange? NameOf(SyntaxNode declaration) => declaration switch
    {
        VariableDeclarationNode variable => variable.NameRange,
        ConstantDeclarationNode constant => constant.NameRange,
        _ => null,
    };

    private static Range RangeOf(SourcePosition start, SourcePosition end) => new(start.Line, start.Character, end.Line, end.Character);
}

using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// A line label and the places its procedure names it: where it is defined, and each <c>GoTo</c>, <c>GoSub</c>, <c>On...GoTo</c>, <c>On...GoSub</c>,
/// <c>Resume</c> and <c>On Error GoTo</c> that names it.
/// </summary>
/// <param name="Name">The label, as it is defined.</param>
/// <param name="Procedure">The procedure it is a label of.</param>
/// <param name="Here">Where it is named at the position it was found at.</param>
/// <param name="Occurrences">Every place its procedure names it, its definitions included, in the order of the text.</param>
/// <param name="Others">The other labels its procedure defines, each where it is defined.</param>
internal sealed record class LabelOccurrences(
    string Name, MemberDeclarationNode Procedure, SourceRange Here, ImmutableArray<SourceRange> Occurrences, ImmutableArray<(string Name, SourcePosition At)> Others);

// MS-VBAL §5.4.1.1, §5.4.2.12, §5.4.2.14, §5.4.2.16, §5.4.4.1, §5.4.4.2
// a label is not a symbol: it is a name of its procedure only, which the statements that transfer execution to it name. A line number is not a name, and is
// never renamed (Erl reads it).
internal static partial class LabelRename
{
    private static readonly MemberKind[] Procedures = [MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet];

    // the label named at a position of a module, with the places its procedure names it; null when no label is named there.
    public static LabelOccurrences? At(RenameModule module, SourcePosition position)
    {
        if (module.Syntax.Children.OfType<MemberDeclarationNode>()
                .FirstOrDefault(member => Procedures.Contains(member.MemberKind) && Contains(member.SourceLocation.Range, position)) is not { } procedure)
        {
            return null;
        }

        var lines = Lines().Split(module.Source);
        var named = Named(procedure, lines).ToList();
        if (named.FirstOrDefault(name => Contains(name.Range, position)) is not ({ } label, var here, _))
        {
            return null;
        }

        var same = named.Where(name => string.Equals(name.Label, label, StringComparison.OrdinalIgnoreCase)).ToList();
        var defined = same.FirstOrDefault(name => name.IsDefinition).Label ?? label;
        return new(defined, procedure, here, [.. same.Select(name => name.Range).Distinct().OrderBy(range => range.Start)],
            [.. named.Where(name => name.IsDefinition && !string.Equals(name.Label, label, StringComparison.OrdinalIgnoreCase))
                .Select(name => (name.Label, name.Range.Start))]);
    }

    // the labels a range of a module defines, each where it is written.
    public static IEnumerable<SourcePosition> DefinitionsIn(RenameModule module, SourceRange range)
    {
        var lines = Lines().Split(module.Source);
        return module.Syntax.Children.OfType<MemberDeclarationNode>()
            .Where(member => Procedures.Contains(member.MemberKind))
            .SelectMany(procedure => Named(procedure, lines))
            .Where(name => name.IsDefinition && Contains(range, name.Range.Start))
            .Select(name => name.Range.Start);
    }

    // what a name of a label is written as where it is: in brackets when it was written in them.
    public static string WrittenAs(RenameModule module, SourceRange range, string newName)
        => TextOf(Lines().Split(module.Source), range).StartsWith('[') ? $"[{newName}]" : newName;

    // every label a procedure defines and every one its statements name, with the range of the name as it is written.
    private static IEnumerable<(string Label, SourceRange Range, bool IsDefinition)> Named(SyntaxNode procedure, string[] lines)
    {
        foreach (var (child, _) in NameOccurrences.Walk(procedure))
        {
            switch (child)
            {
                case LineLabelNode label when !IsLineNumber(label.Name) && NameRange(label, lines) is { } range:
                    yield return (label.Name, range, true);
                    break;
                case GoToStatementNode goTo when goTo.LabelExpression is SimpleNameExpressionNode name:
                    yield return (name.IdentifierName, name.Location.Range, false);
                    break;
                case GoSubStatementNode goSub when goSub.LabelExpression is SimpleNameExpressionNode name:
                    yield return (name.IdentifierName, name.Location.Range, false);
                    break;
                case OnErrorGoToStatementNode onError when onError.LabelExpression is SimpleNameExpressionNode name:
                    yield return (name.IdentifierName, name.Location.Range, false);
                    break;
                case ResumeStatementNode resume when resume.LabelExpression is SimpleNameExpressionNode name:
                    yield return (name.IdentifierName, name.Location.Range, false);
                    break;
                case OnGoToStatementNode onGoTo:
                    foreach (var name in onGoTo.Labels.OfType<SimpleNameExpressionNode>())
                    {
                        yield return (name.IdentifierName, name.Location.Range, false);
                    }

                    break;
                case OnGoSubStatementNode onGoSub:
                    foreach (var name in onGoSub.Labels.OfType<SimpleNameExpressionNode>())
                    {
                        yield return (name.IdentifierName, name.Location.Range, false);
                    }

                    break;
            }
        }
    }

    // the name of a label as it is written at the start of its definition, without the colon after it.
    private static SourceRange? NameRange(LineLabelNode label, string[] lines)
    {
        var start = label.SourceLocation.Range.Start;
        if (start.Line >= lines.Length)
        {
            return null;
        }

        var written = lines[start.Line][start.Character..];
        var length = written.StartsWith('[') ? written.IndexOf(']') + 1 : label.Name.Length;
        return length <= 0 || length > written.Length ? null : new SourceRange(start, start with { Character = start.Character + length });
    }

    // a line number, which a label node can be written as too: no identifier starts with a digit.
    private static bool IsLineNumber(string name) => name.Length > 0 && (char.IsAsciiDigit(name[0]) || name[0] == '-');

    private static string TextOf(string[] lines, SourceRange range)
        => range.Start.Line == range.End.Line && range.Start.Line < lines.Length && range.End.Character <= lines[range.Start.Line].Length
            ? lines[range.Start.Line][range.Start.Character..range.End.Character]
            : string.Empty;

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex Lines();
}

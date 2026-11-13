using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// The edits a fix is made of, placed by the syntax tree's positions: the fix of a finding says what to write where, and never needs the text around it.
/// </summary>
/// <remarks>
/// An analyzer is given the syntax tree of the module and not its text, so an edit is placed only where the tree says what is there: at the start or the end
/// of a node, or over a keyword whose length the tree implies. Where it does not tell, the fix is not offered.
/// </remarks>
internal static class SyntaxEdits
{
    /// <summary>
    /// Text written at a position.
    /// </summary>
    public static TextEdit Insert(SourcePosition at, string text)
        => new() { Range = new Range(at.Line, at.Character, at.Line, at.Character), NewText = text };

    /// <summary>
    /// Text written in place of what a range holds.
    /// </summary>
    public static TextEdit Replace(SourceRange range, string text) => new() { Range = RangeOf(range), NewText = text };

    /// <summary>
    /// What a range holds, removed.
    /// </summary>
    public static TextEdit Remove(SourceRange range) => Replace(range, string.Empty);

    /// <summary>
    /// The range of the characters from a position on, on its line.
    /// </summary>
    public static SourceRange Following(SourcePosition at, int length) => new(at, new SourcePosition(at.Line, at.Character + length));

    /// <summary>
    /// The number of characters from one position to another on the same line, or <c>-1</c> when they are not on the same line.
    /// </summary>
    public static int Width(SourcePosition from, SourcePosition to) => from.Line == to.Line ? to.Character - from.Character : -1;

    /// <summary>
    /// Whether a position is in a range, its ends included.
    /// </summary>
    public static bool Contains(SourceRange range, SourcePosition at)
        => (at.Line > range.Start.Line || at.Line == range.Start.Line && at.Character >= range.Start.Character)
            && (at.Line < range.End.Line || at.Line == range.End.Line && at.Character <= range.End.Character);

    /// <summary>
    /// A fix of one or more edits, under a title.
    /// </summary>
    public static DiagnosticFixes Fix(string title, params TextEdit[] edits) => new() { Edit = new EditFix { Title = title, Edits = edits } };

    private static Range RangeOf(SourceRange range) => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
}

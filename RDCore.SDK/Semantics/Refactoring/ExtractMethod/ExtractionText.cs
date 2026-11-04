using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Semantics.Refactoring.ExtractMethod;

/// <summary>
/// The text of a module, read and changed by the positions its syntax tree has: zero-based lines, and characters in them.
/// </summary>
internal sealed class ExtractionText
{
    private readonly string _text;
    private readonly int[] _lineStarts;

    public ExtractionText(string text)
    {
        _text = text;
        NewLine = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        _lineStarts = [.. starts];
    }

    /// <summary>The line break the module is written with.</summary>
    public string NewLine { get; }

    public int OffsetOf(SourcePosition position)
        => Math.Min(_text.Length, _lineStarts[Math.Min(position.Line, _lineStarts.Length - 1)] + position.Character);

    public string Of(SourceRange range) => _text[OffsetOf(range.Start)..OffsetOf(range.End)];

    /// <summary>What is written on a line before its first character that is not a space or a tab.</summary>
    public string IndentationOf(int line)
    {
        var start = _lineStarts[line];
        var end = start;
        while (end < _text.Length && _text[end] is ' ' or '\t')
        {
            end++;
        }

        return _text[start..end];
    }

    /// <summary>Whether nothing but spaces and tabs is written between the start of the position's line and the position.</summary>
    public bool OnlyIndentationBefore(SourcePosition position) => IndentationOf(position.Line).Length >= position.Character;

    /// <summary>Whether nothing but spaces and tabs is written after the position, up to the end of its line.</summary>
    public bool OnlySpaceAfter(SourcePosition position)
    {
        for (var index = OffsetOf(position); index < _text.Length && _text[index] is not ('\r' or '\n'); index++)
        {
            if (_text[index] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The start of the line after the position's, or the end of the text when it is the last.</summary>
    public SourcePosition StartOfNextLine(SourcePosition position)
        => position.Line + 1 < _lineStarts.Length ? new SourcePosition(position.Line + 1, 0) : new SourcePosition(position.Line, _text.Length - _lineStarts[position.Line]);

    /// <summary>The text of a range with the edits made in it: each edit is within the range, and none overlaps another.</summary>
    public string Of(SourceRange range, IEnumerable<ExtractionTextEdit> edits)
    {
        var start = OffsetOf(range.Start);
        var text = Of(range);
        foreach (var edit in edits.OrderByDescending(edit => edit.Range.Start))
        {
            var from = OffsetOf(edit.Range.Start) - start;
            var to = OffsetOf(edit.Range.End) - start;
            text = string.Concat(text.AsSpan(0, from), edit.NewText, text.AsSpan(to));
        }

        return text;
    }

    /// <summary>The module with the edits made: none overlaps another.</summary>
    public static string Apply(string text, IEnumerable<ExtractionTextEdit> edits)
    {
        var module = new ExtractionText(text);
        var whole = new SourceRange(new SourcePosition(0, 0), new SourcePosition(module._lineStarts.Length - 1, text.Length - module._lineStarts[^1]));
        return module.Of(whole, edits);
    }
}

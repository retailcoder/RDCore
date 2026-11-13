using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01004</c>: an <c>Else</c>, <c>Case</c>, <c>Do</c>, <c>For</c>, <c>For Each</c> or <c>While</c> block that has no statement (<strong>MS-VBAL §5.4.2</strong>).
/// </summary>
/// <remarks>
/// A comment, a line label and a declaration are no statement. A <c>Case Else</c> is not judged: an empty one states that the other values are left alone. The
/// finding is the header of the block, its keyword and what it states. An <c>Else</c> with nothing at all in it, written on lines of its own up to the
/// <c>End If</c>, comes with the fix that removes those lines: an empty <c>Else</c> runs nothing (<strong>MS-VBAL §5.4.2.8</strong>), so the program does the
/// same without it. The other blocks have no such fix: a loop still evaluates its header, and a <c>Case</c> keeps its values from the cases after it.
/// </remarks>
internal sealed class EmptyCodeBlockAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        var nodes = module.Descendants().ToArray();
        var ifs = nodes.OfType<IfBlockStatementNode>().Where(block => block.ElseBlock is not null).ToDictionary(block => block.ElseBlock!.Identity);
        foreach (var node in nodes)
        {
            if (Empty(node) is ({ } range, { } keyword))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.EmptyCodeBlock.ToCode(),
                    range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.EmptyCodeBlock_Message, keyword))
                {
                    Fixes = node is ElseBlockStatementNode block && ifs.TryGetValue(block.Identity, out var owner) && RemovalOf(context.ParseResult, nodes, owner, block) is { } fix
                        ? new DiagnosticFixes { Edit = fix }
                        : null,
                };
            }
        }
    }

    private static (SourceRange? Range, string? Keyword) Empty(SyntaxNode node) => node switch
    {
        ElseBlockStatementNode block when block.Body.HasNoStatement() => (EmptyBlocks.Keyword(block, "Else".Length), "Else"),
        CaseExpressionStatementNode block when block.Block.HasNoStatement() && !block.RangeClauses.IsDefaultOrEmpty
            => (EmptyBlocks.Header(block, block.RangeClauses[^1]), "Case"),
        DoWhileLoopStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.ConditionExpression), "Do"),
        DoUntilLoopStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.ConditionExpression), "Do"),
        DoLoopWhileStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Keyword(loop, "Do".Length), "Do"),
        DoLoopUntilStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Keyword(loop, "Do".Length), "Do"),
        DoLoopStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Keyword(loop, "Do".Length), "Do"),
        ForStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.StepExpression ?? loop.EndExpression), "For"),
        ForEachStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.CollectionExpression), "For Each"),
        WhileWendStatementNode loop when loop.Body.HasNoStatement() => (EmptyBlocks.Header(loop, loop.ConditionExpression), "While"),
        _ => (null, null),
    };

    // the lines from the Else to the End If, when nothing else is written on them: no label, declaration or comment in the Else, no statement of the If
    // before it on its line, no #If directive.
    private static EditFix? RemovalOf(ModuleParseResult parse, SyntaxNode[] nodes, IfBlockStatementNode owner, ElseBlockStatementNode block)
    {
        if (!block.Body.Children.IsDefaultOrEmpty)
        {
            return null;
        }

        var first = block.SourceLocation.Range.Start.Line;
        var last = owner.SourceLocation.Range.End.Line - 1;
        if (last < first)
        {
            return null;
        }

        var outer = owner.SourceLocation.Range;
        var start = block.SourceLocation.Range.Start;
        bool Touches(SyntaxNode node) => node.SourceLocation.Range is var range && range.Start.Line <= last && range.End.Line >= first;
        // a block before the Else, such as an ElseIf, ends with the line break and the indentation in front of the Else.
        bool EndsBefore(SyntaxNode node) => node.SourceLocation.Range is var range && range.Start.Line < first && range.End <= start;
        bool Encloses(SyntaxNode node) => node.SourceLocation.Range is var range && range.Start <= outer.Start && range.End >= outer.End;

        if (nodes.Any(node => !ReferenceEquals(node, block) && !Encloses(node) && !EndsBefore(node) && Touches(node)) || parse.Trivia.Any(Touches) || parse.PrecompilerTrivia.Any(Touches))
        {
            return null;
        }

        return new EditFix
        {
            Title = RDCoreDiagnosticsResources.EmptyElseBlock_Fix,
            Edits = [new TextEdit { Range = new Range(first, 0, last + 1, 0), NewText = string.Empty }],
        };
    }
}

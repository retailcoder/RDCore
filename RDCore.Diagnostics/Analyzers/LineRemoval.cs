using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Analyzers;

// the fix that removes a statement or a directive with the lines it is written on, when nothing else is written there: no other statement, label or
// declaration, and no block that holds it begins or ends there. A comment on those lines goes with it.
internal static class LineRemoval
{
    public static EditFix? Of(SyntaxNode node, IReadOnlyCollection<SyntaxNode> nodes, string title)
    {
        var range = node.SourceLocation.Range;
        var (first, last) = (range.Start.Line, range.End.Line);
        foreach (var other in nodes)
        {
            var around = other.SourceLocation.Range;
            var elsewhere = around.Start.Line > last || around.End.Line < first;
            var part = around.Start >= range.Start && around.End <= range.End;
            var holder = around.Start.Line < first && around.End.Line > last;
            if (!ReferenceEquals(other, node) && other is not ModuleNode && !elsewhere && !part && !holder)
            {
                return null;
            }
        }

        return new EditFix { Title = title, Edits = [new TextEdit { Range = new Range(first, 0, last + 1, 0), NewText = string.Empty }] };
    }
}

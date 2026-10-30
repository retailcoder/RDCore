using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring.ExtractMethod;
using RDCore.Tests.Semantics.Flow;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The plan of extracting lines of a procedure <c>Run</c>, as <see cref="FlowFixture"/> analyzes it.
/// </summary>
internal static class ExtractionFixture
{
    /// <summary>The plan of extracting the lines of the body from <paramref name="first"/> to <paramref name="last"/> (zero-based).</summary>
    public static ExtractionPlan Plan(string body, int first, int last, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true)
    {
        var analyzed = FlowFixture.Analyze(body, header, end, optionExplicit);
        var requested = new SourceRange(analyzed.FirstBodyLine + first, 0, analyzed.FirstBodyLine + last, 1);
        Assert.IsTrue(ExtractionSelection.TrySnap(analyzed.Procedure, analyzed.Instructions, requested, out var selection, out var refusal, out _), $"refused: {refusal}");
        return ExtractionAnalysis.Plan(selection!, analyzed.Graph, analyzed.Accesses, analyzed.Model, analyzed.Scopes);
    }
}

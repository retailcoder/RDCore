using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// An <see cref="IAnalysisObserver"/> that keeps the facts it is told, in the order it is told them.
/// </summary>
internal sealed class RecordingAnalysisObserver : IAnalysisObserver
{
    public List<ConversionFact> Conversions { get; } = [];
    public List<OperatorFact> Operations { get; } = [];
    public List<StatementFact> Statements { get; } = [];
    public List<VBErrorInfo> Errors { get; } = [];

    public void OnConversion(ConversionFact fact) => Conversions.Add(fact);
    public void OnOperation(OperatorFact fact) => Operations.Add(fact);
    public void OnStatement(StatementFact fact) => Statements.Add(fact);
    public void OnError(VBErrorInfo error) => Errors.Add(error);

    /// <summary>What was observed, one fact to a line: for the message of an assertion that fails.</summary>
    public string Describe() => string.Join(Environment.NewLine,
        Conversions.Select(fact => $"conversion {fact.Site} #{fact.Operand} {fact.Source.Name} -> {fact.Destination.Name} {fact.Flags} known={fact.IsValueKnown} error={fact.Error?.ErrorId}")
            .Concat(Operations.Select(fact => $"operation {fact.Operator} {fact.GetType().Name} {fact.EffectiveType?.Name} known={fact.IsValueKnown} error={fact.Error?.ErrorId}"))
            .Concat(Statements.Select(fact => $"statement {fact.Statement} {fact}"))
            .Concat(Errors.Select(error => $"error {error.ErrorId} {error.Description}")));
}

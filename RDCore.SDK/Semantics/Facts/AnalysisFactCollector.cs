using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// An <see cref="IAnalysisObserver"/> that keeps the facts it is told of, to hand them over as the <see cref="RuntimeFacts"/> of a procedure.
/// </summary>
/// <remarks>
/// <para>
/// A fact is kept once: an instruction that the evaluation arrives at again (a <c>For</c> loop's counter is evaluated by its opener and by its
/// <c>Next</c>) states the same fact about the same code, and the same fact twice is not two facts.
/// </para>
/// <para>
/// Not thread-safe, like the analysis it observes.
/// </para>
/// </remarks>
public sealed class AnalysisFactCollector : IAnalysisObserver
{
    private readonly List<ConversionFact> _conversions = [];
    private readonly HashSet<ConversionFact> _seenConversions = [];
    private readonly List<OperatorFact> _operations = [];
    private readonly HashSet<OperatorFact> _seenOperations = [];

    /// <inheritdoc/>
    public void OnConversion(ConversionFact fact)
    {
        if (_seenConversions.Add(fact))
        {
            _conversions.Add(fact);
        }
    }

    /// <inheritdoc/>
    public void OnOperation(OperatorFact fact)
    {
        if (_seenOperations.Add(fact))
        {
            _operations.Add(fact);
        }
    }

    /// <summary>
    /// The facts told of since the last time this was called, and forgets them.
    /// </summary>
    /// <param name="isFullyAnalyzed">Whether the code the facts are about was evaluated in full.</param>
    public RuntimeFacts Take(bool isFullyAnalyzed)
    {
        var facts = new RuntimeFacts([.. _conversions], [.. _operations], isFullyAnalyzed);

        _conversions.Clear();
        _seenConversions.Clear();
        _operations.Clear();
        _seenOperations.Clear();

        return facts;
    }
}

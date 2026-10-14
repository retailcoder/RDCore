using RDCore.SDK.Model.Errors.Abstract;
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
    private readonly UniqueFacts<ConversionFact> _conversions = new();
    private readonly UniqueFacts<OperatorFact> _operations = new();
    private readonly UniqueFacts<StatementFact> _statements = new();
    private readonly UniqueFacts<VBErrorInfo> _errors = new();

    /// <inheritdoc/>
    public void OnConversion(ConversionFact fact) => _conversions.Add(fact);

    /// <inheritdoc/>
    public void OnOperation(OperatorFact fact) => _operations.Add(fact);

    /// <inheritdoc/>
    public void OnStatement(StatementFact fact) => _statements.Add(fact);

    /// <inheritdoc/>
    public void OnError(VBErrorInfo error) => _errors.Add(error);

    /// <summary>
    /// The facts told of since the last time this was called, and forgets them.
    /// </summary>
    /// <param name="isFullyAnalyzed">Whether the code the facts are about was evaluated in full.</param>
    public RuntimeFacts Take(bool isFullyAnalyzed)
        => new(_conversions.Take(), _operations.Take(), _statements.Take(), _errors.Take(), isFullyAnalyzed);

    // the facts of one kind, in the order they were first told of.
    private sealed class UniqueFacts<TFact> where TFact : notnull
    {
        private readonly List<TFact> _facts = [];
        private readonly HashSet<TFact> _seen = [];

        public void Add(TFact fact)
        {
            if (_seen.Add(fact))
            {
                _facts.Add(fact);
            }
        }

        public ImmutableArray<TFact> Take()
        {
            var facts = ImmutableArray.CreateRange(_facts);
            _facts.Clear();
            _seen.Clear();
            return facts;
        }
    }
}

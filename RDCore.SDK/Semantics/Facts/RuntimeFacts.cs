using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// What the language core states about the code of one procedure when it is evaluated rather than run: the conversions the code asks for
/// and the operations it performs.
/// </summary>
/// <remarks>
/// <para>
/// A fact is about the <em>code</em>, never about a run of it: it is stated only when it is true of the code however the procedure is called
/// (<see cref="ConversionFact"/>). Every fact is implicitly "when this line runs": whether a line can run at all is a fact of its own, which
/// a pass over the code paths states.
/// </para>
/// <para>
/// A model whose <see cref="ProcedureSemanticModel.Runtime"/> is <see langword="null"/> has not been evaluated yet. That is not the same as a
/// model that was evaluated and has no facts.
/// </para>
/// </remarks>
/// <param name="Conversions">Every let-coercion (<strong>MS-VBAL 5.5.1.2</strong>) the code asks for, in the order the code was evaluated.</param>
/// <param name="Operations">Every operation (<strong>MS-VBAL 5.6.9</strong>) the code performs, in the order the code was evaluated.</param>
/// <param name="IsFullyAnalyzed">
/// Whether every instruction of the procedure was evaluated. It is <see langword="false"/> when the evaluation of an instruction failed for a
/// reason that is not the code's: what the facts state is true, and the facts there are are not all there are.
/// </param>
public sealed record class RuntimeFacts(
    ImmutableArray<ConversionFact> Conversions,
    ImmutableArray<OperatorFact> Operations,
    bool IsFullyAnalyzed)
{
    /// <summary>
    /// Facts of a procedure that states none, and was evaluated in full.
    /// </summary>
    public static RuntimeFacts None { get; } = new([], [], IsFullyAnalyzed: true);
}

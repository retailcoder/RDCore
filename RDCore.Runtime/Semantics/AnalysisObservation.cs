using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// The observer of one analysis, as the parts of a runtime pipeline share it: it passes on the facts the pipeline states, except
/// while the fact being stated is being described.
/// </summary>
/// <remarks>
/// <para>
/// Describing an operation or a conversion evaluates it again, because what the analysis says of an operation depends on how
/// its operands convert, and a conversion evaluated is a conversion observed. Those evaluations are not the code's. The part
/// of the pipeline that is describing a fact <see cref="Suspend">suspends</see> the observation for as long as it takes, which
/// is why every part of one pipeline shares one instance rather than each holding the observer.
/// </para>
/// <para>
/// It also counts what the evaluation <see cref="Assume">assumed</see>: the error an instruction raises is a fact only when nothing the instruction did
/// was assumed, which is something only the parts of the pipeline that assume it can say.
/// </para>
/// <para>
/// Like the pipeline it belongs to, an observation is for one analysis at a time: it is not thread-safe.
/// </para>
/// </remarks>
/// <param name="observer">Told of every fact the pipeline states.</param>
public sealed class AnalysisObservation(IAnalysisObserver observer)
{
    private int _suspensions;

    /// <summary>
    /// Whether facts are being held back, because one is being described.
    /// </summary>
    public bool IsSuspended => _suspensions > 0;

    /// <summary>
    /// How many times the evaluation assumed something it does not know, since the observation began. Only ever grows: what is of interest is whether it
    /// grew while something was evaluated.
    /// </summary>
    public long Assumptions { get; private set; }

    /// <summary>
    /// Notes that the evaluation relied on something it does not know: the value of a variable, what a call that is not made returns, the state of the outside
    /// world or of the standard library, the state another instruction left behind.
    /// </summary>
    /// <remarks>
    /// 👉 An error raised by an evaluation that assumed something could be the assumption's rather than the code's: it is not stated
    /// (<see cref="OnError"/>). Counted whether or not the observation is suspended: describing a fact is part of evaluating the instruction it is about.
    /// </remarks>
    public void Assume() => Assumptions++;

    /// <summary>
    /// Tells the observer of a conversion, unless the observation is suspended.
    /// </summary>
    /// <param name="fact">The conversion.</param>
    public void OnConversion(ConversionFact fact)
    {
        if (!IsSuspended)
        {
            observer.OnConversion(fact);
        }
    }

    /// <summary>
    /// Tells the observer of an operation, unless the observation is suspended.
    /// </summary>
    /// <param name="fact">The operation.</param>
    public void OnOperation(OperatorFact fact)
    {
        if (!IsSuspended)
        {
            observer.OnOperation(fact);
        }
    }

    /// <summary>
    /// Tells the observer of a statement, unless the observation is suspended.
    /// </summary>
    /// <param name="fact">The statement.</param>
    public void OnStatement(StatementFact fact)
    {
        if (!IsSuspended)
        {
            observer.OnStatement(fact);
        }
    }

    /// <summary>
    /// Tells the observer of the run-time error an instruction raised, unless the observation is suspended or the error is not the code's
    /// (<see cref="StatedErrors"/>).
    /// </summary>
    /// <param name="error">The error the instruction raised.</param>
    public void OnError(IVBRaisableError error)
    {
        if (!IsSuspended && StatedErrors.Of(error) is { } stated)
        {
            observer.OnError(stated);
        }
    }

    /// <summary>
    /// Holds back the facts until the returned scope is disposed.
    /// </summary>
    /// <returns>The scope to dispose when the fact has been described. Scopes nest.</returns>
    public Suspension Suspend()
    {
        _suspensions++;
        return new Suspension(this);
    }

    /// <summary>
    /// The span of time during which an observation holds its facts back.
    /// </summary>
    public readonly struct Suspension : IDisposable
    {
        private readonly AnalysisObservation? _observation;

        internal Suspension(AnalysisObservation observation) => _observation = observation;

        /// <summary>
        /// Resumes the observation, unless another suspension is still open.
        /// </summary>
        public void Dispose()
        {
            if (_observation is not null)
            {
                _observation._suspensions--;
            }
        }
    }
}

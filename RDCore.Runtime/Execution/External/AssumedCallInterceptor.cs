using RDCore.Runtime.Semantics;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External;

/// <summary>
/// Notes every call that leaves the workspace in an <em>analysis</em> as something the analysis assumed, and lets it through.
/// </summary>
/// <remarks>
/// <para>
/// A call into the standard library is made in an analysis, but the library has state the analysis does not have: the files a channel is open on, the
/// seed of <c>Rnd</c>, the error <c>Err</c> holds, the time it is. What such a call raises is not known to be the code's, and neither is what it raises
/// in the outside world, which is not called at all (<see cref="OutsideWorldCallProvider"/>).
/// </para>
/// <para>
/// 👉 This is the coarse answer: a member of the library that has no state, <c>Mid$</c> given a start of <c>0</c>, raises the code's error, and it is
/// not stated either. Telling the two apart is a classification of the library's members by purity, which is not done yet.
/// </para>
/// </remarks>
/// <param name="observation">The observation of the analysis the calls are made in.</param>
public sealed class AssumedCallInterceptor(AnalysisObservation observation) : IExternalCallInterceptor
{
    /// <inheritdoc/>
    public ExternalCallDecision Intercept(ExternalCallRequest request, IRuntimeSession session)
    {
        observation.Assume();
        return ExternalCallDecision.Allow;
    }
}

using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ISessionErrorState"/>
internal sealed class SessionErrorState : ISessionErrorState
{
    /// <inheritdoc/>
    public VBRuntimeErrorInfo? Current { get; private set; }

    /// <inheritdoc/>
    public bool HasError => Current is not null;

    /// <inheritdoc/>
    public void Raise(VBRuntimeErrorInfo error) => Current = error;

    /// <inheritdoc/>
    public bool Clear()
    {
        var had = Current is not null;
        Current = null;
        return had;
    }
}

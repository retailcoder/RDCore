using RDCore.SDK.Model.Errors;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The error state of a session — one per session, which is what makes an <c>Err</c> object possible.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §6.1.3.2</strong>'s <c>Err</c> is "a singleton that is referenced by the <c>Err</c>
/// global/static symbol": one object, for the whole session, whose <c>Number</c>, <c>Description</c> and
/// <c>Source</c> describe the most recent run-time error. Until now the only record of an error was
/// <see cref="Shared.ErrorHandlerState.ActiveError"/>, which belongs to one <em>activation</em> and dies
/// with its frame — so there was nowhere for such an object to read from, and nothing that outlived a
/// procedure call could report what had gone wrong in it.
/// <para>
/// The two are not redundant. An activation's <c>ActiveError</c> answers "may this activation
/// <c>Resume</c>, and where" — a question about control flow in one frame. This answers "what went
/// wrong in this session", which is what source code asks when it reads <c>Err.Number</c>, and which
/// outlives the frame that raised it.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface ISessionErrorState
{
    /// <summary>
    /// The most recent run-time error, or <c>null</c> when there is none — the state <c>Err.Number</c>
    /// reports as <c>0</c>.
    /// </summary>
    VBRuntimeErrorInfo? Current { get; }

    /// <summary>
    /// Whether an error is current. <c>Err.Number &lt;&gt; 0</c>, in source terms.
    /// </summary>
    bool HasError { get; }

    /// <summary>
    /// Records <paramref name="error"/> as the session's current error, replacing any earlier one.
    /// </summary>
    /// <remarks>
    /// Called for every run-time error the interpreter raises, whether or not anything goes on to
    /// handle it: <c>Err</c> is set by the error, not by the handling of it.
    /// </remarks>
    /// <param name="error">The error that was raised.</param>
    void Raise(VBRuntimeErrorInfo error);

    /// <summary>
    /// Clears the current error.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §6.1.3.2.1</strong> lists what does this besides <c>Err.Clear</c> itself: a
    /// <c>Resume</c> statement, <c>Exit Sub</c>/<c>Exit Function</c>/<c>Exit Property</c>, and an
    /// <c>On Error</c> statement.
    /// </remarks>
    /// <returns><c>true</c> if there was an error to clear.</returns>
    bool Clear();
}

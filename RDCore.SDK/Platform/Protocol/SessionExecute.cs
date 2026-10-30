using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="ExecuteSessionParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionExecute, Direction.ClientToServer)]
public sealed record class ExecuteSessionRequest : ExecuteSessionParams, IRequest, IRequest<ExecuteSessionResult>;

/// <summary>
/// Request for <c>rdcore/host/execute</c>: the language-server side of
/// <see cref="ExecuteSessionParams"/>, addressed to the component that owns the runtime session.
/// </summary>
/// <remarks>
/// The parsed module rides a <see cref="System.Text.Json"/> string for the same reason
/// <see cref="DiagnoseDocumentRequest"/>'s does — the AST is polymorphic and the JSON-RPC
/// transport's own serializer cannot round-trip it (see <see cref="PlatformJson"/>). The language
/// server has already parsed and defined the module's symbols by the time it sends this, so the host
/// lowers and runs rather than re-deriving any of it.
/// </remarks>
[Method(RDCorePlatformProtocol.HostExecute, Direction.ClientToServer)]
public record class HostExecuteParams : IRequest, IRequest<ExecuteSessionResult>
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="HostExecutePayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name — the scope <see cref="EntryPoint"/> is resolved in.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The name of the parameterless procedure to invoke.
    /// </summary>
    public string EntryPoint { get; init; } = string.Empty;

    /// <summary>
    /// Whether the program runs under a debugger. See <see cref="ExecuteSessionParams.Debug"/>.
    /// </summary>
    public bool Debug { get; init; }

    /// <summary>
    /// Whether the entry point is a statement typed at a prompt. See <see cref="ExecuteSessionParams.Immediate"/>.
    /// </summary>
    public bool Immediate { get; init; }

    /// <summary>
    /// Whether the lines the program prints are said as they are printed (<see cref="HostOutputNotification"/>) instead of with the answer, for the whole of the run:
    /// the requests that resume the program too. Only a program run under a debugger streams.
    /// </summary>
    public bool StreamOutput { get; init; }
}

/// <summary>
/// The <see cref="System.Text.Json"/> payload carried in <see cref="HostExecuteParams.Json"/>.
/// </summary>
/// <param name="DocumentUri">The document the module was parsed from.</param>
/// <param name="ParseResult">The parsed module — AST plus syntax errors.</param>
public record class HostExecutePayload(Uri DocumentUri, ModuleParseResult ParseResult);

/// <summary>
/// The result of a run. The same result answers both hops — the language server passes the host's
/// answer back to the client unchanged, except for a parse that never reached the host at all.
/// </summary>
public record class ExecuteSessionResult
{
    /// <summary>How the run ended.</summary>
    public ExecutionOutcome Outcome { get; init; }

    /// <summary>
    /// Everything the program printed, in order, one entry per line — including a line it left open
    /// with a trailing <c>;</c>.
    /// </summary>
    public IReadOnlyList<string> Output { get; init; } = [];

    /// <summary>
    /// How many lines the host has said by notification (<see cref="HostOutputNotification"/>) since it started, when this answer was given. A run that streams has
    /// said its lines before it answers, and they are not in <see cref="Output"/>; this is what lets the receiver wait for them.
    /// </summary>
    public long StreamedLines { get; init; }

    /// <summary>
    /// The syntax errors, when <see cref="Outcome"/> is <see cref="ExecutionOutcome.SyntaxError"/>.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>
    /// The run-time error's number, when <see cref="Outcome"/> is
    /// <see cref="ExecutionOutcome.RuntimeError"/>; <c>0</c> otherwise.
    /// </summary>
    public int ErrorNumber { get; init; }

    /// <summary>
    /// The run-time error's description, or a short explanation of any other non-<see cref="ExecutionOutcome.Completed"/> outcome.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// The run-time error's <strong>RD-VBAL §2.6.3</strong> diagnostic code — <c>VBR</c> for one the
    /// runtime semantics reported, <c>VBA</c> for one the program raised itself with <c>Error</c> or
    /// <c>Err.Raise</c>. Empty for any other outcome.
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>
    /// The error's <em>category</em>, as a reader sees it in a title: which of <strong>RD-VBAL §2.6</strong>'s
    /// families raised it - "Run-time error" or "Application error" for a run, localized. What it *was* is
    /// <see cref="ErrorMessage"/>. Empty for any other outcome.
    /// </summary>
    public string ErrorTitle { get; init; } = string.Empty;

    /// <summary>
    /// What <c>Err.Source</c> reports: the object or application that generated the error, defaulting to
    /// the project's own name (<strong>MS-VBAL §6.1.3.2.2.6</strong>). Empty for any other outcome.
    /// </summary>
    public string ErrorSource { get; init; } = string.Empty;

    /// <summary>
    /// Where in the submitted <see cref="ExecuteSessionParams.Source"/> the error was raised, zero-based
    /// as every position in the platform is: the line, and <see cref="ErrorCharacter"/> within it. Both
    /// <c>-1</c> when the outcome is not a run-time error, or when nothing located it.
    /// </summary>
    /// <remarks>
    /// A position in the source the <em>caller sent</em>, which is the one document both ends of the
    /// protocol agree on. A client whose program does not live in a file — an interactive shell's buffer —
    /// maps it back to whatever it calls a line itself.
    /// </remarks>
    public int ErrorLine { get; init; } = -1;

    /// <inheritdoc cref="ErrorLine"/>
    public int ErrorCharacter { get; init; } = -1;

    /// <summary>
    /// 🎯 The <em>line number</em> the error was raised at - what <c>Erl</c> reports: the nearest line-number
    /// label at or before the faulting statement, or <c>0</c> when none precedes it. Also <c>0</c> for any
    /// other outcome.
    /// </summary>
    public long ErrorLineNumber { get; init; }

    /// <summary>
    /// Why the condition of the breakpoint the program waits at could not be evaluated, when <see cref="Outcome"/> is <see cref="ExecutionOutcome.Suspended"/> and that
    /// is why it waits: a breakpoint whose condition raises an error is waited at (see <see cref="RDCore.SDK.Runtime.Abstract.Execution.RuntimeBreakpoint.Condition"/>),
    /// and this is what the person is told of it. Empty otherwise. The program does not wait on the error: it is the condition's and not the program's, and
    /// <see cref="ErrorNumber"/> stays <c>0</c>.
    /// </summary>
    public string ConditionError { get; init; } = string.Empty;

    /// <summary>
    /// 🎯 The call stack the error was raised on, innermost activation first - what <c>Err.StackTrace</c>
    /// reports. Empty for any other outcome.
    /// </summary>
    public IReadOnlyList<ExecuteStackFrame> StackTrace { get; init; } = [];
}

/// <summary>
/// One activation of the call stack a run-time error was raised on.
/// </summary>
/// <remarks>
/// Structured rather than pre-formatted, because a position only means something to the client: the
/// source both ends agree on is the one the client sent, and a client whose program does not live in a
/// file has its own idea of what a line is called. Formatting it here would force the shell to display a
/// line number its user never typed.
/// </remarks>
/// <param name="Procedure">The name of the procedure the activation is of.</param>
/// <param name="Line">
/// Zero-based line in the submitted source, or <c>-1</c> when the activation carries no location — true
/// of every activation but the one the error was raised in, since an activation record does not say
/// where in itself it is suspended.
/// </param>
/// <param name="Character">Zero-based character within <paramref name="Line"/>, or <c>-1</c>.</param>
/// <param name="ReturnLines">
/// The zero-based lines of the <c>GoSub</c> statements the activation had not <c>Return</c>ed from when the error was raised, innermost first - how it got to where it
/// was, as <see cref="HostStackFrame.ReturnLines"/> says it of a program that waits. Empty for an activation in no <c>GoSub</c>.
/// </param>
public record class ExecuteStackFrame(string Procedure, int Line = -1, int Character = -1, IReadOnlyList<int>? ReturnLines = null)
{
    /// <summary>The lines of the <c>GoSub</c> statements the activation would have returned to; none for one that was in none.</summary>
    public IReadOnlyList<int> ReturnLines { get; init; } = ReturnLines ?? [];
}

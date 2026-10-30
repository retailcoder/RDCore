namespace RDCore.SDK.Client;

/// <summary>
/// The names of the methods of the RDCore platform's own protocol (<c>rdcore/*</c>), which travel on the same JSON-RPC connection as <strong>LSP</strong>:
/// those a client sends the language server (<c>rdcore/session/*</c>), those the language server sends a client, and those the components of the platform
/// send one another (<c>rdcore/host/*</c>, <c>rdcore/parser/*</c>, <c>rdcore/diagnostics/*</c>), which no client sends.
/// </summary>
public static class RDCorePlatformProtocol
{
    /// <summary>
    /// Asks the language server for the state of the platform's runtime session.
    /// </summary>
    public const string SessionStatus = "rdcore/session/status";

    /// <summary>
    /// Asks the environment host for the state of the runtime session it owns. The language-server
    /// side of <see cref="SessionStatus"/>; never sent by a client.
    /// </summary>
    public const string HostSessionStatus = "rdcore/host/session/status";

    /// <summary>
    /// Asks the language server to run one procedure of a module the client supplies.
    /// </summary>
    public const string SessionExecute = "rdcore/session/execute";

    /// <summary>
    /// Asks the environment host to lower and run a parsed module in its runtime session. The
    /// language-server side of <see cref="SessionExecute"/>; never sent by a client.
    /// </summary>
    public const string HostExecute = "rdcore/host/execute";

    /// <summary>
    /// Asks the language server to take a module the client supplied out of the runtime session.
    /// </summary>
    public const string SessionDiscard = "rdcore/session/discard";

    /// <summary>
    /// Asks the environment host to take a module out of its runtime session. The language-server side of <see cref="SessionDiscard"/>; never sent
    /// by a client.
    /// </summary>
    public const string HostDiscard = "rdcore/host/discard";

    /// <summary>
    /// Asks the environment host to compose its runtime session again from the project file as it is now: what the language server asks once a client said the
    /// <c>.rdproj</c> changed (<c>workspace/didChangeWatchedFiles</c>), before it defines the workspace again. Never sent by a client.
    /// </summary>
    public const string HostProjectReload = "rdcore/host/project/reload";

    /// <summary>
    /// Tells the language server what a program printed, as it prints: a notification from the environment host, sent for a program that was run with its
    /// output streamed (<c>HostExecuteParams.StreamOutput</c>).
    /// </summary>
    public const string HostOutput = "rdcore/host/output";

    /// <summary>
    /// Asks the language server to have the client answer a program's <c>MsgBox</c>: a request from the environment host, which waits for the answer as the
    /// program waits for the dialog.
    /// </summary>
    public const string HostMessageBox = "rdcore/host/interaction/msgBox";

    /// <summary>
    /// Asks the language server to have the client answer a program's <c>InputBox</c>. See <see cref="HostMessageBox"/>.
    /// </summary>
    public const string HostInputBox = "rdcore/host/interaction/inputBox";

    /// <summary>
    /// Asks a client to answer a program's <c>MsgBox</c>: the language-server side of <see cref="HostMessageBox"/>, sent to a client that expects the
    /// <c>SessionInteraction</c> capability.
    /// </summary>
    public const string SessionMessageBox = "rdcore/session/interaction/msgBox";

    /// <summary>
    /// Asks a client to answer a program's <c>InputBox</c>. See <see cref="SessionMessageBox"/>.
    /// </summary>
    public const string SessionInputBox = "rdcore/session/interaction/inputBox";

    /// <summary>
    /// Asks the environment host to resume a program that waits at a stop, or to take one step of it.
    /// </summary>
    public const string HostDebugResume = "rdcore/host/debug/resume";

    /// <summary>
    /// Asks the environment host to stop a program that is running where it is, so that it waits.
    /// </summary>
    public const string HostDebugPause = "rdcore/host/debug/pause";

    /// <summary>
    /// Asks the environment host to move the point a program that waits goes on from.
    /// </summary>
    public const string HostDebugGoto = "rdcore/host/debug/goto";

    /// <summary>
    /// Asks the environment host which edits made while a program waits are not applied to it: what resuming it would be refused for.
    /// </summary>
    public const string HostDebugEditStatus = "rdcore/host/debug/editStatus";

    /// <summary>
    /// Asks the environment host to set the lines of a module that a program under a debugger waits at.
    /// </summary>
    public const string HostDebugBreakpoints = "rdcore/host/debug/breakpoints";

    /// <summary>Asks the environment host for the call stack of the program that waits.</summary>
    public const string HostDebugStack = "rdcore/host/debug/stack";

    /// <summary>Asks the environment host for the variables of an activation of the program that waits.</summary>
    public const string HostDebugVariables = "rdcore/host/debug/variables";

    /// <summary>Asks the environment host for the value of an expression in an activation of the program that waits.</summary>
    public const string HostDebugEvaluate = "rdcore/host/debug/evaluate";

    /// <summary>Asks the environment host to run a statement in an activation of the program that waits.</summary>
    public const string HostDebugExecute = "rdcore/host/debug/execute";

    /// <summary>Asks the environment host to say which run-time errors a program under a debugger waits at, where they are raised.</summary>
    public const string HostDebugErrorBreak = "rdcore/host/debug/errorBreak";

    /// <summary>Asks the language server to resume, or step, the program a client ran under a debugger. See <see cref="HostDebugResume"/>.</summary>
    public const string SessionDebugResume = "rdcore/session/debug/resume";

    /// <summary>Asks the language server to stop the program that is running. See <see cref="HostDebugPause"/>.</summary>
    public const string SessionDebugPause = "rdcore/session/debug/pause";

    /// <summary>Asks the language server to move the point the program that waits goes on from. See <see cref="HostDebugGoto"/>.</summary>
    public const string SessionDebugGoto = "rdcore/session/debug/goto";

    /// <summary>Asks the language server which edits made while a program waits are not applied to it. See <see cref="HostDebugEditStatus"/>.</summary>
    public const string SessionDebugEditStatus = "rdcore/session/debug/editStatus";

    /// <summary>Asks the language server to end the program that is running or waits. See <see cref="HostDebugTerminate"/>.</summary>
    public const string SessionDebugTerminate = "rdcore/session/debug/terminate";

    /// <summary>Asks the language server to set the lines of a module that a program under a debugger waits at. See <see cref="HostDebugBreakpoints"/>.</summary>
    public const string SessionDebugBreakpoints = "rdcore/session/debug/breakpoints";

    /// <summary>Asks the language server for the call stack of the program that waits. See <see cref="HostDebugStack"/>.</summary>
    public const string SessionDebugStack = "rdcore/session/debug/stack";

    /// <summary>Asks the language server for the variables of an activation of the program that waits. See <see cref="HostDebugVariables"/>.</summary>
    public const string SessionDebugVariables = "rdcore/session/debug/variables";

    /// <summary>Asks the language server for the value of an expression, as text, in an activation of the program that waits. See <see cref="HostDebugEvaluate"/>.</summary>
    public const string SessionDebugEvaluate = "rdcore/session/debug/evaluate";

    /// <summary>Asks the language server to run a statement, as text, in an activation of the program that waits. See <see cref="HostDebugExecute"/>.</summary>
    public const string SessionDebugExecute = "rdcore/session/debug/execute";

    /// <summary>
    /// Asks the environment host to end a program that is running or waits.
    /// </summary>
    public const string HostDebugTerminate = "rdcore/host/debug/terminate";

    /// <summary>
    /// Asks the language server to analyze a module the client supplies.
    /// </summary>
    public const string SessionAnalyze = "rdcore/session/analyze";

    /// <summary>
    /// Asks the language server what can be written at a position of a module the client supplies.
    /// </summary>
    public const string SessionCompletion = "rdcore/session/completion";

    /// <summary>
    /// Asks the client for the indenter settings its user had in Rubberduck, as Rubberduck has them. A request the language server sends.
    /// </summary>
    public const string RubberduckIndenterSettings = "rdcore/rubberduck-indenter-settings";

    /// <summary>
    /// Asks the client to take the <c>Formatting</c> settings the language server mapped from Rubberduck's indenter settings as its own. A request the language server sends.
    /// </summary>
    public const string RubberduckIndenterSettingsApply = "rdcore/rubberduck-indenter-settings/apply";

    /// <summary>
    /// Asks the language server what the call being written at a position of a module the client supplies takes.
    /// </summary>
    public const string SessionSignatureHelp = "rdcore/session/signatureHelp";

    /// <summary>
    /// Asks the language server what the tokens of code fragments the client supplies are, with the legend of <c>textDocument/semanticTokens/full</c>.
    /// </summary>
    public const string SessionSemanticTokens = "rdcore/session/semanticTokens";

    /// <summary>
    /// Registers the verbs of a client's own commands with the language server, described, which completes them as they are typed.
    /// </summary>
    public const string SessionCommands = "rdcore/session/commands";

    /// <summary>
    /// Reads one byte of the runtime session's memory.
    /// </summary>
    public const string SessionPeek = "rdcore/session/memory/peek";

    /// <summary>
    /// Writes one byte of the runtime session's memory.
    /// </summary>
    public const string SessionPoke = "rdcore/session/memory/poke";

    /// <summary>
    /// The language-server side of <see cref="SessionPeek"/>; never sent by a client.
    /// </summary>
    public const string HostPeek = "rdcore/host/memory/peek";

    /// <summary>
    /// The language-server side of <see cref="SessionPoke"/>; never sent by a client.
    /// </summary>
    public const string HostPoke = "rdcore/host/memory/poke";

    /// <summary>
    /// Requests an AST from the parser for a full document.
    /// </summary>
    public const string ParseFullDocument = "rdcore/parser/document";

    /// <summary>
    /// Requests the lexical tokens of a text from the parser.
    /// </summary>
    public const string ParseTokens = "rdcore/parser/tokens";

    /// <summary>
    /// Sends a module's member symbol descriptors to the environment host to define in its runtime session.
    /// </summary>
    public const string DefineSymbols = "rdcore/host/symbols/define";

    /// <summary>
    /// Asks the environment host for the semantic model of the code it holds. The language-server side of the diagnostics it hands an extension;
    /// never sent by a client.
    /// </summary>
    public const string HostSemantics = "rdcore/host/semantics";

    /// <summary>
    /// Hands a diagnostics-provider extension a parsed document and asks for the diagnostics it finds.
    /// </summary>
    public const string DiagnoseDocument = "rdcore/diagnostics/document";
}

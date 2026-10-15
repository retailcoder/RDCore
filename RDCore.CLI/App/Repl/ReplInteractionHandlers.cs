using OmniSharp.Extensions.JsonRpc;
using RDCore.External.Interaction;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// Handles <c>rdcore/session/interaction/msgBox</c>: a program run from the shell opened a <c>MsgBox</c>, and the shell answers it at its console.
/// </summary>
/// <remarks>
/// What the program printed before it opened the dialog comes with it, and is written first: the answer to the request that runs the program, which carries the rest
/// of what it printed, comes when the program is over.
/// </remarks>
/// <param name="interaction">The shell's dialogs.</param>
/// <param name="console">The shell's console.</param>
internal sealed class ReplMessageBoxHandler(ReplUserInteraction interaction, IReplConsole console) : IJsonRpcRequestHandler<SessionMessageBoxParams, MessageBoxResult>
{
    /// <inheritdoc/>
    public Task<MessageBoxResult> Handle(SessionMessageBoxParams request, CancellationToken cancellationToken)
        // the console is read on a thread of its own: the dialog waits for a person, and the connection does not.
        => Task.Run(() =>
        {
            ReplInteraction.WriteOutput(console, request.Output);
            var answer = interaction.ShowMessage(new MessageBoxRequest(request.Prompt, request.Title, (VBMsgBoxStyle)request.Style), cancellationToken);
            return new MessageBoxResult { Handled = true, Result = (int)answer };
        }, cancellationToken);
}

/// <summary>
/// Handles <c>rdcore/session/interaction/inputBox</c>: a program run from the shell opened an <c>InputBox</c>, and the shell asks for the line at its console.
/// </summary>
/// <param name="interaction">The shell's dialogs.</param>
/// <param name="console">The shell's console.</param>
internal sealed class ReplInputBoxHandler(ReplUserInteraction interaction, IReplConsole console) : IJsonRpcRequestHandler<SessionInputBoxParams, InputBoxResult>
{
    /// <inheritdoc/>
    public Task<InputBoxResult> Handle(SessionInputBoxParams request, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            ReplInteraction.WriteOutput(console, request.Output);
            var answer = interaction.Prompt(new InputBoxRequest(request.Prompt, request.Title, request.Default, request.XPos, request.YPos), cancellationToken);
            return answer is null
                ? new InputBoxResult { Handled = true, Cancelled = true }
                : new InputBoxResult { Handled = true, Value = answer };
        }, cancellationToken);
}

internal static class ReplInteraction
{
    /// <summary>Writes the lines a program printed before it opened a dialog, as the shell writes what a program prints.</summary>
    public static void WriteOutput(IReplConsole console, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            console.WriteLine(line);
        }
    }
}

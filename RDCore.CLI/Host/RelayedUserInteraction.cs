using RDCore.External.Interaction;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.CLI.Host;

/// <summary>
/// Where the environment host asks the person running a program to answer its dialogs: the language server, which asks its client.
/// </summary>
/// <remarks>
/// Set by whoever owns the connection to the language server, as <see cref="IEnvironmentSessionProvider.OutputStreamed"/> is; a host with no connection asks nobody,
/// and shows the dialogs of the platform it runs on.
/// </remarks>
public interface IHostInteractionChannel
{
    /// <summary>Asks for a <c>MsgBox</c> to be answered (<c>rdcore/host/interaction/msgBox</c>).</summary>
    Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token);

    /// <summary>Asks for an <c>InputBox</c> to be answered (<c>rdcore/host/interaction/inputBox</c>).</summary>
    Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token);
}

/// <summary>
/// The dialogs of a program that runs in the environment host: answered by the client of the language server when it shows dialogs - the interactive shell asks at
/// its console - and by the platform the host runs on otherwise.
/// </summary>
/// <remarks>
/// <para>
/// The language server answers that nobody was asked when its client did not say it shows dialogs (<see cref="SessionInteraction"/>), and then the platform's own are
/// shown: Windows', or none. So is a dialog that could not be asked at all - a connection that is not there, or was lost.
/// </para>
/// <para>
/// A program prints before it asks, and what it printed is in the answer to the request that runs it, which comes when it is over. The lines it completed are sent
/// with the dialog instead, so that a client shows them first; once the dialog has been answered they are no longer the run's to answer with.
/// </para>
/// </remarks>
/// <param name="provider">The provider of the session: its connection to the language server, and the output of the run that opens the dialog.</param>
/// <param name="fallback">The dialogs of the platform the host runs on.</param>
internal sealed class RelayedUserInteraction(IEnvironmentSessionProvider provider, IUserInteraction fallback) : IUserInteraction
{
    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        if (provider.Interaction is { } client)
        {
            var (buffer, output) = Pending();
            var answer = Ask(() => client.MessageBoxAsync(
                new HostMessageBoxParams { Prompt = request.Prompt, Title = request.Title, Style = (int)request.Style, Output = output }, token), token);

            if (answer is { Handled: true } && request.Choices.Contains((VBMsgBoxResult)answer.Result))
            {
                buffer?.ForgetCompleted(output.Count);
                return (VBMsgBoxResult)answer.Result;
            }
        }

        return fallback.ShowMessage(request, token);
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default)
    {
        if (provider.Interaction is { } client)
        {
            var (buffer, output) = Pending();
            var answer = Ask(() => client.InputBoxAsync(
                new HostInputBoxParams
                {
                    Prompt = request.Prompt, Title = request.Title, Default = request.Default, XPos = request.XPos, YPos = request.YPos, Output = output,
                }, token), token);

            if (answer is { Handled: true })
            {
                buffer?.ForgetCompleted(output.Count);
                return answer.Cancelled ? null : answer.Value;
            }
        }

        return fallback.Prompt(request, token);
    }

    // the lines the run completed and still holds: those of a run that streams its output have been said already.
    private (RuntimeOutputBuffer? Buffer, IReadOnlyList<string> Lines) Pending()
        => provider.Output.Target is RuntimeOutputBuffer buffer ? (buffer, buffer.CompletedLines) : (null, []);

    // the program waits for the dialog as it would for a window, on its own thread; a dialog that could not be asked is not answered, and the platform's is shown.
    private static TResult? Ask<TResult>(Func<Task<TResult>> ask, CancellationToken token) where TResult : class
    {
        try
        {
            return ask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}

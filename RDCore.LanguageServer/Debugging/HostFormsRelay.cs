using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Platform.Components;

namespace RDCore.LanguageServer.Debugging;

// where the environment host says the session has no form loaded any more, for a debugger that stayed attached to a program that completed with one shown.
// The notification and the result of the run travel apart, and either can be handled first: whoever is told asks the host how many forms are loaded, rather
// than taking the notification as the answer.
internal interface IHostFormsRelay
{
    event Action? Unloaded;

    void Publish();
}

internal sealed class HostFormsRelay : IHostFormsRelay
{
    public event Action? Unloaded;

    public void Publish() => Unloaded?.Invoke();
}

internal sealed class HostFormsUnloadedHandler(IHostFormsRelay relay) : IJsonRpcNotificationHandler<HostFormsUnloadedNotification>
{
    public Task<MediatR.Unit> Handle(HostFormsUnloadedNotification request, CancellationToken cancellationToken)
    {
        relay.Publish();
        return Task.FromResult(MediatR.Unit.Value);
    }
}

using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="PeekSessionParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionPeek, Direction.ClientToServer)]
public sealed record class PeekSessionRequest : PeekSessionParams, IRequest, IRequest<PeekSessionResult>;

/// <summary>
/// Request for <c>rdcore/host/memory/peek</c>: the language-server side of
/// <see cref="PeekSessionParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCorePlatformProtocol.HostPeek, Direction.ClientToServer)]
public record class HostPeekParams : IRequest, IRequest<PeekSessionResult>
{
    /// <summary>The address to read.</summary>
    public int Address { get; init; }
}

/// <summary>
/// <see cref="PokeSessionParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionPoke, Direction.ClientToServer)]
public sealed record class PokeSessionRequest : PokeSessionParams, IRequest, IRequest<PokeSessionResult>;

/// <summary>
/// Request for <c>rdcore/host/memory/poke</c>: the language-server side of
/// <see cref="PokeSessionParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCorePlatformProtocol.HostPoke, Direction.ClientToServer)]
public record class HostPokeParams : IRequest, IRequest<PokeSessionResult>
{
    /// <summary>The address to write.</summary>
    public int Address { get; init; }

    /// <summary>The byte to write there.</summary>
    public byte Value { get; init; }
}

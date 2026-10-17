using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ISessionRandomNumberGenerator"/>
internal sealed class SessionRandomNumberGenerator : ISessionRandomNumberGenerator
{
    // MS-VBA's seed before anything seeds it (0x50000), measured: it is the one the first Rnd of a new process, 0.7055475, is generated from.
    internal const int InitialSeed = 0x50000;

    public int Seed { get; set; } = InitialSeed;

    public void Reset() => Seed = InitialSeed;
}

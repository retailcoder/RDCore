namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The state of the standard library's random-number generator (<strong>MS-VBAL §6.1.2.10.1.6</strong> <c>Rnd</c> and
/// <strong>§6.1.2.10.2.1</strong> <c>Randomize</c>), which outlives a call to either.
/// </summary>
/// <remarks>
/// <para>
/// The generator is MS-VBA's, so that a program that seeds it gets the sequence it gets in MS-VBA: a 24-bit linear congruential generator whose state is
/// also the last number it generated, as a fraction of 2<sup>24</sup>. A session that no program has seeded starts where MS-VBA starts, so that the first
/// <c>Rnd</c> of a session is <c>0.7055475</c>, as it is in a new MS-VBA process.
/// </para>
/// <para>
/// An <c>End</c> resets it with the rest of the session: the program that runs next generates the sequence a new session does.
/// </para>
/// </remarks>
public interface ISessionRandomNumberGenerator
{
    /// <summary>
    /// The generator's state: a 24-bit value, from <c>0</c> to <c>16777215</c>, that the next number is generated from.
    /// </summary>
    int Seed { get; set; }

    /// <summary>
    /// The seed of a session that no program has run in: the one MS-VBA starts a new process with.
    /// </summary>
    void Reset();
}

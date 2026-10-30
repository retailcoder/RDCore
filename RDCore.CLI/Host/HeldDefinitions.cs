using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.Host;

/// <summary>
/// The definitions a program in progress holds (<see cref="ISessionSymbols.HoldsStoredValues"/>), and what was defined and lowered against them: applied, and
/// defined and lowered again, when the program is over.
/// </summary>
/// <remarks>
/// A variable redeclared as another type, or a Type given other fields, while a program runs or waits stays as it was - the variable with its value, the
/// Type with the fields the program's values of it are laid out by - so that the edit can be taken back with nothing lost. What the analysis pass sent
/// meanwhile was defined and lowered against them as they were; once the held definitions are applied, each of those requests is made again, in the order
/// it came in, against them as they are declared: a variable of the Type is given the Type as edited, and the code is checked and lowered against it.
/// </remarks>
internal static class HeldDefinitions
{
    // the requests received while a definition was held, the latest of each module and phase, in the order they came in, each with what makes it again.
    private static readonly ConditionalWeakTable<IRuntimeSession, Requests> ReceivedBySession = new();

    private sealed record class Request(string Key, string Module, Func<ImmutableArray<string>> Again);

    private sealed class Requests
    {
        private readonly Lock _sync = new();
        private ImmutableList<Request> _requests = [];

        public void Add(Request request)
        {
            lock (_sync)
            {
                _requests = _requests.RemoveAll(older => older.Key == request.Key).Add(request);
            }
        }

        public void Remove(string key)
        {
            lock (_sync)
            {
                _requests = _requests.RemoveAll(request => request.Key == key);
            }
        }

        public bool Contains(string module)
        {
            lock (_sync)
            {
                return _requests.Any(request => string.Equals(request.Module, module, StringComparison.OrdinalIgnoreCase));
            }
        }

        public ImmutableList<Request> Take()
        {
            lock (_sync)
            {
                var taken = _requests;
                _requests = [];
                return taken;
            }
        }
    }

    /// <summary>A program starts: from now until it is over, a definition that would let go of a value, or change a Type, is held.</summary>
    public static void Hold(IRuntimeSession session) => session.Symbols.HoldsStoredValues = true;

    /// <summary>
    /// A request defined a module's symbols, or loaded its code: when a definition is held, what makes the request is kept, to make it again once the held
    /// definitions are applied. Made when nothing is held, it was made against the declarations as they are, and needs making again no more than the older
    /// request of the same module and phase it replaces.
    /// </summary>
    /// <param name="session">The session it was made in.</param>
    /// <param name="key">The module and the phase of the definition it makes: a later request with the same key replaces it.</param>
    /// <param name="module">The module's name.</param>
    /// <param name="again">What makes it again, and says what is wrong with the module's code the way the request did.</param>
    public static void Received(IRuntimeSession session, string key, string module, Func<ImmutableArray<string>> again)
    {
        var received = ReceivedBySession.GetOrCreateValue(session);
        if (session.Symbols.HeldDefinitions.Count > 0)
        {
            received.Add(new Request(key, module, again));
        }
        else
        {
            received.Remove(key);
        }
    }

    /// <summary>
    /// Whether <paramref name="module"/> was defined or loaded while a definition is held: it was checked against the declarations as they were, not as
    /// edited.
    /// </summary>
    public static bool LoweredUnderAHold(IRuntimeSession session, string module)
        => session.Symbols.HeldDefinitions.Count > 0 && ReceivedBySession.TryGetValue(session, out var received) && received.Contains(module);

    /// <summary>
    /// The program is over: nothing is held from now on, what was held is applied, and every request made against it is made again, in the order it came in
    /// - each says what is wrong with its module's code as it did.
    /// </summary>
    /// <param name="session">The session the program was in.</param>
    /// <returns>The names of the modules defined and lowered again; none when nothing was held.</returns>
    public static IReadOnlyCollection<string> Release(IRuntimeSession session)
    {
        // stopping to hold and taking what is held are one step (ISessionSymbols.ApplyHeldDefinitions): a definition that arrives meanwhile is held before
        // it, or applied as it arrives.
        var applied = session.Symbols.ApplyHeldDefinitions();
        var requests = ReceivedBySession.GetOrCreateValue(session).Take();
        if (applied.Count == 0)
        {
            return [];
        }

        foreach (var request in requests)
        {
            _ = request.Again();
        }

        return [.. requests.Select(request => request.Module).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}

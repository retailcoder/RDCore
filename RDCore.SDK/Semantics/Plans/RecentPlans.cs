using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RDCore.SDK.Semantics.Plans;

/// <summary>
/// The plans last asked for, each in a slot for the identity of what it was asked for: what an operation evaluated in a loop asks with every time.
/// </summary>
/// <remarks>
/// Finding a plan by the references it was asked for is several times cheaper than looking it up by value, which compares the records the types are.
/// An entry is immutable and replaces the one before it whole, so threads that race for a slot only cost each other a lookup; a slot that holds
/// something else is a miss, and the plan is looked up by value.
/// </remarks>
/// <typeparam name="TPlan">The plan.</typeparam>
internal sealed class RecentPlans<TPlan> where TPlan : class
{
    private const int Slots = 64;

    private sealed record class Entry(object First, object Second, object? Third, TPlan Plan);

    private readonly Entry?[] _entries = new Entry?[Slots];

    /// <summary>
    /// The plan last asked for with these very references, if it is still in its slot.
    /// </summary>
    public bool TryGet(object first, object second, object? third, [MaybeNullWhen(false)] out TPlan plan)
    {
        if (_entries[SlotOf(first, second, third)] is { } entry
            && ReferenceEquals(entry.First, first) && ReferenceEquals(entry.Second, second) && ReferenceEquals(entry.Third, third))
        {
            plan = entry.Plan;
            return true;
        }

        plan = null;
        return false;
    }

    /// <summary>
    /// Keeps the plan that was asked for with these references, in place of whatever its slot held.
    /// </summary>
    public void Add(object first, object second, object? third, TPlan plan) => _entries[SlotOf(first, second, third)] = new Entry(first, second, third, plan);

    private static int SlotOf(object first, object second, object? third)
        => (RuntimeHelpers.GetHashCode(first) ^ RuntimeHelpers.GetHashCode(second) * 31 ^ (third is null ? 0 : RuntimeHelpers.GetHashCode(third) * 17)) & (Slots - 1);
}

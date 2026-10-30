using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The error-handling states an instruction can run in (<strong>MS-VBAL §5.4.4</strong>), over every code path to it: which <c>On Error</c> statement is in effect, and
/// whether an error is being handled.
/// </summary>
/// <remarks>
/// <para>
/// The states are propagated from the start of the activation (<see cref="ErrorHandlingState.Initial"/>) along the code paths, including where a <c>Resume</c> can continue
/// (an approximation: see <see cref="ControlFlowImprecision.Resumption"/>):
/// </para>
/// <list type="bullet">
/// <item><description>an <c>On Error</c> statement sets the policy (<strong>MS-VBAL §5.4.4.1</strong>);</description></item>
/// <item><description>an error raised under <c>On Error GoTo</c> reaches the handler with the error active, and a <c>Resume</c> ends it (<strong>MS-VBAL §5.4.4.2</strong>),
/// with the policy as it was;</description></item>
/// <item><description>an <c>On Error</c> statement run while an error is active may or may not end it: MS-VBA keeps the active error through an <c>On Error GoTo</c> other than
/// <c>-1</c>, where the runtime ends it with any of them, so both states are kept.</description></item>
/// </list>
/// <para>
/// A state is in the set of an instruction when some code path in the graph reaches it in that state: the answer to "can an error raised here reach a handler?" is exact
/// for those paths, and "it cannot" is true of every path the graph has.
/// </para>
/// </remarks>
public sealed class ErrorHandlingStates
{
    private readonly ImmutableHashSet<ErrorHandlingState>[] _states;

    private ErrorHandlingStates(ControlFlowGraph graph, ImmutableHashSet<ErrorHandlingState>[] states)
    {
        Graph = graph;
        _states = states;
    }

    /// <summary>The graph the states were propagated over.</summary>
    public ControlFlowGraph Graph { get; }

    /// <summary>
    /// The states the instruction at <paramref name="node"/> can run in: before it executes. Empty when control cannot reach it.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    public ImmutableHashSet<ErrorHandlingState> At(int node) => _states[node];

    /// <summary>
    /// The handlers an error raised by the instruction at <paramref name="node"/> can go to: the offsets of their first instruction.
    /// </summary>
    /// <param name="node">The offset of an instruction.</param>
    public ImmutableArray<int> HandlersAt(int node) => [.. _states[node].Select(state => state.CatchingHandler).OfType<int>().Distinct().Order()];

    /// <summary>
    /// Propagates the states of a procedure body.
    /// </summary>
    /// <param name="graph">The code paths of the body.</param>
    public static ErrorHandlingStates Of(ControlFlowGraph graph)
    {
        var edges = FlowEdges.Of(graph);
        var states = Enumerable.Range(0, edges.Count).Select(_ => new HashSet<ErrorHandlingState>()).ToArray();
        var items = graph.Instructions.Items;

        var pending = new Queue<(int Node, ErrorHandlingState State)>();
        pending.Enqueue((graph.Entry, ErrorHandlingState.Initial));
        while (pending.TryDequeue(out var arrival))
        {
            if (!states[arrival.Node].Add(arrival.State) || arrival.Node == graph.Exit)
            {
                continue;
            }

            var instruction = items[arrival.Node];
            foreach (var edge in edges.SuccessorsOf(arrival.Node))
            {
                if (edge.Kind is ControlFlowEdgeKind.Error)
                {
                    // the handler is taken in this state only: the error is active until something resumes or ends it.
                    if (arrival.State.CatchingHandler == edge.To)
                    {
                        pending.Enqueue((edge.To, arrival.State with { IsHandling = true }));
                    }

                    continue;
                }

                foreach (var after in After(instruction, arrival.State))
                {
                    pending.Enqueue((edge.To, after));
                }
            }
        }

        return new ErrorHandlingStates(graph, [.. states.Select(set => set.ToImmutableHashSet())]);
    }

    // what running the instruction to completion leaves.
    private static IEnumerable<ErrorHandlingState> After(Instruction instruction, ErrorHandlingState before)
    {
        switch (instruction.Kind)
        {
            case InstructionKind.OnErrorGoTo:
                return Set(new ErrorHandlingState(ErrorHandlingPolicy.GoTo, instruction.Target, false), before);
            case InstructionKind.OnErrorResumeNext:
                return Set(new ErrorHandlingState(ErrorHandlingPolicy.ResumeNext, null, false), before);
            case InstructionKind.OnErrorDisable:
                // On Error GoTo -1 ends the active error, and On Error GoTo 0 is lowered to the same instruction.
                return Set(ErrorHandlingState.Initial, before);
            case InstructionKind.ResumeCurrentStatement:
            case InstructionKind.ResumeNext:
            case InstructionKind.ResumeLabel:
                return [before with { IsHandling = false }];
            default:
                return [before];
        }
    }

    private static IEnumerable<ErrorHandlingState> Set(ErrorHandlingState policy, ErrorHandlingState before)
        => before.IsHandling ? [policy, policy with { IsHandling = true }] : [policy];
}

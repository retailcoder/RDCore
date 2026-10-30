using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The variables whose value may still be read, at each instruction of a procedure: <em>live</em> variables, over every code path, loops and handlers included.
/// </summary>
/// <remarks>
/// <para>
/// A variable is live before an instruction when some code path from there reads its value before anything replaces it whole (a
/// <see cref="VariableDefinition.IsComplete"/> definition). It is a backward analysis over the graph, and the answer is what makes the loop case right: a value
/// written in the body of a loop and read at its top, on the next iteration, is live after the write.
/// </para>
/// <para>
/// What may happen counts as happening, so that "not live" is true on every path the graph has:
/// </para>
/// <list type="bullet">
/// <item><description>an instruction that raises an error into a handler does not complete, so what it would have replaced is still live on that edge;</description></item>
/// <item><description>under <c>On Error Resume Next</c>, an instruction may stop part way and continue at the next one, so it replaces nothing;</description></item>
/// <item><description>a <c>Resume</c> may continue at any instruction that raised into a handler (<see cref="ControlFlowImprecision.Resumption"/>);</description></item>
/// <item><description>at the end of the activation, what outlives it is live: a <c>ByRef</c> parameter, a field, a <c>Static</c> local, the result of a function
/// (<see cref="DefinitionsAndUses.OutliveTheActivation"/>).</description></item>
/// </list>
/// </remarks>
public sealed class LiveVariables
{
    private readonly FlowEdges _edges;
    private readonly ImmutableHashSet<SemanticId>[] _liveBefore;

    private LiveVariables(ControlFlowGraph graph, FlowEdges edges, ImmutableHashSet<SemanticId>[] liveBefore)
    {
        Graph = graph;
        _edges = edges;
        _liveBefore = liveBefore;
    }

    /// <summary>The graph the analysis is of.</summary>
    public ControlFlowGraph Graph { get; }

    /// <summary>
    /// The variables live before the instruction at <paramref name="node"/> executes.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    public ImmutableHashSet<SemanticId> LiveBefore(int node) => _liveBefore[node];

    /// <summary>
    /// The variables live once the instruction at <paramref name="node"/> completes: live before one of the instructions it can continue at.
    /// </summary>
    /// <param name="node">The offset of an instruction.</param>
    public ImmutableHashSet<SemanticId> LiveAfter(int node)
        => [.. _edges.SuccessorsOf(node).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error).SelectMany(edge => _liveBefore[edge.To])];

    /// <summary>
    /// The variables live as control leaves a region of instructions, by any edge: falling out of it, jumping out of it, or raising an error into a handler outside it.
    /// </summary>
    /// <param name="region">The offsets of the instructions of the region.</param>
    /// <remarks>What code moved to another procedure must give back to its caller is what is written in it and live here.</remarks>
    public ImmutableHashSet<SemanticId> LiveAfter(IReadOnlySet<int> region)
        => [.. region.SelectMany(node => _edges.SuccessorsOf(node)).Where(edge => !region.Contains(edge.To)).SelectMany(edge => _liveBefore[edge.To])];

    /// <summary>
    /// Whether <paramref name="variable"/> is live before the instruction at <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    /// <param name="variable">The variable.</param>
    public bool IsLiveBefore(int node, SemanticId variable) => _liveBefore[node].Contains(variable);

    /// <summary>
    /// Analyzes a procedure body.
    /// </summary>
    /// <param name="graph">The code paths of the body.</param>
    /// <param name="accesses">The definitions and uses of its variables.</param>
    public static LiveVariables Of(ControlFlowGraph graph, DefinitionsAndUses accesses)
    {
        var edges = FlowEdges.Of(graph);
        var resumesNext = ResumesNext(graph);
        var live = Enumerable.Range(0, edges.Count).Select(_ => new HashSet<SemanticId>()).ToArray();
        live[graph.Exit].UnionWith(accesses.OutliveTheActivation);

        var pending = new Queue<int>(Enumerable.Range(0, edges.Count).Reverse());
        var queued = Enumerable.Repeat(true, edges.Count).ToArray();
        while (pending.TryDequeue(out var node))
        {
            queued[node] = false;
            if (node == graph.Exit)
            {
                Requeue(node);
                continue;
            }

            var after = new HashSet<SemanticId>();
            var raised = new HashSet<SemanticId>();
            foreach (var edge in edges.SuccessorsOf(node))
            {
                (edge.Kind is ControlFlowEdgeKind.Error ? raised : after).UnionWith(live[edge.To]);
            }

            if (!resumesNext[node])
            {
                after.ExceptWith(accesses.DefinitionsAt(node).Where(definition => definition.IsComplete).Select(definition => definition.Variable));
            }

            after.UnionWith(raised);
            after.UnionWith(accesses.UsesAt(node).Select(use => use.Variable));
            if (!after.SetEquals(live[node]))
            {
                live[node] = after;
                Requeue(node);
            }
        }

        return new LiveVariables(graph, edges, [.. live.Select(set => set.ToImmutableHashSet())]);

        void Requeue(int node)
        {
            foreach (var edge in edges.PredecessorsOf(node).Where(edge => !queued[edge.From]))
            {
                queued[edge.From] = true;
                pending.Enqueue(edge.From);
            }
        }
    }

    // the instructions that may run under On Error Resume Next: one may stop part way, and what it would have written is not written.
    internal static bool[] ResumesNext(ControlFlowGraph graph)
    {
        var states = ErrorHandlingStates.Of(graph);
        return [.. Enumerable.Range(0, graph.Instructions.Items.Length + 1)
            .Select(node => states.At(node).Any(state => state is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false }))];
    }
}

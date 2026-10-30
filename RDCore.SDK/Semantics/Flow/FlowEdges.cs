using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The edges a data-flow analysis walks: the <see cref="ControlFlowGraph"/>'s, and where a <c>Resume</c> can continue, which the graph has no edge for
/// (<see cref="ControlFlowImprecision.Resumption"/>).
/// </summary>
/// <remarks>
/// An analysis that misses a path states facts that are false on it: a write a later <c>Resume</c> reads would look dead. So a <c>Resume</c> is given an edge to every
/// instruction that can raise an error into a handler (it executes the fault statement again, <strong>MS-VBAL §5.4.4.2</strong>), and a <c>Resume Next</c> an edge to
/// every instruction after one. Which of them is the fault statement is known only at run time: the edges are the ones that are possible, and some that are not.
/// </remarks>
internal sealed class FlowEdges
{
    private readonly ImmutableArray<ControlFlowEdge>[] _successors;
    private readonly ImmutableArray<ControlFlowEdge>[] _predecessors;

    private FlowEdges(ImmutableArray<ControlFlowEdge>[] successors)
    {
        _successors = successors;
        var predecessors = Enumerable.Range(0, successors.Length).Select(_ => new List<ControlFlowEdge>()).ToArray();
        foreach (var edge in successors.SelectMany(edges => edges))
        {
            predecessors[edge.To].Add(edge);
        }

        _predecessors = [.. predecessors.Select(edges => edges.ToImmutableArray())];
    }

    /// <summary>The number of nodes: every instruction, and the exit.</summary>
    public int Count => _successors.Length;

    public ImmutableArray<ControlFlowEdge> SuccessorsOf(int node) => _successors[node];

    public ImmutableArray<ControlFlowEdge> PredecessorsOf(int node) => _predecessors[node];

    public static FlowEdges Of(ControlFlowGraph graph)
    {
        var items = graph.Instructions.Items;
        var successors = Enumerable.Range(0, items.Length + 1).Select(node => graph.SuccessorsOf(node).ToList()).ToArray();

        var faults = items.Where(instruction => graph.SuccessorsOf(instruction.Offset).Any(edge => edge.Kind is ControlFlowEdgeKind.Error)).ToArray();
        var afterFaults = faults
            .SelectMany(fault => graph.SuccessorsOf(fault.Offset).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error).Select(edge => edge.To).Append(fault.Offset + 1))
            .Distinct()
            .ToArray();

        foreach (var resume in items)
        {
            var targets = resume.Kind switch
            {
                InstructionKind.ResumeCurrentStatement => faults.Select(fault => fault.Offset),
                InstructionKind.ResumeNext => afterFaults,
                _ => [],
            };

            foreach (var target in targets)
            {
                var edge = new ControlFlowEdge(resume.Offset, target, ControlFlowEdgeKind.Branch);
                if (!successors[resume.Offset].Contains(edge))
                {
                    successors[resume.Offset].Add(edge);
                }
            }
        }

        return new FlowEdges([.. successors.Select(edges => edges.ToImmutableArray())]);
    }
}

using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// The ordered, keyed instruction list of one procedure body — <strong>RD-VBAL §3.5</strong>
/// (<strong>MS-VBAL §2.3.1</strong>: "sequentially evaluate each instruction in the frame"). Built by
/// <see cref="InstructionListLowering"/>; immutable.
/// </summary>
/// <remarks>
/// <see cref="Items"/> is dense: index <c>i</c> is offset <c>i</c>, and every offset a program counter
/// can hold names an entry. A label may resolve to <c>Items.Length</c> itself (a label with nothing
/// after it) — that offset is valid to hold, and a fetch there completes as if execution had reached
/// the end of the body.
/// </remarks>
public sealed class InstructionList
{
    private readonly IReadOnlyDictionary<string, int> _labels;
    private readonly IReadOnlyDictionary<SyntaxNodeId, int> _byNode;

    internal InstructionList(ImmutableArray<Instruction> items, IReadOnlyDictionary<string, int> labels, IReadOnlyDictionary<SyntaxNodeId, int> byNode)
    {
        Items = items;
        _labels = labels;
        _byNode = byNode;
    }

    /// <summary>
    /// Every instruction, in program-counter order. <c>Items[i].Offset == i</c> for every <c>i</c>.
    /// </summary>
    public ImmutableArray<Instruction> Items { get; }

    /// <summary>
    /// Looks up the offset a <em>line label</em> or <em>line number</em> defines, by name
    /// (<strong>MS-VBAL §5.4.1.1</strong>). Case-insensitive, like every VBA identifier.
    /// </summary>
    /// <param name="label">The label name, or a line number's decimal string form.</param>
    /// <returns><c>false</c> when this procedure defines no such label.</returns>
    public bool TryGetLabelOffset(string label, out int offset) => _labels.TryGetValue(label, out offset);

    /// <summary>
    /// Looks up the offset a source <see cref="StatementNode"/> lowered to, by its stable
    /// <see cref="SyntaxNodeId"/> — the fault-statement identity a breakpoint or a runtime error
    /// anchors to.
    /// </summary>
    /// <returns><c>false</c> when <paramref name="nodeId"/> is not a statement of this body.</returns>
    public bool TryGetOffset(SyntaxNodeId nodeId, out int offset) => _byNode.TryGetValue(nodeId, out offset);
}

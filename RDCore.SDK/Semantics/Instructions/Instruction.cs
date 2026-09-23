using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// One entry of an <see cref="InstructionList"/> — <strong>RD-VBAL §3.5</strong>. <see cref="Offset"/>
/// is both the entry's index into <see cref="InstructionList.Items"/> and the program-counter value
/// that fetches it.
/// </summary>
/// <param name="Offset">The entry's index into the owning <see cref="InstructionList.Items"/>.</param>
/// <param name="Node">The <see cref="StatementNode"/> this instruction was lowered from.</param>
/// <param name="Kind">The control-flow shape of this instruction.</param>
/// <param name="Target">
/// For <see cref="InstructionKind.Jump"/>: the resolved offset to branch to, or <c>null</c> when the
/// operand did not resolve to a label the procedure defines (lowering already reported the
/// <see cref="RDCore.SDK.Model.Errors.VBCompileErrorId.LabelNotDefined"/> diagnostic for it). Unused
/// otherwise.
/// </param>
/// <param name="Targets">
/// For <see cref="InstructionKind.JumpTable"/>: the resolved offset for each label in source order,
/// with a <c>null</c> entry wherever the corresponding label did not resolve. Empty otherwise.
/// </param>
public sealed record class Instruction(int Offset, StatementNode Node, InstructionKind Kind, int? Target, ImmutableArray<int?> Targets);

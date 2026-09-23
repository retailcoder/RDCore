namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// Discriminates the control-flow shape of an <see cref="Instruction"/> within an
/// <see cref="InstructionList"/> — RD-VBAL §3.5.
/// </summary>
/// <remarks>
/// A statement's own runtime semantics stay pure (<strong>RD-VBAL §3.5</strong>: they never mutate
/// control state); <see cref="InstructionKind"/> only tells the interpreter's fetch/decode loop which
/// pre-resolved offset on <see cref="Instruction"/> to consult, and whether to consult it at all.
/// </remarks>
public enum InstructionKind
{
    /// <summary>
    /// Falls through to the following offset. Every statement kind not covered by another member of
    /// this enum lowers as <see cref="Simple"/> — including, for now, a block statement's own header
    /// (<c>If</c>/<c>Select Case</c>/a loop/<c>With</c>): its nested <c>Body</c> is not yet lowered.
    /// </summary>
    Simple,

    /// <summary>
    /// An unconditional branch (<c>GoTo</c>, <strong>MS-VBAL §5.4.2.12</strong>) to
    /// <see cref="Instruction.Target"/>.
    /// </summary>
    Jump,

    /// <summary>
    /// An indexed branch (<c>On…GoTo</c>, <strong>MS-VBAL §5.4.2.13</strong>) to one of
    /// <see cref="Instruction.Targets"/>, selected at runtime; an out-of-range selector falls through
    /// instead.
    /// </summary>
    JumpTable,

    /// <summary>
    /// <c>Exit Sub</c>/<c>Exit Function</c>/<c>Exit Property</c> (<strong>MS-VBAL §5.4.2.17</strong>–
    /// <strong>§5.4.2.19</strong>): ends the current activation as if execution had reached the end of
    /// its body.
    /// </summary>
    ExitProcedure,

    /// <summary>
    /// <c>End</c> (not a MS-VBAL-numbered statement): halts the whole program.
    /// </summary>
    Halt,

    /// <summary>
    /// <c>Stop</c> (<strong>MS-VBAL §5.4.2.11</strong>): suspends execution for a debugger to resume.
    /// </summary>
    Break,
}

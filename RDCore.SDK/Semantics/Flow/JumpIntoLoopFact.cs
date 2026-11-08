using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The statement that jumps to a label.
/// </summary>
public enum JumpStatementKind
{
    /// <summary>
    /// A <c>GoTo</c> statement (<strong>MS-VBAL §5.4.2.12</strong>).
    /// </summary>
    GoTo,

    /// <summary>
    /// A <c>Resume</c> statement that names a label (<strong>MS-VBAL §5.4.4.2</strong>).
    /// </summary>
    Resume,
}

/// <summary>
/// A statement that jumps from outside a <c>For</c> or a <c>For Each</c> loop to a label inside it, past the statement that opens the loop
/// (<strong>MS-VBAL §5.4.2.3</strong>, <strong>§5.4.2.4</strong>).
/// </summary>
/// <remarks>
/// The specification has the <c>Next</c> of a loop whose <c>For</c> statement never ran raise error 92 (For loop not initialized). MS-VBA raises it for a <c>For
/// Each</c> loop, and for a <c>For</c> loop whose counter is declared <c>Variant</c>; the <c>Next</c> of any other counter steps it on a loop that has a 0 end
/// (<strong>RD-VBAL §5.4.2.3</strong>). Whether the jump is a mistake is for an analyzer to say.
/// </remarks>
/// <param name="Statement">The statement that jumps.</param>
/// <param name="Location">Where the statement is written.</param>
public sealed record class JumpIntoLoopFact(JumpStatementKind Statement, SourceLocation Location);

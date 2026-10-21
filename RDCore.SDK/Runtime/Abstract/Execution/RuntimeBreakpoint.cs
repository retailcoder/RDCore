using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// A breakpoint as the interpreter has it: a line of a module's source, and the condition a program that reaches it waits on, if it has one.
/// </summary>
/// <param name="Line">The zero-based line of the source.</param>
/// <param name="Condition">
/// The expression that decides whether the program waits, <see langword="null"/> when it always does. It is evaluated in the activation that reaches the line, before
/// the instruction there runs, and let-coerced to <c>Boolean</c> the way the condition of an <c>If</c> is (<strong>MS-VBAL §5.5.1.2</strong>): the program waits
/// when it is <c>True</c>, and goes on when it is <c>False</c>. A condition that cannot be evaluated - one that raises a run-time error - waits the program all the
/// same, since a breakpoint that is silently passed over is one the person never learns is wrong.
/// </param>
/// <remarks>
/// The condition is a parsed tree and not source text: what a client typed is parsed where the parser is, and only an expression reaches the interpreter.
/// </remarks>
public sealed record class RuntimeBreakpoint(int Line, ExpressionNode? Condition = null);

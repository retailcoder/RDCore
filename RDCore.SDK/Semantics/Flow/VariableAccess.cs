using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// How an instruction gives a variable a value.
/// </summary>
public enum VariableDefinitionKind
{
    /// <summary>
    /// The value the variable has when the activation starts: a parameter's argument, a local's initial value, a field's current value. Not an instruction's.
    /// </summary>
    Entry,

    /// <summary>
    /// An assignment of the whole variable: <c>Let</c>, <c>Set</c>, <c>LSet</c>, <c>RSet</c>.
    /// </summary>
    Assignment,

    /// <summary>
    /// The control variable of a <c>For</c> or <c>For Each</c> loop, assigned by the loop.
    /// </summary>
    LoopControl,

    /// <summary>
    /// <c>ReDim</c> of an array, or <c>Erase</c>.
    /// </summary>
    Redimension,

    /// <summary>
    /// A write to a part of the variable: an element of an array, a member of a <c>Type</c>, a part of a string (<c>Mid</c>), the elements a <c>ReDim Preserve</c>
    /// keeps.
    /// </summary>
    PartialWrite,

    /// <summary>
    /// An argument the procedure called may take by reference, and so write to.
    /// </summary>
    ByRefArgument,

    /// <summary>
    /// A variable a file statement reads into: <c>Input #</c>, <c>Line Input #</c>, <c>Get #</c>.
    /// </summary>
    FileInput,
}

/// <summary>
/// A place where a variable may be given a value.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Offset">The offset of the instruction that defines it; <c>-1</c> for <see cref="VariableDefinitionKind.Entry"/>.</param>
/// <param name="Kind">How.</param>
/// <param name="IsComplete">
/// Whether the whole value is replaced, every time the instruction completes: what ends the life of the value before. A write to a part, and an argument that may or may
/// not be written, is not.
/// </param>
/// <param name="Expression">The expression that names the variable there; <see langword="null"/> for <see cref="VariableDefinitionKind.Entry"/>.</param>
public sealed record class VariableDefinition(SemanticId Variable, int Offset, VariableDefinitionKind Kind, bool IsComplete, SyntaxNode? Expression);

/// <summary>
/// A place where the value of a variable may be read.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Offset">The offset of the instruction that reads it.</param>
/// <param name="Expression">The expression that names it there.</param>
public sealed record class VariableUse(SemanticId Variable, int Offset, SyntaxNode Expression);

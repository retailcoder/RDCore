using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics;

/// <summary>
/// What an expression is classified as (<strong>MS-VBAL §5.6.1</strong>): what it names, which decides what may be done with it.
/// </summary>
public enum ExpressionClassification
{
    /// <summary>
    /// What it is could not be told: a name that did not resolve, or a kind of expression the static pass does not classify yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// A value, which is not something that can be assigned to: a literal, the result of an operator, of a call or of an index.
    /// </summary>
    Value,

    /// <summary>
    /// A variable: a local, a parameter, a field, which has storage and can be assigned to.
    /// </summary>
    Variable,

    /// <summary>
    /// A constant: its value is known where it is written, and is substituted there.
    /// </summary>
    Constant,

    /// <summary>
    /// A <c>Function</c>.
    /// </summary>
    Function,

    /// <summary>
    /// A <c>Property</c>.
    /// </summary>
    Property,

    /// <summary>
    /// A <c>Sub</c>.
    /// </summary>
    Subroutine,

    /// <summary>
    /// A type: a class, a <c>Type</c> or an <c>Enum</c>.
    /// </summary>
    Type,

    /// <summary>
    /// A project or a procedural module, which a member is looked up in (<strong>MS-VBAL §5.6.12</strong>).
    /// </summary>
    Namespace,

    /// <summary>
    /// A member of an object whose type does not say what it has: it is bound when the expression runs.
    /// </summary>
    UnboundMember,

    /// <summary>
    /// An operator expression, evaluates to a value.
    /// </summary>
    Operation,

    /// <summary>
    /// A conversion operation, whether implicit or explicit, converts a value to another type.
    /// </summary>
    Conversion,
}

/// <summary>
/// What the static pass found out about one expression.
/// </summary>
/// <remarks>
/// A fact is a description of the expression, not an opinion of it: whether a late-bound member, a name written in the wrong case or a constant condition
/// is worth a diagnostic is for an analyzer to say.
/// </remarks>
/// <param name="NodeId">The expression the fact describes.</param>
/// <param name="Location">Where the expression is written.</param>
/// <param name="DeclaredType">The declared type of the expression (<strong>RD-VBAL §5.0.1</strong>), or <see langword="null"/> when it is an error.</param>
/// <param name="Classification">What the expression names.</param>
/// <param name="Binding">The symbol the expression refers to, when it refers to one that resolved.</param>
/// <param name="ExpressionFlags">The semantic flags of the expression.</param>
/// <param name="Error">The compile error of the expression itself or of the first of its operands that has one, when it has.</param>
public sealed record class ExpressionFact(
    SyntaxNodeId NodeId,
    SourceLocation Location,
    VBType? DeclaredType,
    ExpressionClassification Classification,
    SemanticId? Binding,
    ValueExpressionSemanticFlags ExpressionFlags,
    VBCompileErrorInfo? Error = null)
{
    /// <summary>
    /// The semantic flags for each conversion operation carried by this expression, per operand.
    /// </summary>
    /// <remarks>
    /// Unary operators use <see cref="InputIndex.UnaryOperand"/>; for binary operators, use <see cref="InputIndex.BinaryLeftOperand"/> and <see cref="InputIndex.BinaryRightOperand"/>.
    /// </remarks>
    public ImmutableDictionary<InputIndex, ConversionSemanticFlags> ConversionFlags { get; init; } = [];

    /// <summary>
    /// The semantic flags for each arithmetic operator operation carried by this expression, per operand.
    /// </summary>
    /// <remarks>
    /// Unary operators use <see cref="InputIndex.UnaryOperand"/>; for binary operators, use <see cref="InputIndex.BinaryLeftOperand"/> and <see cref="InputIndex.BinaryRightOperand"/>.
    /// Empty for non-arithmetic operator expressions.
    /// </remarks>
    public ImmutableDictionary<InputIndex, ArithmeticOperatorSemanticFlags> ArithmeticOperatorFlags { get; init; } = [];

    /// <summary>
    /// The semantic flags for each logical operator operation carried by this expression, per operand.
    /// </summary>
    /// <remarks>
    /// Unary operators use <see cref="InputIndex.UnaryOperand"/>; for binary operators, use <see cref="InputIndex.BinaryLeftOperand"/> and <see cref="InputIndex.BinaryRightOperand"/>.
    /// Empty for non-logical operator expressions.
    /// </remarks>
    public ImmutableDictionary<InputIndex, LogicalOperatorSemanticFlags> LogicalOperatorFlags { get; init; } = [];

    /// <summary>
    /// The semantic flags for each comparison operator operation carried by this expression, per operand.
    /// </summary>
    /// <remarks>
    /// Unary operators use <see cref="InputIndex.UnaryOperand"/>; for binary operators, use <see cref="InputIndex.BinaryLeftOperand"/> and <see cref="InputIndex.BinaryRightOperand"/>.
    /// Empty for non-comparison operator expressions.
    /// </remarks>
    public ImmutableDictionary<InputIndex, ComparisonOperatorSemanticFlags> ComparisonOperatorFlags { get; init; } = [];

    /// <summary>
    /// The semantic flags for each concatenation operator operation carried by this expression, per operand.
    /// </summary>
    /// <remarks>
    /// Unary operators use <see cref="InputIndex.UnaryOperand"/>; for binary operators, use <see cref="InputIndex.BinaryLeftOperand"/> and <see cref="InputIndex.BinaryRightOperand"/>.
    /// Empty for non-concatenation operator expressions.
    /// </remarks>
    public ImmutableDictionary<InputIndex, ConcatOperationSemanticFlags> ConcatOperationFlags { get; init; } = [];

}

/// <summary>
/// Takes the facts of the expressions the static pass evaluates.
/// </summary>
public interface IExpressionFactSink
{
    /// <summary>
    /// Records what is known of an expression. An expression that is evaluated again has its facts replaced.
    /// </summary>
    /// <param name="facts">The semantic facts recorded for the expression node.</param>
    void Record(ExpressionFact facts);

    /// <summary>
    /// Gets the facts of an expression the pass has evaluated, which it does for an operand before the expression that has it.
    /// </summary>
    /// <param name="node">The expression node ID.</param>
    /// <param name="facts">The associated facts.</param>
    bool TryGet(SyntaxNodeId node, out ExpressionFact? facts);
}

/// <summary>
/// An <see cref="IExpressionFactSink"/> that keeps what it is given, by the expression.
/// </summary>
public sealed class ExpressionFactCollector : IExpressionFactSink
{
    private readonly Dictionary<SyntaxNodeId, ExpressionFact> _facts = [];

    /// <inheritdoc/>
    public void Record(ExpressionFact facts)
        => _facts[facts.NodeId] = facts;

    /// <inheritdoc/>
    public bool TryGet(SyntaxNodeId node, out ExpressionFact? facts)
        => _facts.TryGetValue(node, out facts);

    /// <summary>
    /// The facts recorded so far, by expression.
    /// </summary>
    public ImmutableDictionary<SyntaxNodeId, ExpressionFact> ToImmutable()
        => _facts.ToImmutableDictionary();
}

/// <summary>
/// Extension methods for enriching <see cref="ExpressionFact"/> records with operator-specific semantic flags.
/// </summary>
public static class ExpressionFactEnrichment
{
    /// <summary>
    /// Returns a new <see cref="ExpressionFact"/> with conversion flags set for the specified operand.
    /// </summary>
    public static ExpressionFact WithConversionFlags(
        this ExpressionFact fact,
        InputIndex operand,
        ConversionSemanticFlags flags)
    {
        if (flags == 0)
        {
            return fact; // No-op if no flags to add
        }

        var updated = fact.ConversionFlags.ToBuilder();
        if (updated.TryGetValue(operand, out var existing))
        {
            updated[operand] = existing | flags;
        }
        else
        {
            updated[operand] = flags;
        }

        return fact with { ConversionFlags = updated.ToImmutable() };
    }

    /// <summary>
    /// Returns a new <see cref="ExpressionFact"/> with arithmetic operator flags set for the specified operand.
    /// </summary>
    public static ExpressionFact WithArithmeticOperatorFlags(
        this ExpressionFact fact,
        InputIndex operand,
        ArithmeticOperatorSemanticFlags flags)
    {
        if (flags == 0)
        {
            return fact; // No-op if no flags to add
        }

        var updated = fact.ArithmeticOperatorFlags.ToBuilder();
        if (updated.TryGetValue(operand, out var existing))
        {
            updated[operand] = existing | flags;
        }
        else
        {
            updated[operand] = flags;
        }

        return fact with { ArithmeticOperatorFlags = updated.ToImmutable() };
    }

    /// <summary>
    /// Returns a new <see cref="ExpressionFact"/> with logical operator flags set for the specified operand.
    /// </summary>
    public static ExpressionFact WithLogicalOperatorFlags(
        this ExpressionFact fact,
        InputIndex operand,
        LogicalOperatorSemanticFlags flags)
    {
        if (flags == 0)
        {
            return fact; // No-op if no flags to add
        }

        var updated = fact.LogicalOperatorFlags.ToBuilder();
        if (updated.TryGetValue(operand, out var existing))
        {
            updated[operand] = existing | flags;
        }
        else
        {
            updated[operand] = flags;
        }

        return fact with { LogicalOperatorFlags = updated.ToImmutable() };
    }

    /// <summary>
    /// Returns a new <see cref="ExpressionFact"/> with comparison operator flags set for the specified operand.
    /// </summary>
    public static ExpressionFact WithComparisonOperatorFlags(
        this ExpressionFact fact,
        InputIndex operand,
        ComparisonOperatorSemanticFlags flags)
    {
        if (flags == 0)
        {
            return fact; // No-op if no flags to add
        }

        var updated = fact.ComparisonOperatorFlags.ToBuilder();
        if (updated.TryGetValue(operand, out var existing))
        {
            updated[operand] = existing | flags;
        }
        else
        {
            updated[operand] = flags;
        }

        return fact with { ComparisonOperatorFlags = updated.ToImmutable() };
    }

    /// <summary>
    /// Returns a new <see cref="ExpressionFact"/> with concatenation operator flags set for the specified operand.
    /// </summary>
    public static ExpressionFact WithConcatOperationFlags(
        this ExpressionFact fact,
        InputIndex operand,
        ConcatOperationSemanticFlags flags)
    {
        if (flags == 0)
        {
            return fact; // No-op if no flags to add
        }

        var updated = fact.ConcatOperationFlags.ToBuilder();
        if (updated.TryGetValue(operand, out var existing))
        {
            updated[operand] = existing | flags;
        }
        else
        {
            updated[operand] = flags;
        }

        return fact with { ConcatOperationFlags = updated.ToImmutable() };
    }
}

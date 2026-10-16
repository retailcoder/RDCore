using RDCore.SDK.Model;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.2</strong> Binary '+' Operator (static semantics)
/// </summary>
public sealed record class BinaryAdditionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.AdditionOp;
}

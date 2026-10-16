using RDCore.SDK.Model;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.4</strong> Binary '&amp;' Operator (static semantics)
/// </summary>
public sealed record class BinaryConcatOperatorStaticSemantics : PlannedOperatorStaticSemantics
{
    protected override string Operator => Tokens.ConcatOp;
}

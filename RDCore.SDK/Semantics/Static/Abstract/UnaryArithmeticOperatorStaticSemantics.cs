namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// The unary arithmetic operators' static semantics (<strong>MS-VBAL 5.6.9.3</strong>): the declared type of each is the plan of its operation, made for the
/// declared type of its operand (<see cref="PlannedOperatorStaticSemantics"/>).
/// </summary>
/// <remarks>
/// This is implicitly the specification for the unary '+' operator, which is omitted from MS-VBAL.
/// </remarks>
public abstract record class UnaryArithmeticOperatorStaticSemantics : PlannedOperatorStaticSemantics;

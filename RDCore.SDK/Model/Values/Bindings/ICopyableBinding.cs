namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A cell that makes its own copy, when a copy of what it holds is made: an element of an array that is assigned (<see cref="Abstract.DataValues.CopyCell"/>).
/// </summary>
/// <remarks>
/// A runtime that keeps a value in a cell of its own representation - a scalar as a payload - gives a copy that is kept the same way, rather than a cell
/// that holds the value as it is. The copy holds the same value, known or not as the cell is, and shares nothing with it: a write to one is not seen by the
/// other.
/// </remarks>
public interface ICopyableBinding : IKnowledgeBinding
{
    /// <summary>
    /// A new cell holding what this one holds.
    /// </summary>
    IBindingHandle Copy();
}

using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Refactoring.ExtractMethod;

/// <summary>
/// How a variable is passed to the procedure the selection is extracted as.
/// </summary>
public enum ExtractionPassing
{
    /// <summary>Its value is passed: the new procedure reads it, and what it writes to it stays its own.</summary>
    ByVal,

    /// <summary>The variable is passed: what the new procedure writes to it is the caller's.</summary>
    ByRef,
}

/// <summary>
/// Why a variable is passed as it is: what the preview says of it ("ByRef: written here, read after").
/// </summary>
public enum ExtractionPassingReason
{
    /// <summary>The selection reads a value given before it, and gives it no value that is read after it.</summary>
    ReadOnly,

    /// <summary>The selection reads a value given before it, and gives it one that is read after it - on a later iteration of a loop, too.</summary>
    ReadAndWritten,

    /// <summary>The selection gives it a value that is read after it, and reads none given before: it may be the new procedure's return value instead.</summary>
    WrittenOnly,

    /// <summary>An array, which VBA passes by reference only.</summary>
    Array,

    /// <summary>A variable of a <c>Type</c>, which VBA passes by reference only.</summary>
    UserDefinedType,

    /// <summary>A fixed-length string, which is passed as a <c>String</c> by reference, so that what is written to it comes back.</summary>
    FixedLengthString,

    /// <summary>A <c>Static</c> local, which belongs to the source procedure's lifetime: it is never moved into the new one.</summary>
    Static,
}

/// <summary>
/// A variable of the source procedure that the new procedure is given.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Name">Its name, which the parameter is named for.</param>
/// <param name="Type">Its declared type, which the parameter is declared as; <see langword="null"/> when it is not known (a <c>Variant</c>).</param>
/// <param name="Passing">How it is passed.</param>
/// <param name="Reason">Why.</param>
/// <param name="IsInput">Whether the selection reads a value given before it.</param>
/// <param name="IsOutput">Whether the selection gives it a value that is read after it.</param>
public sealed record class ExtractionParameter(SemanticId Variable, string Name, VBType? Type, ExtractionPassing Passing, ExtractionPassingReason Reason, bool IsInput, bool IsOutput);

/// <summary>
/// A variable the new procedure declares as a local of its own.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its declared type; <see langword="null"/> when it is not known.</param>
/// <param name="MovesDeclaration">
/// Whether its declaration moves into the new procedure: it is used nowhere else. Otherwise the source procedure keeps its own, and the new one declares another:
/// what the selection does with it neither comes from nor goes to the rest of the source procedure.
/// </param>
public sealed record class ExtractionLocal(SemanticId Variable, string Name, VBType? Type, bool MovesDeclaration);

/// <summary>
/// What extracting a selection as a procedure takes: what it is given, what it gives back, and what moves (<strong>Extract Method §3.2, §3.3</strong>).
/// </summary>
/// <param name="Selection">The statements extracted.</param>
/// <param name="Parameters">The variables the new procedure is given, in the order the selection first touches them.</param>
/// <param name="Locals">The variables the new procedure declares as its own.</param>
/// <param name="MovedOut">The variables declared in the selection and used after it, whose declaration moves out, before the call.</param>
/// <param name="AssignsResult">Whether the selection assigns the source procedure's own result, which the call site assigns from the new procedure.</param>
public sealed record class ExtractionPlan(
    ExtractionSelection Selection,
    ImmutableArray<ExtractionParameter> Parameters,
    ImmutableArray<ExtractionLocal> Locals,
    ImmutableArray<SemanticId> MovedOut,
    bool AssignsResult)
{
    /// <summary>
    /// The parameters the new procedure may return instead: those it only gives a value to, which the call site can assign from it.
    /// </summary>
    public ImmutableArray<ExtractionParameter> ReturnCandidates => [.. Parameters.Where(parameter => parameter.Reason is ExtractionPassingReason.WrittenOnly)];
}

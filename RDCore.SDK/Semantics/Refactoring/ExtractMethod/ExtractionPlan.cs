using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
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

    /// <summary>
    /// The source procedure's <c>ParamArray</c>, which the selection only reads: it is passed as a <c>Variant</c> that holds the array of its elements (<strong>Extract
    /// Method §3.3</strong>). A selection that writes its elements is refused (<see cref="ExtractionRefusalKind.ParamArrayWritten"/>).
    /// </summary>
    ParamArray,
}

/// <summary>
/// Why a selection is not extracted, once what it does is known.
/// </summary>
public enum ExtractionRefusalKind
{
    /// <summary>
    /// The selection writes an element of the source procedure's <c>ParamArray</c>, which is a variable of its caller (<strong>MS-VBAL §5.3.1.5</strong>): a copy
    /// passed to the new procedure would lose the write.
    /// </summary>
    ParamArrayWritten,

    /// <summary>
    /// The selection uses a member of the object of a <c>With</c> block it is inside of, and the block names no variable: passing its object would evaluate the
    /// expression again (<c>With New Thing</c>, <c>With Sheet.Range("A1")</c>), where the block evaluates it once.
    /// </summary>
    WithTargetIsNotAVariable,
}

/// <summary>
/// A reason the selection is not extracted, and where it is.
/// </summary>
/// <param name="Kind">The reason.</param>
/// <param name="Location">What causes it: the expression that writes the element, the expression of the <c>With</c> block.</param>
/// <param name="Name">The name of the variable it is about, when there is one.</param>
public sealed record class ExtractionRefusal(ExtractionRefusalKind Kind, SourceLocation? Location, string? Name);

/// <summary>
/// The object of the <c>With</c> block the selection is inside of, which the selection uses a member of, relative to the block (<c>.Name</c>): the new procedure is
/// given it, and wraps the extracted code in a <c>With</c> block of its own (<strong>Extract Method §3.3</strong>).
/// </summary>
/// <param name="With">The block.</param>
/// <param name="Variable">The variable the block names.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its declared type; <see langword="null"/> when it is not known.</param>
/// <param name="Passing">How it is passed: by reference for a variable of a <c>Type</c>, whose members the selection may write; by value otherwise, an object.</param>
public sealed record class ExtractionWithTarget(WithStatementNode With, SemanticId Variable, string Name, VBType? Type, ExtractionPassing Passing);

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
/// <param name="MayBeMissing">
/// Whether it is a parameter of the source procedure that can be missing - an <c>Optional</c> <c>Variant</c> with no default value, or a <c>ParamArray</c> - so that
/// <c>IsMissing</c> of it means something: it is passed as a <c>Variant</c>. Whether <c>IsMissing</c> is still <c>True</c> of it in the new procedure is to be measured
/// (<strong>Extract Method §3.3</strong>).
/// </param>
/// <param name="IsUndeclared">Whether it is declared by being named, with no <c>Option Explicit</c>: the new procedure declares the parameter, as a <c>Variant</c>.</param>
public sealed record class ExtractionParameter(
    SemanticId Variable, string Name, VBType? Type, ExtractionPassing Passing, ExtractionPassingReason Reason, bool IsInput, bool IsOutput, bool MayBeMissing = false,
    bool IsUndeclared = false);

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
/// <param name="IsUndeclared">
/// Whether it is declared by being named, with no <c>Option Explicit</c>: the new procedure declares it explicitly, as a <c>Variant</c>, one step towards
/// <c>Option Explicit</c> (<strong>Extract Method §3.2</strong>).
/// </param>
public sealed record class ExtractionLocal(SemanticId Variable, string Name, VBType? Type, bool MovesDeclaration, bool IsUndeclared = false);

/// <summary>
/// What extracting a selection as a procedure takes: what it is given, what it gives back, and what moves (<strong>Extract Method §3.2, §3.3</strong>).
/// </summary>
/// <param name="Selection">The statements extracted.</param>
/// <param name="Parameters">The variables the new procedure is given, in the order the selection first touches them.</param>
/// <param name="Locals">The variables the new procedure declares as its own.</param>
/// <param name="MovedOut">
/// The variables declared in the selection whose declaration stays in the source procedure, before the call: those used after the selection, and its <c>Static</c>
/// locals, whose lifetime is the source procedure's.
/// </param>
/// <param name="AssignsResult">Whether the selection assigns the source procedure's own result, which the call site assigns from the new procedure.</param>
/// <param name="ReadsResult">
/// Whether the selection reads a value of the source procedure's own result given outside it: the new procedure is given it, since it cannot name the source's.
/// </param>
/// <param name="WithTarget">The object of the <c>With</c> block the selection is inside of and uses a member of; <see langword="null"/> when there is none.</param>
/// <param name="Refusals">Why the selection is not extracted; empty when it is.</param>
public sealed record class ExtractionPlan(
    ExtractionSelection Selection,
    ImmutableArray<ExtractionParameter> Parameters,
    ImmutableArray<ExtractionLocal> Locals,
    ImmutableArray<SemanticId> MovedOut,
    bool AssignsResult,
    bool ReadsResult,
    ExtractionWithTarget? WithTarget,
    ImmutableArray<ExtractionRefusal> Refusals)
{
    /// <summary>Whether the selection is extracted: nothing refuses it.</summary>
    public bool IsExtractable => Refusals.IsEmpty;

    /// <summary>
    /// The parameters the new procedure may return instead: those it only gives a value to, which the call site can assign from it.
    /// </summary>
    public ImmutableArray<ExtractionParameter> ReturnCandidates => [.. Parameters.Where(parameter => parameter.Reason is ExtractionPassingReason.WrittenOnly)];
}

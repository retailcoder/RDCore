using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Cli;

/// <summary>
/// Every kind of runtime fact the host states reaches a diagnostics extension: through <c>rdcore/host/semantics</c>, as JSON (<see cref="RuntimeFactsDto"/>).
/// </summary>
[TestClass]
public sealed class RuntimeFactsWireTests
{
    private const string Program = """
        Attribute VB_Name = "Program"
        Option Explicit
        Option Compare Text
        Public Sub Main(ByVal o As Object, ByVal s As String, ByVal a As Long)
            Dim n As Byte
            Dim b As Boolean
            n = 5678
            b = s = "x"
            b = a And 1
            o.Foo = s + s
            With New Collection
            End With
            LSet s = "abc"
        End Sub
        """;

    private static async Task<RuntimeFactsDto> RuntimeAsync()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program.ReplaceLineEndings("\r\n") + "\r\n");
        var runtime = payload.Modules.Single().Procedures.Single().Runtime;
        Assert.IsNotNull(runtime);
        return runtime;
    }

    [TestMethod]
    public async Task TheErrorsOfTheCode_Travel_WithTheirFamily()
    {
        var error = (await RuntimeAsync()).Errors.Single();

        Assert.AreEqual(6, error.ErrorId);
        Assert.IsFalse(error.IsApplicationError);
        Assert.AreEqual(6, error.Location.Range.Start.Line);
    }

    [TestMethod]
    public async Task AConversion_Travels_WithTheOperandItConverts()
    {
        var conversion = (await RuntimeAsync()).Conversions.First(fact => fact.Site == ConversionSite.Assignment && fact.Destination == "Byte");

        Assert.AreEqual("Integer", conversion.Source);
        Assert.AreEqual(6, conversion.Error!.ErrorId);
        Assert.AreEqual(InputIndex.BinaryRightOperand, conversion.Operand, "the value is the right-hand side of the assignment");
    }

    [TestMethod]
    public async Task EachFamilyOfOperations_Travels_WithItsOwnFlags()
    {
        var operations = (await RuntimeAsync()).Operations;

        var comparison = operations.Single(fact => fact.ComparisonOpSemanticFlags is not null);
        Assert.AreEqual(OptionCompare.Text, comparison.Comparison);
        Assert.AreEqual("String", comparison.EffectiveType);

        var logical = operations.Single(fact => fact.LogicalOpSemanticFlags is not null);
        Assert.IsTrue(logical.LogicalOpSemanticFlags!.Value.HasFlag(LogicalOperatorSemanticFlags.IsBitwiseSemantics));

        var addition = operations.Single(fact => fact.ArithmeticOpSemanticFlags is not null);
        Assert.IsTrue(addition.ArithmeticOpSemanticFlags!.Value.HasFlag(ArithmeticOperatorSemanticFlags.VBStringEffectiveType));
        Assert.IsNull(addition.LogicalOpSemanticFlags, "the flags of another family are not set");

        var access = operations.Single(fact => fact.MemberAccessOpSemanticFlags is not null);
        Assert.AreEqual(Tokens.MemberAccess, access.Operator);
        Assert.AreEqual("Foo", access.Member);
        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.LateBound, access.MemberAccessOpSemanticFlags);
    }

    [TestMethod]
    public async Task EachKindOfStatement_Travels_WithItsOwnFlags()
    {
        var statements = (await RuntimeAsync()).Statements;

        var with = statements.Single(fact => fact.Statement == Tokens.With);
        Assert.AreEqual(WithStatementSemanticFlags.ClassTarget, with.WithStatementFlags);
        Assert.IsTrue(with.IsValueKnown);
        Assert.IsNull(with.FixedAssignmentFlags);

        var lset = statements.Single(fact => fact.Statement == Tokens.LSet);
        Assert.AreEqual(FixedAssignmentSemanticFlags.StringTarget, lset.FixedAssignmentFlags);
    }
}

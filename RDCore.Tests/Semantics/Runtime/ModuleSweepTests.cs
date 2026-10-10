using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The language core's statements about the code of a module, found by evaluating every instruction of every procedure once, wherever the code paths
/// lead (<see cref="RDCore.Runtime.Execution.ModuleSweep"/>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class ModuleSweepTests
{
    private static async Task<RuntimeFacts> FactsOfAsync(params string[] body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var model = await ModuleWorkspace.ModelAsync([], program);

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        var procedure = model.Procedures.Single();
        Assert.IsNotNull(procedure.Runtime, "the module was loaded, so its code was evaluated");
        return procedure.Runtime;
    }

    private static ConversionFact Assignment(RuntimeFacts facts)
        => facts.Conversions.Single(conversion => conversion.Site == ConversionSite.Assignment);

    [TestMethod]
    public async Task ADoubleAssignedToALong_IsAnImplicitNarrowingConversion_ThatRoundsHalfToEven()
    {
        var facts = await FactsOfAsync("Dim d As Double", "Dim n As Long", "n = d");

        var conversion = Assignment(facts);
        Assert.AreEqual(VBDoubleType.TypeInfo, conversion.Source);
        Assert.AreEqual(VBLongType.TypeInfo, conversion.Destination);
        Assert.AreEqual(
            ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding,
            conversion.Flags & (ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Explicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy
                | ConversionSemanticFlags.BankersRounding));
        Assert.IsFalse(conversion.IsValueKnown, "what the variable holds is not known: the conversion is stated from the types alone");
        Assert.IsNull(conversion.Error, "nothing is guaranteed to fail for a Double that is not known");
        Assert.IsTrue(facts.IsFullyAnalyzed);
    }

    [TestMethod]
    public async Task ALongAssignedToADouble_IsAnImplicitWideningConversion()
    {
        var facts = await FactsOfAsync("Dim d As Double", "Dim n As Long", "d = n");

        var conversion = Assignment(facts);
        Assert.IsTrue(conversion.Flags.HasFlag(ConversionSemanticFlags.Widening));
        Assert.IsFalse(conversion.Flags.HasFlag(ConversionSemanticFlags.Narrowing));
    }

    [TestMethod]
    public async Task TheBranchNobodyTakes_IsEvaluatedLikeTheRest()
    {
        var facts = await FactsOfAsync(
            "Dim d As Double", "Dim n As Long",
            "If False Then", "    n = d", "End If");

        Assert.IsTrue(Assignment(facts).Flags.HasFlag(ConversionSemanticFlags.Narrowing));
    }

    [TestMethod]
    public async Task AValueTheCodeAssigns_IsNotTrustedByAReadThatMayFollowAnotherPath()
    {
        // n is 0 along the path that took the branch, and 1 along the other: the division is not a certain error.
        var facts = await FactsOfAsync(
            "Dim n As Long", "Dim r As Long", "Dim c As Boolean",
            "n = 1", "If c Then n = 0", "r = 1 / n");

        Assert.IsTrue(facts.IsFullyAnalyzed);
        Assert.IsFalse(facts.Operations.Any(operation => operation.Error is not null), "a value assumed does not raise a stated error");
    }

    [TestMethod]
    public async Task ALiteralTheDestinationCannotHold_IsAStatedRunTimeError()
    {
        var facts = await FactsOfAsync("Dim b As Byte", "b = 300");

        var conversion = Assignment(facts);
        Assert.IsTrue(conversion.IsValueKnown);
        Assert.IsNotNull(conversion.Error, "300 does not fit a Byte, whatever else is true of the code");
        Assert.AreEqual(6, conversion.Error.ErrorId);
    }

    [TestMethod]
    public async Task AProcedureIsEvaluatedWhetherOrNotAnythingCallsIt_AndACallIsNotMade()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Public Sub Main()\r\n    Dim n As Long\r\n    n = Half(3)\r\nEnd Sub\r\n"
            + "Private Function Half(ByVal x As Double) As Double\r\n    Dim n As Long\r\n    n = x\r\n    Half = n\r\nEnd Function\r\n"
            + "Private Sub NeverCalled(ByVal x As Double)\r\n    Dim n As Long\r\n    n = x\r\nEnd Sub\r\n";

        var model = await ModuleWorkspace.ModelAsync([], program);

        var main = model.Procedures[0].Runtime!;
        Assert.IsTrue(Assignment(main).Flags.HasFlag(ConversionSemanticFlags.Narrowing), "Half returns a Double, which Main assigns to a Long");
        foreach (var procedure in model.Procedures.Skip(1))
        {
            Assert.IsTrue(procedure.Runtime!.Conversions.Any(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.Narrowing)), "x is a Double assigned to a Long");
        }
    }
}

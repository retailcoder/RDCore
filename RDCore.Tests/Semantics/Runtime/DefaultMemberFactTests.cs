using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What the facts of the code say of a default member that the code does not name: the object it is read or assigned through stands for it
/// (<strong>MS-VBAL §5.6.2.2</strong>, <strong>§5.4.3.8</strong>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class DefaultMemberFactTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Holder", ModuleWorkspace.ClassModule("Holder",
            "Public Function Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 42", "End Function")),
        ("Slot", ModuleWorkspace.ClassModule("Slot",
            "Private mV As Long",
            "Public Property Get V() As Long", "Attribute V.VB_UserMemId = 0", "V = mV", "End Property",
            "Public Property Let V(ByVal n As Long)", "mV = n", "End Property",
            "Public Sub Twice()", "V = V * 2", "End Sub")),
    ];

    private static async Task<RuntimeFacts> FactsOfAsync(params string[] body)
    {
        var model = await ModuleWorkspace.ModelAsync(Classes,
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        return facts;
    }

    [TestMethod]
    public async Task AValueAssignedToAnObject_IsAnImplicitAccessOfItsDefaultMember()
    {
        var access = (await FactsOfAsync("Dim s As New Slot", "s = 5")).Operations.OfType<MemberAccessFact>().Single();

        Assert.AreEqual(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember, access.Flags);
        Assert.AreEqual("V", access.Member);
    }

    [TestMethod]
    public async Task AValueAssignedToAMemberTheCodeNames_IsAnExplicitAccess()
    {
        var access = (await FactsOfAsync("Dim s As New Slot", "s.V = 5")).Operations.OfType<MemberAccessFact>().Single();

        // V is the default member of Slot, and the code names it.
        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.DefaultMember, access.Flags);
    }

    [TestMethod]
    [DataRow("Debug.Print h + 1", ConversionSemanticFlags.BinaryLeftOperand, DisplayName = "the left operand")]
    [DataRow("Debug.Print 1 + h", ConversionSemanticFlags.BinaryRightOperand, DisplayName = "the right operand")]
    [DataRow("Debug.Print -h", ConversionSemanticFlags.UnaryOperand, DisplayName = "the operand of a unary operator")]
    public async Task AnObjectThatIsAnOperand_IsConvertedToTheValueOfItsDefaultMember(string statement, ConversionSemanticFlags position)
    {
        var facts = await FactsOfAsync("Dim h As New Holder", statement);

        var conversions = facts.Conversions.Where(fact => fact.Flags.HasFlag(ConversionSemanticFlags.ObjectOperand)).ToList();
        Assert.HasCount(1, conversions, string.Join("; ", facts.Conversions.Select(fact => $"{fact.Source.Name}->{fact.Destination.Name} {fact.Flags}")));
        var conversion = conversions[0];
        // the analysis assumes what a variable holds, so the value the default member returns is not known: its type is.
        Assert.IsFalse(conversion.IsValueKnown);
        Assert.AreEqual(ConversionSemanticFlags.Implicit | ConversionSemanticFlags.ObjectOperand | position, conversion.Flags);
        Assert.AreEqual("Long", conversion.Destination.Name);
        Assert.IsNull(conversion.Error);
    }

    [TestMethod]
    public async Task TheMeOfAMemberOfAClass_IsNoDeclaration()
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n", moduleName: "Slot");

        Assert.IsFalse(model.Declarations.Any(declaration => declaration.Name == "Me"), string.Join(", ", model.Declarations.Select(declaration => declaration.Name)));
        Assert.IsTrue(model.Declarations.Any(declaration => declaration is { Name: "n", Kind: DeclarationKind.Parameter }), "a parameter the code declares is one");
    }
}

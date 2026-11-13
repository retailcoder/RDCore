using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// What the facts of a <c>Let</c> statement say of a target that MS-VBAL and MS-VBA treat differently (<strong>MS-VBAL §5.4.3.8</strong>): an object whose
/// default member has no <c>Property Let</c>, and a <c>Property Get</c> or a function declared <c>Variant</c>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class LetThroughDefaultMemberFactTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Leaf", ModuleWorkspace.ClassModule("Leaf",
            "Private mV As Long",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = mV", "End Property",
            "Public Property Let Value(ByVal n As Long)", "mV = n", "End Property")),
        ("ToLeaf", ModuleWorkspace.ClassModule("ToLeaf",
            "Private mLeaf As New Leaf",
            "Public Property Get Child() As Leaf", "Attribute Child.VB_UserMemId = 0", "Set Child = mLeaf", "End Property",
            "Public Sub Poke()", "Me = 5", "End Sub")),
        ("ReadOnly", ModuleWorkspace.ClassModule("ReadOnly",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 7", "End Property")),
        ("MakesLeaf", ModuleWorkspace.ClassModule("MakesLeaf",
            "Public Function Make() As Leaf", "Attribute Make.VB_UserMemId = 0", "Set Make = New Leaf", "End Function")),
        ("Holder", ModuleWorkspace.ClassModule("Holder",
            "Private mV As Variant",
            "Public Property Get Held() As Variant", "Held = mV", "Held = 1", "End Property",
            "Public Property Get Kept() As Variant", "Kept = mV", "End Property",
            "Public Property Let Kept(ByVal v As Variant)", "mV = v", "End Property",
            "Public Function Made() As Variant", "End Function",
            "Public Function Counted() As Long", "End Function")),
    ];

    private static async Task<ModuleSemanticModel> ModelAsync(string moduleName, params string[] body)
    {
        var model = await ModuleWorkspace.ModelAsync(Classes,
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n", moduleName: moduleName);

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        return model;
    }

    private static int Flagged(ModuleSemanticModel model, ValueExpressionSemanticFlags flag)
        => model.Procedures.SelectMany(procedure => procedure.Expressions.Values).Count(fact => fact.Flags.HasFlag(flag));

    [TestMethod]
    [DataRow("Dim t As New ToLeaf", "t = 5", 1, DisplayName = "a Property Get that returns an object")]
    [DataRow("Dim r As New ReadOnly", "r = 5", 1, DisplayName = "a Property Get that returns a Long")]
    [DataRow("Dim m As New MakesLeaf", "m = 5", 1, DisplayName = "a function")]
    [DataRow("Dim l As New Leaf", "l = 5", 0, DisplayName = "a default member with a Property Let")]
    [DataRow("Dim t As New ToLeaf", "t.Child = 5", 0, DisplayName = "the default member, named")]
    [DataRow("Dim t As New ToLeaf", "t() = 5", 0, DisplayName = "an empty argument list")]
    [DataRow("Dim o As Object", "Set o = New ToLeaf: o = 5", 0, DisplayName = "an Object")]
    [DataRow("Dim v As Variant", "Set v = New ToLeaf: v = 5", 0, DisplayName = "a Variant")]
    public async Task ALetToAnObject_StatesADefaultMemberWithNoPropertyLet(string declaration, string statement, int flagged)
    {
        var model = await ModelAsync("Program", declaration, statement);

        Assert.AreEqual(flagged, Flagged(model, ValueExpressionSemanticFlags.LetToADefaultMemberWithNoPropertyLet));
    }

    [TestMethod]
    public async Task ALetToMe_StatesADefaultMemberWithNoPropertyLet()
    {
        var model = await ModelAsync("ToLeaf");

        Assert.AreEqual(1, Flagged(model, ValueExpressionSemanticFlags.LetToADefaultMemberWithNoPropertyLet));
    }

    [TestMethod]
    [DataRow("h.Held = 5", 1, DisplayName = "a Property Get with no Property Let")]
    [DataRow("h.Made = 5", 1, DisplayName = "a function")]
    [DataRow("h.Kept = 5", 0, DisplayName = "a Property Get with a Property Let")]
    public async Task ALetToAVariantGetOrFunction_IsStated(string statement, int flagged)
    {
        var model = await ModelAsync("Program", "Dim h As New Holder", statement);

        Assert.AreEqual(flagged, Flagged(model, ValueExpressionSemanticFlags.LetThroughAVariantGetOrFunction));
    }

    [TestMethod]
    public async Task TheResultOfAVariantGet_AssignedInItsOwnBody_IsNotStated()
    {
        var model = await ModelAsync("Holder");

        Assert.AreEqual(0, Flagged(model, ValueExpressionSemanticFlags.LetThroughAVariantGetOrFunction));
    }
}

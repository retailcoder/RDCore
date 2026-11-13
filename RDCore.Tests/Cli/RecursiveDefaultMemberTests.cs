using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// A default member that returns an object whose own default member gives the value, or takes the argument (<strong>MS-VBAL §5.6.2.2</strong>,
/// <strong>§5.6.2.3</strong>, <strong>§5.6.13</strong>, <strong>§5.6.14</strong>), as MS-VBA has it (recorded in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.2.2 Evaluation to a simple data value")]
public sealed class RecursiveDefaultMemberTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Holder", ModuleWorkspace.ClassModule("Holder",
            "Public Function Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 42", "End Function")),
        ("Outer", ModuleWorkspace.ClassModule("Outer",
            "Public Function Inner() As Holder", "Attribute Inner.VB_UserMemId = 0", "Set Inner = New Holder", "End Function")),
        ("OuterGet", ModuleWorkspace.ClassModule("OuterGet",
            "Public Property Get Inner() As Holder", "Attribute Inner.VB_UserMemId = 0", "Set Inner = New Holder", "End Property")),
        ("OuterObj", ModuleWorkspace.ClassModule("OuterObj",
            "Public Function Inner() As Object", "Attribute Inner.VB_UserMemId = 0", "Set Inner = New Holder", "End Function")),
        ("OuterVar", ModuleWorkspace.ClassModule("OuterVar",
            "Public Function Inner() As Variant", "Attribute Inner.VB_UserMemId = 0", "Set Inner = New Holder", "End Function")),
        ("Outer2", ModuleWorkspace.ClassModule("Outer2",
            "Public Function Middle() As Outer", "Attribute Middle.VB_UserMemId = 0", "Set Middle = New Outer", "End Function")),
        ("OuterCollection", ModuleWorkspace.ClassModule("OuterCollection",
            "Public Function Items() As Collection", "Attribute Items.VB_UserMemId = 0", "Set Items = New Collection", "End Function")),
        ("Looped", ModuleWorkspace.ClassModule("Looped",
            "Public Function Again() As Looped", "Attribute Again.VB_UserMemId = 0", "Set Again = Me", "End Function")),
        ("Bag", ModuleWorkspace.ClassModule("Bag",
            "Public Function Item(ByVal key As Variant) As String", "Attribute Item.VB_UserMemId = 0", "Item = \"<\" & key & \">\"", "End Function")),
        ("OuterBag", ModuleWorkspace.ClassModule("OuterBag",
            "Public Function Bags() As Bag", "Attribute Bags.VB_UserMemId = 0", "Set Bags = New Bag", "End Function")),
        ("OuterBagObj", ModuleWorkspace.ClassModule("OuterBagObj",
            "Public Function Bags() As Object", "Attribute Bags.VB_UserMemId = 0", "Set Bags = New Bag", "End Function")),
        ("Slot", ModuleWorkspace.ClassModule("Slot",
            "Private mV As Long",
            "Public Property Get V() As Long", "Attribute V.VB_UserMemId = 0", "V = mV", "End Property",
            "Public Property Let V(ByVal n As Long)", "mV = n", "End Property")),
        ("OuterSlot", ModuleWorkspace.ClassModule("OuterSlot",
            "Private mS As Slot",
            "Public Property Get S() As Slot", "Attribute S.VB_UserMemId = 0", "If mS Is Nothing Then Set mS = New Slot", "Set S = mS", "End Property")),
    ];

    private static string Program(string body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Dim ob As New OuterBag ;; Debug.Print ob(1)", "<1>", DisplayName = "indexed")]
    [DataRow("Dim ob As New OuterBag ;; Debug.Print ob!key", "<key>", DisplayName = "a dictionary access")]
    [DataRow("Dim ob As New OuterBag ;; Dim s As String ;; s = ob(\"a\") & ob!b ;; Debug.Print s", "<a><b>", DisplayName = "indexed and a dictionary access, assigned")]
    [DataRow("Dim ob As New OuterBag ;; Debug.Print ob()(1)", "<1>", DisplayName = "indexed after an empty argument list")]
    [DataRow("Dim ob As New OuterBagObj ;; Debug.Print ob(1)", "<1>", DisplayName = "indexed, through a default member that returns an Object")]
    [DataRow("Dim ob As New OuterBagObj ;; Debug.Print ob!key", "<key>", DisplayName = "a dictionary access, through a default member that returns an Object")]
    [DataRow("Dim ob As Object ;; Set ob = New OuterBag ;; Debug.Print ob(1) ;; Debug.Print ob!key", "<1>|<key>", DisplayName = "late-bound")]
    [DataRow("Dim o As New Outer ;; Debug.Print o()()", "42", DisplayName = "two empty argument lists")]
    [DataRow("Dim o As New OuterVar ;; Dim x As Long ;; x = o ;; Debug.Print x", "42", DisplayName = "let-coerced, through a default member that returns a Variant")]
    [DataRow("Dim o As Object ;; Set o = New Outer ;; Dim x As Long ;; x = o ;; Debug.Print x", "42", DisplayName = "let-coerced, late-bound")]
    [DataRow("Dim os As New OuterSlot ;; os() = 5 ;; Debug.Print os.S.V", "5", DisplayName = "let-assigned through an empty argument list")]
    public async Task ADefaultMemberThatReturnsAnObject_IsEvaluatedThroughTheDefaultMemberOfThatObject(string body, string printed)
        => CollectionAssert.AreEqual(printed.Split('|'), await ModuleWorkspace.RunAsync(Classes, Program(body)));

    // MS-VBAL §5.6.2.3 lets an implementation refuse the recursion statically, and MS-VBA does where a value is expected of an object whose default member
    // returns a specific class or Object.
    [TestMethod]
    [DataRow("Dim o As New Outer ;; Dim x As Long ;; x = o", DisplayName = "let-coerced")]
    [DataRow("Dim o As New Outer ;; Debug.Print o + 1", DisplayName = "an operand")]
    [DataRow("Dim o As New Outer ;; Dim s As String ;; s = o", DisplayName = "let-coerced to a String")]
    [DataRow("Dim o As New Outer ;; Debug.Print CLng(o)", DisplayName = "the argument of a conversion")]
    [DataRow("Dim o As New OuterGet ;; Dim x As Long ;; x = o", DisplayName = "through a Property Get")]
    [DataRow("Dim o As New Outer2 ;; Dim x As Long ;; x = o", DisplayName = "two default members deep")]
    [DataRow("Dim o As New OuterObj ;; Dim x As Long ;; x = o", DisplayName = "through a default member that returns an Object")]
    [DataRow("Dim o As New OuterCollection ;; Dim x As Long ;; x = o", DisplayName = "to an object whose default member takes an argument")]
    [DataRow("Dim o As New Looped ;; Dim x As Long ;; x = o", DisplayName = "a class whose default member returns an object of the same class")]
    public async Task AValueOfADefaultMemberThatReturnsAnObject_IsATypeMismatch(string body)
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program(body));

        CollectionAssert.AreEqual(new[] { (int)VBCompileErrorId.TypeMismatch }, model.CompileErrors.Select(error => error.ErrorId).ToArray());
    }
}

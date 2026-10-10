namespace RDCore.Tests.Cli;

/// <summary>
/// A declared type is a name that resolves to a type (<strong>MS-VBAL §5.6.4</strong>): a module that declares a variable, a parameter or a result of a type
/// nothing declares - a library that is not referenced, a class that is not there - is not valid, and says which name it could not resolve.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
public sealed class DeclaredTypeErrorTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Public Sub Work()", "Dim ws As Excel.Worksheet", "End Sub", "Excel.Worksheet")]
    [DataRow("Public Sub Work()", "Dim ws As Worksheet", "End Sub", "Worksheet")]
    [DataRow("Public Sub Work(ByVal ws As Excel.Range)", "End Sub", "", "Excel.Range")]
    [DataRow("Public Function Work() As Excel.Application", "End Function", "", "Excel.Application")]
    [DataRow("Public Property Get Sheet() As Worksheet", "End Property", "", "Worksheet")]
    [DataRow("Private ws As Excel.Worksheet", "", "", "Excel.Worksheet")]
    [DataRow("Public Sub Work()", "Dim all() As Excel.Worksheet", "End Sub", "Excel.Worksheet")]
    public async Task ATypeNothingDeclares_IsACompileError_AtTheNameTheModuleWrote(string first, string second, string third, string name)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(first, second, third));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], $"'{name}'");
    }

    [TestMethod]
    public async Task AQualifierThatIsNothingTheProjectCanSee_IsSaidToBeMaybeAMissingReference_AndOneThatIsNot()
    {
        var unknownQualifier = await ModuleWorkspace.LoadErrorsAsync([], Program("Public ws As Excel.Worksheet"));
        var knownQualifier = await ModuleWorkspace.LoadErrorsAsync([], Program("Public ws As Program.Worksheet"));

        StringAssert.Contains(unknownQualifier.Single(), string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeQualifierNotResolved_Verbose, "Excel.Worksheet", "Excel"));
        StringAssert.Contains(knownQualifier.Single(), string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeNotResolved_Verbose, "Program.Worksheet"),
            "Program is a module of the project: it is the type that is not there, and no reference is missing");
    }

    [TestMethod]
    public async Task ATypeOfAnotherModuleOfTheWorkspace_IsFound_WhicheverModuleIsDefinedFirst()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [("Alpha", ModuleWorkspace.ClassModule("Alpha", "Public Peer As Zulu")), ("Zulu", ModuleWorkspace.ClassModule("Zulu", "Public Peer As Alpha"))],
            Program("Public Both As Alpha", "Public Other As Zulu"));

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    [TestMethod]
    [DataRow("Dim n As Long")]
    [DataRow("Dim s As String")]
    [DataRow("Dim v As Variant")]
    [DataRow("Dim o As Object")]
    [DataRow("Dim c As Collection")]
    [DataRow("Dim c As VBA.Collection")]
    [DataRow("Dim e As ErrObject")]
    [DataRow("Dim d As Date")]
    [DataRow("Dim b() As Byte")]
    public async Task ATypeTheLibraryOfTheLanguageDeclares_IsFound(string declaration)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program("Public Sub Work()", declaration, "End Sub"));

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    private static readonly (string, string) Holder = ("Holder", ModuleWorkspace.ClassModule(
        "Holder", "Public Type TPublic", "X As Long", "End Type", "Private Type TSecret", "X As Long", "End Type",
        "Private Hidden As Holder.TSecret", "Public Open As Holder.TPublic"));

    [TestMethod]
    public async Task AModuleQualifiesTheTypesItDeclares_FromWithinAndFromWithout_WhenTheyArePublic()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [Holder], Program("Private Type TLocal", "X As Long", "End Type", "Public A As Program.TLocal", "Public B As Holder.TPublic"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    [DataRow("Public S As Holder.TSecret", "Holder.TSecret")]
    [DataRow("Public S As Holder.TMissing", "Holder.TMissing")]
    [DataRow("Public S As Program.TPublic", "Program.TPublic")]
    public async Task ATypeThatTheQualifyingModuleDoesNotDeclareForTheModule_IsNotFound(string declaration, string name)
    {
        // TSecret is Private to Holder; TMissing is nowhere; and TPublic is Holder's, which does not make it Program's.
        var errors = await ModuleWorkspace.LoadErrorsAsync([Holder], Program(declaration));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], $"'{name}'");
    }

    [TestMethod]
    public async Task ATypeDeclaredInTheModule_AndAnEnumOfAnotherModule_AreFound()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [("IShip", ModuleWorkspace.ClassModule("IShip", "Public Enum ShipOrientation", "Horizontal", "Vertical", "End Enum"))],
            Program(
                "Private Type TState", "Name As String", "End Type", "Private this As TState",
                "Public Sub Work()", "Dim o As IShip.ShipOrientation", "Dim p As ShipOrientation", "End Sub"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }
}

using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// A type of a library is named by its own name, as the standard library's are, and qualified by the name of the library: <c>Widgets.Gadget</c> is the
/// library's class and nothing else of the name (<strong>MS-VBAL §5.6.12</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
public sealed class LibraryResolutionTests
{
    private static readonly Uri Root = new("file:///c:/ws/");
    private static readonly VBStandardModuleSymbol Program = new(Root, Root, "Program");

    private static ScopeTreeSymbolResolver ResolverOf(params LibraryDescription[] libraries)
    {
        List<Symbol> symbols =
        [
            Program,
            new VBClassModuleSymbol(Root, Root, "Local"),
            .. new StdLibSymbolProvider(Root).ProvideSymbols(),
            .. new LibrarySymbolReader(Root).Read(libraries),
        ];

        return new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols));
    }

    [TestMethod]
    [DataRow("Widgets", "Gadget", true, DisplayName = "a class, qualified by its library")]
    [DataRow(null, "Gadget", true, DisplayName = "a class, by its name")]
    [DataRow("Widgets", "WidgetMode", true, DisplayName = "an enumeration, qualified")]
    [DataRow(null, "WidgetMode", true, DisplayName = "an enumeration, by its name")]
    [DataRow("Panels", "Panel", true, DisplayName = "a class of a library that depends on another")]
    [DataRow("Panels", "Gadget", false, DisplayName = "a class of the library depended on is not the other's")]
    [DataRow("VBA", "Gadget", false, DisplayName = "and not the standard library's")]
    [DataRow("Widgets", "Collection", false, DisplayName = "the standard library's classes are not a library's")]
    [DataRow("VBA", "Collection", true, DisplayName = "the standard library's own is")]
    [DataRow("Widgets", "Local", false, DisplayName = "a class of the workspace is not a library's")]
    [DataRow(null, "Local", true, DisplayName = "the workspace's own is found")]
    [DataRow("Widgets", "Missing", false, DisplayName = "a type the library does not declare")]
    [DataRow("Excel", "Gadget", false, DisplayName = "a library that is not referenced")]
    public void ATypeIsResolvedQualifiedOrNot_ToWhatTheQualifierSays(string? qualifier, string name, bool resolves)
    {
        var resolved = VBProjectSymbol.ResolveQualifiedType(ResolverOf(Widgets, Panels), qualifier, name, Program.Uri);

        Assert.AreEqual(resolves, resolved.IsResolved, $"{qualifier}.{name}");
    }

    [TestMethod]
    public void AQualifiedType_IsTheSymbolOfTheLibrary()
    {
        var gadget = VBProjectSymbol.ResolveQualifiedType(ResolverOf(Widgets), "Widgets", "Gadget", Program.Uri).Symbol;

        Assert.IsInstanceOfType<VBClassModuleSymbol>(gadget);
        Assert.AreEqual("Widgets", gadget.GetProperty(SymbolProperties.Library));
    }

    [TestMethod]
    public void TheMembersOfAnEnumeration_AreNamesOfTheProject_AsTheStandardLibrarysAre()
    {
        var constant = ResolverOf(Widgets).ResolveValue("wgOn", ScopeKind.Local, Program.Uri).Symbol;

        Assert.IsInstanceOfType<VBEnumConstMemberSymbol>(constant);
    }

    [TestMethod]
    public void TheNameOfALibrary_IsAQualifier_AndNothingElse()
    {
        var resolver = ResolverOf(Widgets);

        Assert.IsInstanceOfType<VBProjectSymbol>(resolver.ResolveQualifier("Widgets", ScopeKind.Global, Program.Uri).Symbol);
        Assert.IsNull(resolver.ResolveValue("Parts", ScopeKind.Local, Program.Uri).Symbol, "the members of a class are the members of an object, not names of the project");
    }
}

using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The declared symbols of a session, apart from anything that happens when code runs (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
[TestClass]
public sealed class SymbolTableTests
{
    private static VBStandardModuleSymbol Module(string name)
    {
        var workspace = TestUri.WorkspaceRoot();
        return new VBStandardModuleSymbol(workspace, workspace, name);
    }

    [TestMethod]
    public void ADeclaration_IsInTheTable_AndInItsScopeTree_OnceAdded()
    {
        var table = new SymbolTable();
        var module = Module("Module1");

        Assert.IsTrue(table.TryAdd(module, module.ScopeKind));

        CollectionAssert.AreEqual(new[] { module }, table.All().ToArray());
        Assert.IsTrue(table.ScopeTree.TryGetScope(module.Uri, out _));
    }

    [TestMethod]
    public void ADeclaration_IsGoneFromTheScopeTree_OnceRemoved()
    {
        var table = new SymbolTable();
        var module = Module("Module1");
        table.TryAdd(module, module.ScopeKind);
        Assert.IsTrue(table.ScopeTree.TryGetScope(module.Uri, out _), "the tree is built, and so memoized, before the change");

        Assert.IsTrue(table.Remove(module, module.ScopeKind));

        Assert.IsEmpty(table.All());
        Assert.IsFalse(table.ScopeTree.TryGetScope(module.Uri, out _));
    }

    [TestMethod]
    public void ItsNames_ResolveAgainstTheTableAsItIs_WhenTheNameIsLookedUp()
    {
        // the resolver keeps the one it made for the tree, and makes another when a change builds another tree.
        var table = new SymbolTable();
        var names = table.Names;
        var first = Module("Module1");
        table.TryAdd(first, first.ScopeKind);
        Assert.AreSame(first, names.ResolveValue("Module1", ScopeKind.Global, first.Uri).Symbol);
        Assert.IsTrue(names.ResolveValue("Module2", ScopeKind.Global, first.Uri).IsUnbound);

        var second = Module("Module2");
        table.TryAdd(second, second.ScopeKind);

        Assert.AreSame(second, names.ResolveValue("Module2", ScopeKind.Global, first.Uri).Symbol);
        table.Remove(first, first.ScopeKind);
        Assert.IsTrue(names.ResolveValue("Module1", ScopeKind.Global, second.Uri).IsUnbound);
    }

    [TestMethod]
    public void TheFirstDefinitionOfADeclaration_Wins()
    {
        var table = new SymbolTable();
        var module = Module("Module1");

        Assert.IsTrue(table.TryAdd(module, module.ScopeKind));
        Assert.IsFalse(table.TryAdd(module with { }, module.ScopeKind));

        Assert.HasCount(1, table.All());
    }

    [TestMethod]
    public void RemovingADeclarationThatIsNotThere_IsNotAnError_ButItIsNotRemoved()
    {
        var module = Module("Module1");

        Assert.IsFalse(new SymbolTable().Remove(module, module.ScopeKind));
    }

    [TestMethod]
    public void ANewerDefinitionOfADeclaration_ReplacesItInPlace()
    {
        var table = new SymbolTable();
        var module = Module("Module1");
        table.TryAdd(module, module.ScopeKind);
        var newer = module with { };

        Assert.IsTrue(table.TryGet(newer, newer.ScopeKind, out var existing));
        Assert.AreSame(module, existing);

        table.Replace(newer, newer.ScopeKind);

        Assert.AreSame(newer, table.All().Single());
    }

    [TestMethod]
    public void ItsVersion_Changes_WithEveryChangeMadeToIt_AndNotWithAChangeNotMade()
    {
        var table = new SymbolTable();
        var module = Module("Module1");
        var versions = new List<int> { table.Version };

        Assert.IsTrue(table.TryAdd(module, module.ScopeKind));
        versions.Add(table.Version);
        Assert.IsFalse(table.TryAdd(module with { }, module.ScopeKind));
        Assert.AreEqual(versions[^1], table.Version, "a definition the table did not take changes nothing");
        table.Replace(module with { }, module.ScopeKind);
        versions.Add(table.Version);
        Assert.IsTrue(table.Remove(module, module.ScopeKind));
        versions.Add(table.Version);
        Assert.IsFalse(table.Remove(module, module.ScopeKind));
        Assert.AreEqual(versions[^1], table.Version, "a declaration the table did not hold changes nothing");

        Assert.HasCount(versions.Count, versions.Distinct());
    }

    [TestMethod]
    public void TwoTables_DoNotShareTheirDeclarations()
    {
        var first = new SymbolTable();
        var second = new SymbolTable();
        var module = Module("Module1");

        first.TryAdd(module, module.ScopeKind);

        Assert.IsEmpty(second.All());
    }

    private static VBUserDefinedTypeMemberSymbol PublicType(VBModuleSymbol module, string name)
        => new(module.WorkspaceRoot, module.Uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

    private static VBModuleFieldVariableMemberSymbol PublicField(VBModuleSymbol module, string name)
        => new(module.WorkspaceRoot, module.Uri, name, ScopeKind.Module, VBLongType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

    // the tree its type names resolve against holds what a type name can name: a variable defined leaves it as it is, and a type defined builds it again.
    [TestMethod]
    public void ItsTypeScopeTree_IsBuiltAgain_WhenATypeIsDefined_AndNotWhenAVariableIs()
    {
        var table = new SymbolTable();
        var module = Module("Module1");
        table.TryAdd(module, module.ScopeKind);
        var (tree, types) = (table.ScopeTree, table.TypeScopeTree);

        Assert.IsTrue(table.TryAdd(PublicField(module, "Total"), ScopeKind.Module));

        Assert.AreNotSame(tree, table.ScopeTree, "the variable is declared in the table's whole tree, built again");
        Assert.AreSame(types, table.TypeScopeTree);
        var point = PublicType(module, "TPoint");
        Assert.IsTrue(table.TryAdd(point, point.ScopeKind));
        Assert.AreNotSame(types, table.TypeScopeTree);
        Assert.AreSame(point, table.TypeNames.ResolveType("TPoint", ScopeKind.Module, module.Uri).Symbol);
    }

    // a type name resolves against the type scope tree to what it resolves to against the whole tree, from each module, qualified by a module or not, and
    // whatever else is declared by the name: a variable named after a class is no type, and the class is what the name names.
    [TestMethod]
    public void ItsTypeNames_ResolveAsItsNames_FromEveryModule()
    {
        var table = new SymbolTable();
        var (shapes, other) = (Module("Shapes"), Module("Other"));
        var widget = new VBClassModuleSymbol(shapes.WorkspaceRoot, shapes.WorkspaceRoot, "Widget");
        Symbol[] declared = [shapes, other, widget, PublicType(shapes, "TPoint"), PublicField(shapes, "Widget"), PublicField(other, "TPoint")];
        foreach (var symbol in declared)
        {
            Assert.IsTrue(table.TryAdd(symbol, symbol.ScopeKind));
        }

        Assert.IsInstanceOfType<VBModuleFieldVariableMemberSymbol>(table.Names.ResolveValue("Widget", ScopeKind.Module, shapes.Uri).Symbol,
            "a variable of the module is named after the class");
        foreach (var from in new[] { shapes, other })
        {
            foreach (var name in new[] { "TPoint", "Widget", "Shapes", "Missing" })
            {
                var inWhole = table.Names.ResolveType(name, ScopeKind.Module, from.Uri);
                var inTypes = table.TypeNames.ResolveType(name, ScopeKind.Module, from.Uri);
                Assert.AreEqual(inWhole.IsResolved, inTypes.IsResolved, $"{name} from {from.Name}");
                Assert.AreSame(inWhole.Symbol, inTypes.Symbol, $"{name} from {from.Name}");
            }

            var (qualifiedInWhole, qualifiedInTypes) = (
                VBProjectSymbol.ResolveQualifiedType(table.Names, "Shapes", "TPoint", from.Uri, ProjectVisibility.Instance, from.WorkspaceRoot),
                VBProjectSymbol.ResolveQualifiedType(table.TypeNames, "Shapes", "TPoint", from.Uri, ProjectVisibility.Instance, from.WorkspaceRoot));
            Assert.IsTrue(qualifiedInWhole.IsResolved, $"Shapes.TPoint from {from.Name}");
            Assert.AreSame(qualifiedInWhole.Symbol, qualifiedInTypes.Symbol, $"Shapes.TPoint from {from.Name}");
        }

        Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(table.TypeNames.ResolveType("TPoint", ScopeKind.Module, other.Uri).Symbol,
            "a type of another module is found past a variable of its name");
    }
}

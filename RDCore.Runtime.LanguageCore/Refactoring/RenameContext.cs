using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Refactoring;

// Designer: what the module's designer declares, when it is a form (ModuleParseResult.Designer).
public sealed record class RenameModule(Uri Module, ModuleNode Syntax, string Source, RDCore.SDK.Workspace.Forms.FormDescriptor? Designer = null);

public sealed record class RenameContext(
    ISymbolResolver Resolver, ScopeTree ScopeTree, IReadOnlyList<RenameModule> Modules, StaticEnvironment? Environment = null,
    IReadOnlyList<IPossibleClassesSource>? ClassSources = null);

using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RDCore.Runtime.Support.Host;

// what the machines of one composed program have in common, found once for all of them. Each machine defines the same definitions, in the same order, in a
// session composed the same way: the parse of a module and its directives are syntax, which nothing changes, and the branches the directives make dead are
// decided by the project's constants, which are the same in every machine. Once a machine has loaded the program's code and found nothing wrong with it, so
// would every other, which then loads the code to run and for nothing else (ModuleLoader.Checks). A definition is known by its own object, the image's.
internal sealed class CompositionReplay
{
    private readonly ConcurrentDictionary<DefineSymbolsParams, Lazy<ModuleParseResult?>> _parses = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<DefineSymbolsParams, Lazy<ImmutableArray<SyntaxNode>>> _trivia = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<DefineSymbolsParams, ImmutableArray<SourceLocation>> _deadBranches = new(ReferenceEqualityComparer.Instance);
    private volatile bool _checked;

    // whether a machine of the program has loaded all of its code, and found nothing wrong with it.
    public bool IsChecked => _checked;

    public void Checked() => _checked = true;

    public ModuleParseResult? ParseOf(DefineSymbolsParams definition)
        => _parses.GetOrAdd(definition, each => new(() => PlatformJson.Deserialize<ModuleParseResult>(each.ParseResultJson))).Value;

    public ImmutableArray<SyntaxNode> TriviaOf(DefineSymbolsParams definition)
        => _trivia.GetOrAdd(definition, each => new(() => PlatformJson.Deserialize<ImmutableArray<SyntaxNode>>(each.PrecompilerTriviaJson))).Value;

    // the dead branches of the definition's directives, as the first machine to define it evaluated them.
    public ImmutableArray<SourceLocation> DeadBranchesOf(
        DefineSymbolsParams definition, Func<ImmutableArray<SyntaxNode>, ImmutableArray<SourceLocation>> evaluate)
        =>_deadBranches.GetOrAdd(definition, each => evaluate(TriviaOf(each)));
}

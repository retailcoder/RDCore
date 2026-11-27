using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Annotations;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// The annotations of a module that no installed extension declares (<c>RDC00419</c>), and the core annotations that their declarations do not allow where
/// they are written: <c>RDC00404</c> at a place they do not annotate, <c>RDC00420</c> written twice for one target, <c>RDC00421</c> in a kind of module
/// they do not apply to, <c>RDC00422</c> and <c>RDC00423</c> with too few or too many arguments.
/// </summary>
/// <remarks>
/// An annotation binds to the module when it is in its declarations section or above a block's header, to a procedure or a variable when it is on the lines
/// right above it (<see cref="Annotations"/>), and to the first of those its declaration allows. An extension's annotation is the extension's to check: only
/// whether one is declared at all is this analyzer's, against the annotations of the extensions the platform loads (<paramref name="installed"/>).
/// The kind of a module that is not a block's is the host's to say: with no semantics, <c>RDC00421</c> is not reported.
/// </remarks>
/// <param name="installed">The annotations of the core and of every extension the platform loads.</param>
internal sealed class AnnotationDeclarationsAnalyzer(AnnotationCatalogue installed) : IModuleAnalyzer
{
    /// <summary>
    /// The core annotations, as the core extension's assembly declares them.
    /// </summary>
    public static AnnotationSet CoreSet { get; }
        = ExtensionAnnotationsAttribute.Read(typeof(CoreAnnotations).Assembly) ?? throw new InvalidOperationException("no core annotations");

    /// <summary>
    /// The annotations of the core and of each of the extensions, as their manifests carry them.
    /// </summary>
    /// <param name="extensions">The extensions the platform loads (<see cref="IExtensionsProvider.Discover"/>).</param>
    public static AnnotationCatalogue Installed(IEnumerable<ExtensionInfo> extensions)
        => new([CoreSet, .. extensions.Select(extension => extension.AnnotationSet)]);

    private static readonly AnnotationCatalogue Catalogue = new([CoreSet]);

    public IReadOnlyList<RDCoreDiagnosticCode> Codes =>
    [
        RDCoreDiagnosticId.UnrecognizedAnnotation.ToCode(),
        RDCoreDiagnosticId.InvalidAnnotation.ToCode(),
        RDCoreDiagnosticId.DuplicatedAnnotation.ToCode(),
        RDCoreDiagnosticId.AnnotationInIncompatibleComponentType.ToCode(),
        RDCoreDiagnosticId.MissingAnnotationArgument.ToCode(),
        RDCoreDiagnosticId.SuperfluousAnnotationArgument.ToCode(),
    ];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        // the file's module, and each of its blocks' with the kind its header says
        var document = new RDCoreDocument(context.Document, context.ParseResult);
        return document.ModuleUris.SelectMany(uri => document.ParseOf(uri) is { } module
            ? Analyze(module, context.ParseResult.BlockModules.FirstOrDefault(block => block.Uri == uri) is { } block
                ? block.Kind == ModuleType.ClassModule ? AnnotationModuleKinds.Class : AnnotationModuleKinds.Standard
                : KindOf(context))
            : []);
    }

    private IEnumerable<AnalyzerFinding> Analyze(ModuleParseResult parse, AnnotationModuleKinds? kind)
    {
        if (parse.SyntaxTree is not { } module)
        {
            yield break;
        }

        var ofModule = parse.GetModuleAnnotations().ToHashSet();
        var above = new Dictionary<AnnotationTriviaNode, (AnnotationTargets Target, SyntaxNode Declaration)>();
        foreach (var declaration in module.Children)
        {
            var target = declaration switch
            {
                MemberDeclarationNode { MemberKind: MemberKind.Procedure } => AnnotationTargets.Member,
                VariableDeclarationNode => AnnotationTargets.Variable,
                _ => AnnotationTargets.None,
            };
            if (target != AnnotationTargets.None)
            {
                foreach (var annotation in parse.GetAnnotations(declaration))
                {
                    above[annotation] = (target, declaration);
                }
            }
        }

        var bound = new List<(AnnotationTriviaNode Annotation, AnnotationDeclaration Declaration, object Target)>();
        foreach (var annotation in parse.Trivia.OfType<AnnotationTriviaNode>())
        {
            if (Catalogue.Find(annotation.Name) is not { } declared)
            {
                if (installed.Find(annotation.Name) is null)
                {
                    yield return Finding(RDCoreDiagnosticId.UnrecognizedAnnotation, annotation.SourceLocation.Range,
                        AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnrecognizedAnnotation_Message, annotation.Name));
                }

                continue;
            }

            var places = (ofModule.Contains(annotation) ? AnnotationTargets.Module : AnnotationTargets.None)
                | (above.TryGetValue(annotation, out var below) ? below.Target : AnnotationTargets.None);
            if ((declared.Targets & places) == AnnotationTargets.None)
            {
                yield return Finding(RDCoreDiagnosticId.InvalidAnnotation, annotation.SourceLocation.Range,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.InvalidAnnotation_Message, annotation.Name, Words(declared.Targets)));
                continue;
            }

            if (kind is { } moduleKind && (declared.ModuleKinds & moduleKind) == AnnotationModuleKinds.None)
            {
                yield return Finding(RDCoreDiagnosticId.AnnotationInIncompatibleComponentType, annotation.SourceLocation.Range,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.AnnotationInIncompatibleComponentType_Message, annotation.Name, Word(moduleKind)));
            }

            foreach (var finding in Arguments(annotation, declared))
            {
                yield return finding;
            }

            // a declaration's own annotation before the module's: on the last line of the declarations section, right above the first procedure, it is both
            bound.Add((annotation, declared, (declared.Targets & below.Target) != AnnotationTargets.None ? below.Declaration : module));
        }

        var repeatedOnes = bound.Where(item => !item.Declaration.Repeats).GroupBy(item => (item.Declaration, item.Target));
        foreach (var repeated in repeatedOnes.Where(group => group.Count() > 1))
        {
            foreach (var (annotation, _, _) in repeated.Skip(1))
            {
                yield return Finding(RDCoreDiagnosticId.DuplicatedAnnotation, annotation.SourceLocation.Range,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.DuplicatedAnnotation_Message, annotation.Name));
            }
        }
    }

    private static IEnumerable<AnalyzerFinding> Arguments(AnnotationTriviaNode annotation, AnnotationDeclaration declared)
    {
        var written = annotation.Children.Length;
        if (written < declared.MinArguments)
        {
            yield return Finding(RDCoreDiagnosticId.MissingAnnotationArgument, annotation.SourceLocation.Range,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.MissingAnnotationArgument_Message, annotation.Name, declared.Parameters[written].Name));
        }
        else if (declared.MaxArguments is { } most && written > most)
        {
            var range = new SourceRange(annotation.Children[most].SourceLocation.Range.Start, annotation.Children[^1].SourceLocation.Range.End);
            yield return Finding(RDCoreDiagnosticId.SuperfluousAnnotationArgument, range, most == 0
                ? AnalyzerMessages.Format(RDCoreDiagnosticsResources.SuperfluousAnnotationArgument_NoParameter, annotation.Name)
                : AnalyzerMessages.Format(RDCoreDiagnosticsResources.SuperfluousAnnotationArgument_Message, annotation.Name, declared.Parameters[^1].Name));
        }
    }

    private static AnalyzerFinding Finding(RDCoreDiagnosticId id, SourceRange range, string message)
        => new(id.ToCode(), range, DiagnosticSeverity.Warning, message);

    // a document module and a form are the host's and the designer's to say; a class module, the semantics'
    private static AnnotationModuleKinds? KindOf(ModuleAnalysisContext context) => context.Semantics switch
    {
        null => null,
        { Document: not null } => AnnotationModuleKinds.Document,
        _ when context.ParseResult.Designer is not null => AnnotationModuleKinds.Form,
        { Class: not null } => AnnotationModuleKinds.Class,
        _ => AnnotationModuleKinds.Standard,
    };

    private static string Words(AnnotationTargets targets)
        => string.Join(RDCoreDiagnosticsResources.AnnotationTarget_Or, new[]
        {
            (AnnotationTargets.Module, RDCoreDiagnosticsResources.AnnotationTarget_Module),
            (AnnotationTargets.Member, RDCoreDiagnosticsResources.AnnotationTarget_Member),
            (AnnotationTargets.Variable, RDCoreDiagnosticsResources.AnnotationTarget_Variable),
            (AnnotationTargets.Line, RDCoreDiagnosticsResources.AnnotationTarget_Line),
        }.Where(item => targets.HasFlag(item.Item1)).Select(item => item.Item2));

    private static string Word(AnnotationModuleKinds kind) => kind switch
    {
        AnnotationModuleKinds.Class => RDCoreDiagnosticsResources.AnnotationModuleKind_Class,
        AnnotationModuleKinds.Form => RDCoreDiagnosticsResources.AnnotationModuleKind_Form,
        AnnotationModuleKinds.Document => RDCoreDiagnosticsResources.AnnotationModuleKind_Document,
        _ => RDCoreDiagnosticsResources.AnnotationModuleKind_Standard,
    };
}

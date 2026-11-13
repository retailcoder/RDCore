using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The code actions of a range of a document: the renames that fix its naming diagnostics, and a rename of each name it declares.
/// </summary>
[TestClass]
public sealed class CodeActionHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-codeaction-ws");

    private const string Program = "10 Dim strName As String\r\n20 Dim x As Long\r\n30 strName = \"a\"\r\n40 Debug.Print strName, x\r\n";

    // a module the shell has loaded: its statements can be extracted, which the shell's own program's cannot.
    private const string Module = "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\n    Dim strName As String\r\n    Dim x As Long\r\n    strName = \"a\"\r\n    Debug.Print strName, x\r\nEnd Sub\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    // what the client said it can do, as it initialized; none, a client that takes neither documentChanges nor change annotations.
    private ClientCapabilities? _client;

    public CodeActionHandlerTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var program = new WorkspaceDocument("Main.rdc", Root, Program, version: 2);
        var module = new WorkspaceDocument("Module1.bas", Root, Module, version: 1);
        _documents.GetAllDocuments().Returns([program, module]);
        var uri = program.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = program; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(program))));
        var moduleUri = module.Id.Uri.ToUri();
        _documents.TryGetDocument(moduleUri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = module; return true; });
        _parsing.ParseDocumentAsync(moduleUri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(moduleUri, ParsingClientService.ModuleSourceOf(module))));

        // an extraction offered with its edit is verified on its text, parsed again.
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
    }

    private CodeActionHandler Sut()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        var handler = new CodeActionHandler(rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(rename, _parsing, options),
            Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>(), new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        if (_client is not null)
        {
            ((IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)handler).GetRegistrationOptions(new CodeActionCapability(), _client);
        }

        return handler;
    }

    private static DocumentUri Document => DocumentUri.From(Path.Combine(Root, "Main.rdc"));

    private static Diagnostic Finding(Range range, params string[] suggestions) => new()
    {
        Range = range,
        Code = "RDC01002",
        Source = "RDCore",
        Message = "Hungarian",
        Data = new DiagnosticFixes { Rename = new RenameFix { Suggestions = suggestions } }.ToData(),
    };

    private Task<List<CodeAction>> ActionsAsync(Range range, CodeActionKind[]? only = null, params Diagnostic[] diagnostics)
        => ActionsAsync(Document, range, only, diagnostics);

    private async Task<List<CodeAction>> ActionsAsync(DocumentUri document, Range range, CodeActionKind[]? only, params Diagnostic[] diagnostics)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var answer = await Sut().Handle(new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier(document),
                Range = range,
                Context = new CodeActionContext
                {
                    Diagnostics = new Container<Diagnostic>(diagnostics),
                    Only = only is null ? null : new Container<CodeActionKind>(only),
                },
            }, CancellationToken.None);
            return [.. answer!.Select(item => item.CodeAction!)];
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public async Task ADiagnosticFixedByARename_IsOfferedTheRenameToEachSuggestion_AndToANameTheUserChooses()
    {
        var diagnostic = Finding(new Range(0, 7, 0, 14), "Name");

        var actions = await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], diagnostic);

        Assert.HasCount(2, actions);
        Assert.AreEqual("Rename 'strName' to 'Name'", actions[0].Title);
        Assert.IsTrue(actions[0].IsPreferred);
        // a client that has not said it takes documentChanges is sent changes (RefactoringWireTests has both).
        var edits = actions[0].Edit!.Changes!.Single().Value.Select(edit => edit.Range).ToArray();
        CollectionAssert.AreEqual(new[] { new Range(0, 7, 0, 14), new Range(2, 3, 2, 10), new Range(3, 15, 3, 22) }, edits);

        Assert.AreEqual("Rename 'strName'…", actions[1].Title);
        Assert.AreEqual(RefactoringCommands.Rename, actions[1].Command!.Name);
        Assert.AreEqual(Document.ToString(), actions[1].Command!.Arguments![0].ToString());
        Assert.AreEqual(diagnostic, actions[1].Diagnostics!.Single());
    }

    // a finding of RDC00115 in the shell's program, fixed by two edits of it: the declaration of a local, or the parameter passed ByRef.
    private static Diagnostic FixedByEdits(int? version, bool firstChangesBehavior = false) => new()
    {
        Range = new Range(2, 3, 2, 10),
        Code = "RDC00115",
        Source = "RDCore",
        Message = "Implicit result local",
        Data = new DiagnosticFixes
        {
            Edits =
            [
                new EditFix
                {
                    Title = "Declare a local",
                    Edits = [new TextEdit { Range = new Range(1, 0, 1, 0), NewText = "15 Dim result As Long\r\n" }, new TextEdit { Range = new Range(2, 3, 2, 10), NewText = "result" }],
                    ChangesBehavior = firstChangesBehavior,
                    Version = version,
                },
                new EditFix { Title = "Pass it ByRef", Edits = [new TextEdit { Range = new Range(2, 3, 2, 10), NewText = "x" }], ChangesBehavior = true, Version = version },
            ],
        }.ToData(),
    };

    [TestMethod]
    public async Task ADiagnosticFixedByEdits_IsOfferedEachFix_BestFirst_AsTheAnalyzerMadeIt()
    {
        var diagnostic = FixedByEdits(version: 2);

        var actions = await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], diagnostic);

        CollectionAssert.AreEqual(new[] { "Declare a local", "Pass it ByRef" }, actions.Select(action => action.Title).ToArray());
        CollectionAssert.AreEqual(new bool[] { true, false }, actions.Select(action => action.IsPreferred).ToArray(), "only the first is preferred");
        Assert.IsTrue(actions.All(action => action.Diagnostics!.Single() == diagnostic));
        // a client that has not said it takes documentChanges is sent changes.
        var (document, changes) = actions[0].Edit!.Changes!.Single();
        Assert.AreEqual(Document, document);
        CollectionAssert.AreEqual(new[] { (new Range(1, 0, 1, 0), "15 Dim result As Long\r\n"), (new Range(2, 3, 2, 10), "result") },
            changes.Select(edit => (edit.Range, edit.NewText)).ToArray());
    }

    [TestMethod]
    public async Task AFirstFixThatChangesWhatTheCodeDoes_IsNotPreferred()
    {
        var actions = await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], FixedByEdits(version: 2, firstChangesBehavior: true));

        CollectionAssert.AreEqual(new bool[] { false, false }, actions.Select(action => action.IsPreferred).ToArray());
    }

    [TestMethod]
    [DataRow(1, DisplayName = "made of an earlier version")]
    [DataRow(null, DisplayName = "of a version that is not known")]
    public async Task EditFixesOfAnotherVersionOfTheDocument_AreNotOffered(int? version)
        => Assert.IsEmpty(await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], FixedByEdits(version)));

    [TestMethod]
    public async Task EditFixes_AreSentWithTheVersionTheyAreOf_ToAClientThatTakesIt()
    {
        _client = new ClientCapabilities { Workspace = new WorkspaceClientCapabilities { WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = true } } };

        var action = (await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], FixedByEdits(version: 2)))[0];

        var edit = action.Edit!.DocumentChanges!.Single().TextDocumentEdit!;
        Assert.AreEqual(Document, edit.TextDocument.Uri);
        Assert.AreEqual(2, edit.TextDocument.Version);
    }

    [TestMethod]
    public async Task ASuggestionThatCannotBeMade_IsNotOffered()
    {
        var actions = await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], Finding(new Range(0, 7, 0, 14), "x"));

        CollectionAssert.AreEqual(new[] { "Rename 'strName'…" }, actions.Select(action => action.Title).ToArray(), "x is declared already");
    }

    [TestMethod]
    public async Task EachNameARangeDeclares_IsOfferedARename()
    {
        var actions = await ActionsAsync(new Range(0, 0, 3, 0));

        CollectionAssert.AreEqual(new[] { "Rename 'strName'…", "Rename 'x'…" }, actions.Select(action => action.Title).ToArray());
        Assert.IsTrue(actions.All(action => action.Kind == CodeActionHandler.RefactorRename));
    }

    [TestMethod]
    public async Task AtAPosition_TheNameThere_IsOfferedARename()
    {
        var actions = await ActionsAsync(new Range(3, 24, 3, 24));

        CollectionAssert.AreEqual(new[] { "Rename 'x'…" }, actions.Select(action => action.Title).ToArray());
    }

    [TestMethod]
    public async Task AClientThatAsksForQuickFixesOnly_IsNotOfferedTheRefactorings()
        => Assert.IsEmpty(await ActionsAsync(new Range(0, 0, 3, 0), [CodeActionKind.QuickFix]));

    [TestMethod]
    public async Task ADiagnosticFixedByARefactoring_IsOfferedTheRefactoring_ForItsRange_Once()
    {
        var document = DocumentUri.From(Path.Combine(Root, "Module1.bas"));
        var range = new Range(2, 0, 6, 0);
        var diagnostic = new Diagnostic
        {
            Range = range,
            Code = "RDC00000",
            Source = "RDCore",
            Message = "Too long",
            Data = new DiagnosticFixes { Refactoring = new RefactoringFix { Kind = RefactoringKinds.ExtractMethod } }.ToData(),
        };

        var atAPosition = await ActionsAsync(document, new Range(1, 0, 1, 0), null, diagnostic);
        var forTheRange = await ActionsAsync(document, range, null, diagnostic);

        var extraction = atAPosition.Single(action => action.Kind == CodeActionHandler.RefactorExtractMethod);
        Assert.AreEqual(diagnostic, extraction.Diagnostics!.Single());
        Assert.IsNotNull(extraction.Edit, "a client with no dialog is sent the edit");
        Assert.ContainsSingle(forTheRange.Where(action => action.Kind == CodeActionHandler.RefactorExtractMethod), "the selection is the range of the diagnostic");
    }

    [TestMethod]
    public async Task ARefactoringFixOfAKindThisServerDoesNotMake_IsNotOffered()
    {
        var document = DocumentUri.From(Path.Combine(Root, "Module1.bas"));
        var diagnostic = new Diagnostic
        {
            Range = new Range(2, 0, 6, 0),
            Message = "Unsorted",
            Data = new DiagnosticFixes { Refactoring = new RefactoringFix { Kind = "refactor.rewrite.unknown" } }.ToData(),
        };

        Assert.IsEmpty(await ActionsAsync(document, new Range(1, 0, 1, 0), [CodeActionKind.Refactor], diagnostic));
    }
}

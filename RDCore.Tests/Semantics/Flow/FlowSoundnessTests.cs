using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// The cases a review of the flow analysis found it wrong on: each asserts the sound answer, which a missing edge or a missing definition loses.
/// </summary>
[TestClass]
public sealed class FlowSoundnessTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private const string Others = """
        Private Type Point
            X As Long
            Y As Long
        End Type

        Private Type Outer
            Inner As Point
            Arr(1 To 3) As Long
        End Type

        Private field As Long

        Private Sub Bump(ByRef v As Long)
            v = v + 1
        End Sub

        Private Sub TakesByVal(ByVal v As Long)
        End Sub

        Private Sub ShowField()
            Debug.Print field
        End Sub

        Private Function G() As Long
            G = 1
        End Function

        Private Function F(ByRef v As Long) As Long
            v = v + 1
            F = v
        End Function
        """;

    private sealed record class Analyzed(InstructionList Instructions, ProcedureSemanticModel Model, DefinitionsAndUses Accesses, ControlFlowGraph Graph, int FirstBodyLine)
    {
        public LiveVariables Live => LiveVariables.Of(Graph, Accesses);

        public ReachingDefinitions Reaching => ReachingDefinitions.Of(Graph, Accesses);

        public ErrorHandlingStates States => ErrorHandlingStates.Of(Graph);

        public SemanticId Variable(string name)
            => Accesses.Variables.Single(variable => variable.Uri.Fragment.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase));

        public IReadOnlySet<int> Lines(int first, int last)
            => Instructions.Items
                .Where(instruction => instruction.Location is { } location
                    && location.Range.Start.Line - FirstBodyLine >= first && location.Range.Start.Line - FirstBodyLine <= last)
                .Select(instruction => instruction.Offset)
                .ToHashSet();

        public int Line(int line) => Lines(line, line).Min();

        public bool Defines(int line, SemanticId variable) => Lines(line, line).Any(offset => Accesses.DefinitionsAt(offset).Any(definition => definition.Variable == variable));
    }

    private static Analyzed Analyze(string body, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", string others = Others, ModuleType moduleType = ModuleType.StdModule)
    {
        var moduleLevel = others.ReplaceLineEndings("\r\n");
        var attributes = moduleType is ModuleType.ClassModule
            ? "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Main\"\r\n"
            : "Attribute VB_Name = \"Main\"\r\n";
        var prefix = $"{attributes}Option Explicit\r\n{moduleLevel}\r\n{header}\r\n";
        var extension = moduleType is ModuleType.ClassModule ? "cls" : "bas";
        var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/Main.{extension}"), $"{prefix}{body.ReplaceLineEndings("\r\n")}\r\n{end}\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(ModuleUri("Main"), moduleType, parse)], new IntrinsicSymbolResolver());
        Assert.IsTrue(composition.ScopeTree.TryGetScope(ModuleUri("Main.Run"), out var scope));
        var run = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Run");
        var block = new StatementBlock([.. run.Children]);
        var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(ModuleUri("Main.Run")), block, kind: run.MemberKind,
            context: new StaticEvaluationContext(composition.Resolver, scope));
        Assert.IsTrue(model.IsFullyAnalyzed, string.Join("; ", model.CompileErrors.Select(error => error.ToString())));

        var instructions = InstructionListLowering.Lower(block, default, run.MemberKind).InstructionList;
        var graph = ControlFlowGraph.Of(instructions);
        return new Analyzed(instructions, model, DefinitionsAndUses.Of(instructions, model, composition.ScopeTree), graph, prefix.Split("\r\n").Length - 1);
    }

    // ---------------- soundness: errors ----------------

    [TestMethod]
    public void S01_AnErrorRaisedToTheCaller_ExposesAByRefParameter_CallThatMayFail()
    {
        // Run's caller, with a handler, sees r = 1 when G() fails.
        var analyzed = Analyze("r = 1\nr = G()", header: "Public Sub Run(ByRef r As Long)");
        Assert.Contains(analyzed.Variable("r"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)));
    }

    [TestMethod]
    public void S02_AnErrorStatementRaisedToTheCaller_ExposesAByRefParameter()
    {
        var analyzed = Analyze("r = 1\nError 5", header: "Public Sub Run(ByRef r As Long)");
        Assert.Contains(analyzed.Variable("r"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)));
    }

    [TestMethod]
    public void S03_AnErrorStatementUnderResumeNext_ContinuesAtTheNextStatement()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error Resume Next\nx = 1\nError 5\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(2, 2)));
    }

    [TestMethod]
    public void S04_OnErrorGoToMinusOne_KeepsTheHandler_Liveness()
    {
        // measured (oracle s53 A05): inside handler H, On Error GoTo -1 then a raise goes to H again.
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo H\nx = G()\nExit Sub\nH:\nDebug.Print x\nOn Error GoTo -1\nx = 1\nx = G()\nExit Sub");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(7, 7)), "x = G() may raise into H, which reads x");
    }

    [TestMethod]
    public void S05_OnErrorGoToMinusOne_KeepsTheHandler_States()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo H\nx = G()\nExit Sub\nH:\nDebug.Print x\nOn Error GoTo -1\nx = 1\nx = G()\nExit Sub");
        Assert.IsNotEmpty(analyzed.States.HandlersAt(analyzed.Line(8)), "H is still the handler");
    }

    [TestMethod]
    public void S06_StatementsReachedOnlyByResumeNext_AreProtectedByTheHandler()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo H\nError 5\nx = 1\nx = G()\nExit Sub\nH:\nDebug.Print x\nResume Next");
        Assert.IsNotEmpty(analyzed.States.HandlersAt(analyzed.Line(4)), "the states know H is in effect");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)), "x = G() may raise into H, which reads x");
    }

    [TestMethod]
    public void S07_AnErrorInsideTheHandler_ExposesAByRefParameterToTheCaller()
    {
        var analyzed = Analyze("On Error GoTo H\nr = G()\nExit Sub\nH:\nr = 1\nr = G()", header: "Public Sub Run(ByRef r As Long)");
        Assert.Contains(analyzed.Variable("r"), analyzed.Live.LiveAfter(analyzed.Lines(4, 4)));
    }

    // ---------------- soundness: definitions ----------------

    [TestMethod]
    public void S10_AnArgumentOfRaiseEvent_IsADefinition()
    {
        const string cls = """
            Public Event Changing(ByRef Cancel As Boolean)
            """;
        var analyzed = Analyze("Dim cancel As Boolean\nRaiseEvent Changing(cancel)\nIf cancel Then Exit Sub", others: cls, moduleType: ModuleType.ClassModule);
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("cancel")), "a handler of the event may write it");
    }

    [TestMethod]
    public void S11_AMemberOfAnElementOfAnArrayOfTypes_IsAPartialDefinition()
    {
        var analyzed = Analyze("Dim a(1 To 3) As Point\na(1).X = 5\nDebug.Print a(1).X");
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("a")));
    }

    [TestMethod]
    public void S12_AnElementOfAnArrayMemberOfAType_IsAPartialDefinition()
    {
        var analyzed = Analyze("Dim o As Outer\no.Arr(1) = 5\nDebug.Print o.Arr(1)");
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("o")));
    }

    [TestMethod]
    public void S13_AMemberOfAnElement_InAWithBlock_IsAPartialDefinition()
    {
        var analyzed = Analyze("Dim a(1 To 3) As Point\nWith a(1)\n.X = 5\nEnd With\nDebug.Print a(1).X");
        Assert.IsTrue(analyzed.Defines(2, analyzed.Variable("a")));
    }

    [TestMethod]
    public void S14_AReadOfAMember_InAWithBlockOnAType_IsAUse()
    {
        var analyzed = Analyze("Dim p As Point\nWith p\n.Y = 1\nDebug.Print .Y\nEnd With");
        Assert.Contains(analyzed.Variable("p"), analyzed.Live.LiveAfter(analyzed.Lines(2, 2)), "Debug.Print .Y reads p.Y");
    }

    [TestMethod]
    public void S15_NestedWith_OnAMemberOfAType_DoesNotRecurseForever()
    {
        var analyzed = Analyze("Dim o As Outer\nWith o\nWith .Inner\n.X = 1\nEnd With\nEnd With");
        Assert.IsTrue(analyzed.Defines(3, analyzed.Variable("o")));
    }

    [TestMethod]
    public void S16_NestedWith_OnAnObject_DoesNotRecurseForever()
    {
        var analyzed = Analyze("Dim c As Collection\nSet c = New Collection\nWith c\nWith .Item(1)\n.Foo = 1\nEnd With\nEnd With");
        Assert.IsNotNull(analyzed.Accesses);
    }

    [TestMethod]
    public void S17_ALateBoundArgument_IsADefinition()
    {
        var analyzed = Analyze("Dim o As Object\nDim x As Long\no.Foo x\nDebug.Print x");
        Assert.IsTrue(analyzed.Defines(2, analyzed.Variable("x")));
    }

    [TestMethod]
    public void S18_AnArgumentOfANestedCall_IsADefinition()
    {
        var analyzed = Analyze("Dim x As Long\nBump F(x)\nDebug.Print x");
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("x")));
    }

    [TestMethod]
    public void S19_TheResult_ReadAndWritten_IsLiveAfterAnEarlierWrite()
    {
        var analyzed = Analyze("Run = 1\nRun = Run + 1", header: "Public Function Run() As Long", end: "End Function");
        Assert.Contains(analyzed.Model.Procedure, analyzed.Live.LiveAfter(analyzed.Lines(0, 0)));
        Assert.IsTrue(analyzed.Accesses.UsesAt(analyzed.Line(1)).Any(use => use.Variable == analyzed.Model.Procedure), "Run + 1 reads the result");
    }

    [TestMethod]
    public void S20_AFieldReadByACallee_IsLive()
    {
        var analyzed = Analyze("field = 1\nShowField\nfield = 2");
        Assert.Contains(analyzed.Variable("field"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)), "ShowField reads it");
    }

    [TestMethod]
    public void S21_AnArgumentOfADefaultMemberCall_OnAnObject_IsADefinition()
    {
        // Collection.Item(index): index is ByRef Variant in the type library.
        var analyzed = Analyze("Dim c As Collection\nDim k As Variant\nSet c = New Collection\nDebug.Print c(k)\nDebug.Print k");
        Assert.IsTrue(analyzed.Defines(3, analyzed.Variable("k")));
    }

    // ---------------- soundness: control flow ----------------

    [TestMethod]
    public void S30_GoSub_TheReturnGoesBackAfterTheSite()
    {
        var analyzed = Analyze("Dim x As Long\nGoSub S\nDebug.Print x\nExit Sub\nS:\nx = 1\nReturn");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(5, 5)));
    }

    [TestMethod]
    public void S31_OnGoSub_TheReturnGoesBackAfterTheSite()
    {
        var analyzed = Analyze("Dim x As Long\nOn n GoSub S, T\nDebug.Print x\nExit Sub\nS:\nx = 1\nReturn\nT:\nReturn");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(5, 5)));
    }

    [TestMethod]
    public void S32_OnGoTo_EachTarget()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nOn n GoTo A, B\nExit Sub\nA:\nExit Sub\nB:\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S33_SelectCase_ACaseThatDoesNotAssign()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nSelect Case n\nCase 1\nx = 2\nCase Else\nEnd Select\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S34_ExitDo_FromANestedFor()
    {
        var analyzed = Analyze("Dim x As Long, i As Long\nDo\nFor i = 1 To n\nx = x + i\nIf x > 5 Then Exit Do\nNext\nLoop\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)));
    }

    [TestMethod]
    public void S35_ABackEdgeToALineNumber()
    {
        var analyzed = Analyze("Dim x As Long\n10 x = x + 1\nIf x < n Then GoTo 10\nDebug.Print \"done\"");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S36_ForEach_TheElementAfterTheLoop()
    {
        var analyzed = Analyze("Dim v As Variant\nDim c As Collection\nSet c = New Collection\nv = 0\nFor Each v In c\nNext\nDebug.Print v");
        Assert.Contains(analyzed.Variable("v"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)), "an empty collection assigns nothing");
    }

    [TestMethod]
    public void S37_Stop_ContinuesAfterIt()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nStop\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S38_ReachingInto_ALoopBody_FromTheBodyItself()
    {
        var analyzed = Analyze("Dim x As Long, i As Long\nFor i = 1 To n\nDebug.Print x\nx = i\nNext");
        var body = analyzed.Lines(2, 3);
        Assert.IsTrue(analyzed.Reaching.ReachingInto(body).Any(definition => definition.Variable == analyzed.Variable("x")));
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(body), "the next iteration reads it");
    }

    // ---------------- precision ----------------

    // precision, not soundness: an argument counted as a possible definition makes a variable ByRef that could be ByVal (overnight #46, deferred).
    [TestMethod]
    [Ignore("Overnight #46: PassedAsArgument does not consult the parameter, so an argument of a ByVal parameter counts as a possible definition")]
    public void P01_AnArgumentForAByValParameter_IsNoDefinition()
    {
        var analyzed = Analyze("Dim x As Long\nTakesByVal x");
        Assert.IsFalse(analyzed.Defines(1, analyzed.Variable("x")));
    }

    [TestMethod]
    [Ignore("Overnight #46: PassedAsArgument does not consult the parameter, so an argument of a library function taken ByVal counts as a possible definition")]
    public void P02_AnArgumentOfAStdlibFunction_IsNoDefinition()
    {
        var analyzed = Analyze("Dim s As String\nDebug.Print Len(s)");
        Assert.IsFalse(analyzed.Defines(1, analyzed.Variable("s")));
    }
}


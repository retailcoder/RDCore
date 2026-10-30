using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Serialization;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmniSharpSerializer = OmniSharp.Extensions.JsonRpc.ISerializer;

namespace RDCore.Tests.Platform;

/// <summary>
/// A client that has only <c>RDCore.SDK.Protocol</c> writes and reads every message of it with <see cref="RDCorePlatformJson.Options"/>; the platform's own processes
/// write and read them with OmniSharp's Newtonsoft serializers. The JSON must be the same both ways, member for member.
/// </summary>
[TestClass]
public sealed class RDCorePlatformJsonWireTests
{
    private static readonly Uri Module = new("file:///c:/workspace/Module1.bas");

    private static readonly SourceRange Range = new(1, 2, 3, 4);

    /// <summary>
    /// One message of each Protocol type, with every member off its default, and what the platform puts on the wire for it: its SDK wrapper where the wrapper
    /// writes it differently from Newtonsoft's defaults (<see cref="SystemTextJsonBridge{T}"/>), the message itself otherwise.
    /// </summary>
    private static IEnumerable<(object Message, object OnTheWire)> Messages()
    {
        static (object, object) Same(object message) => (message, message);

        yield return Same(new SessionStatusParams { WaitMilliseconds = 250 });
        yield return Same(new SessionStatusResult
        {
            IsComposed = true,
            ProjectName = "VBAProject",
            ModuleCount = 3,
            ReferenceCount = 2,
            Memory = new SessionMemoryStatus { ReservedBytes = 1, AvailableBytes = 2, AllocatedBytes = 3, FreeBytes = 4, CommittedBytes = 5, LargestFreeBlock = 6 },
        });
        yield return Same(new ExecuteSessionParams { Source = "Debug.Print 1", ModuleName = "Module1", EntryPoint = "Main", Debug = true, Immediate = true });
        yield return Same(new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.Suspended,
            Output = ["1"],
            StreamedLines = 7,
            Diagnostics = ["RDC00001"],
            ErrorNumber = 13,
            ErrorMessage = "Type mismatch",
            ErrorCode = "VBR00013",
            ErrorTitle = "Type mismatch",
            ErrorSource = "VBAProject",
            ErrorLine = 3,
            ErrorCharacter = 4,
            ErrorLineNumber = 100,
            ConditionError = "x",
            StackTrace = [new ExecuteStackFrame("Main", 1, 2, [5, 6])],
        });
        yield return Same(new DiscardSessionParams { ModuleName = "Module1", EndProgram = true });
        yield return Same(new DiscardSessionResult { Discarded = 2 });
        yield return Same(new PeekSessionParams { Address = 16 });
        yield return Same(new PeekSessionResult { Address = 16, IsAllocated = true, Value = 255 });
        yield return Same(new PokeSessionParams { Address = 16, Value = 7 });
        yield return Same(new PokeSessionResult { Address = 16, IsWritten = true });

        yield return Same(new SessionMessageBoxParams { Prompt = "Hello", Title = "Title", Style = 4, Output = ["before"] });
        yield return Same(new MessageBoxResult { Handled = true, Result = 6 });
        yield return Same(new SessionInputBoxParams { Prompt = "Name?", Title = "Title", Default = "x", XPos = 10, YPos = 20, Output = ["before"] });
        yield return Same(new InputBoxResult { Handled = true, Cancelled = true, Value = "y" });

        yield return Same(new SessionDebugResumeParams { Step = StepKind.Over });
        yield return Same(new SessionDebugResumeParams { Step = null });
        yield return Same(new SessionDebugPauseParams());
        yield return Same(new SessionDebugGotoParams { Line = 4, Label = "Retry" });
        yield return Same(new SessionDebugEditStatusParams());
        yield return Same(new SessionDebugTerminateParams { Wipe = true });
        yield return Same(new SessionDebugBreakpointsParams { ModuleName = "Module1", Breakpoints = [new SessionBreakpointLine { Line = 2, IsEnabled = false, Condition = "i > 3" }] });
        yield return Same(new SessionDebugStackParams());
        yield return Same(new SessionDebugVariablesParams { FrameId = 1, Scope = HostVariableScope.Module, Reference = 9 });
        yield return Same(new SessionDebugEvaluateParams { FrameId = 1, Expression = "i + 1" });
        yield return Same(new SessionDebugExecuteParams { FrameId = 1, Statement = "i = 2" });

        yield return Same(new HostDebugAck { Acted = true });
        yield return Same(new HostDebugGotoResult { Moved = true, Reason = "moved", Line = 4, Character = 0 });
        yield return Same(new HostDebugGotoResult { Moved = false, Reason = null });
        yield return Same(new HostDebugEditStatusResult
        {
            Suspended = true,
            Edits = ImmutableArray.Create(
                new HostDebugEdit { Name = "Module1.Main", Kind = HostDebugEditKind.Restart, Reason = "signature", ModuleUri = Module, Range = Range },
                new HostDebugEdit { Name = "Module1.Other", ModuleUri = null, Range = null }),
        });
        yield return Same(new HostDebugBreakpointsResult { Breakpoints = [new HostBreakpoint(2, true), new HostBreakpoint(3, false, "Expected: expression")], Judged = true });
        yield return Same(new HostDebugStackResult { Frames = [new HostStackFrame(1, "Main", "Module1", 2, 3, [4], "ErrHandler"), new HostStackFrame(2, "Other", "Module1", 5, 6)] });
        yield return Same(new HostDebugVariablesResult { Variables = [new HostVariable("i", "1", "Integer"), new HostVariable("o", "Object", "Class1", 3)], NotListed = 2 });
        yield return Same(new HostDebugEvaluateResult { Success = true, Value = "2", Type = "Integer", Reference = 1, Error = "none", Output = ["x"], EndedTheProgram = true });

        yield return Same(new PlatformInitializeParams
        {
            ExpectedComponent = CoreServerComponent.LanguageServer,
            Expected = new CorePlatformClientCapabilities
            {
                Parsing = new ParserCapabilities { ParseFullDocument = new ParseFullDocument(true) },
                EnvironmentHost = new EnvironmentHostCapabilities { DefineSymbols = new DefineSymbols(true) },
                LanguageServer = new LanguageServerCapabilities { SessionExecute = new SessionExecute(true), SessionInteraction = new SessionInteraction(true) },
            },
        });
        yield return Same(new PlatformInitializeParams { ExpectedComponent = CoreServerComponent.LanguageServer, Expected = new CorePlatformClientCapabilities { LanguageServer = new() } });
        yield return Same(new PlatformInitializeResult { Component = CoreServerComponent.LanguageServer, Provided = ["SessionExecute", "SessionStatus"] });

        // declared by an extension's assembly and read from its manifest, in no capability group: written as they are.
        yield return Same(new DiagnoseDocument(true));
        yield return Same(new CliCommand(true));

        yield return Same(new SourceLocation(Module, Range));

        yield return (new RubberduckIndenterSettingsParams(), new RubberduckIndenterSettingsRequest());

        var read = new RubberduckIndenterSettingsResult
        {
            Source = RubberduckIndenterSettingsSource.SmartIndenterRegistry,
            Values = new Dictionary<string, JsonElement>
            {
                ["IndentProc"] = JsonSerializer.SerializeToElement("Y"),
                ["AlignDimCol"] = JsonSerializer.SerializeToElement(15),
            },
            TabWidth = 4,
        };
        yield return (read, new RubberduckIndenterSettingsReply(read));

        var apply = new RubberduckIndenterSettingsApplyParams { Source = RubberduckIndenterSettingsSource.RubberduckConfig, Settings = new() { ["Formatting.IndentSpaces"] = "2" } };
        yield return (apply, new RubberduckIndenterSettingsApplyRequest(apply));
        yield return Same(new RubberduckIndenterSettingsApplyResult { Applied = 1, Refused = ["Formatting.Nope"] });
    }

    private static IEnumerable<OmniSharpSerializer> PlatformSerializers() => [new LspSerializer(), new PullDiagnosticsLspSerializer()];

    private static JsonNode? Parse(string json) => JsonNode.Parse(json);

    private static void AssertSameJson(string expected, string actual, string what)
        => Assert.IsTrue(JsonNode.DeepEquals(Parse(expected), Parse(actual)), $"{what}\nexpected: {expected}\nactual:   {actual}");

    [TestMethod]
    public void EveryMessage_IsWrittenTheSame_ByTheProtocolOptionsAndByThePlatform()
    {
        foreach (var serializer in PlatformSerializers())
        {
            foreach (var (message, onTheWire) in Messages())
            {
                var protocol = JsonSerializer.Serialize(message, message.GetType(), RDCorePlatformJson.Options);
                var platform = serializer.SerializeObject(onTheWire);

                AssertSameJson(platform, protocol, $"{message.GetType().Name}, {serializer.GetType().Name}");
            }
        }
    }

    [TestMethod]
    public void WhatThePlatformWrites_TheProtocolOptionsRead_AsTheSameMessage()
    {
        foreach (var serializer in PlatformSerializers())
        {
            foreach (var (message, onTheWire) in Messages())
            {
                var platform = serializer.SerializeObject(onTheWire);
                var read = JsonSerializer.Deserialize(platform, message.GetType(), RDCorePlatformJson.Options);

                AssertSameJson(platform, JsonSerializer.Serialize(read, message.GetType(), RDCorePlatformJson.Options), $"{message.GetType().Name}, {serializer.GetType().Name}");
            }
        }
    }

    [TestMethod]
    public void WhatTheProtocolOptionsWrite_ThePlatformReads_AsTheSameMessage()
    {
        foreach (var serializer in PlatformSerializers())
        {
            foreach (var (message, onTheWire) in Messages())
            {
                var protocol = JsonSerializer.Serialize(message, message.GetType(), RDCorePlatformJson.Options);
                var read = serializer.DeserializeObject(protocol, onTheWire.GetType());

                AssertSameJson(protocol, serializer.SerializeObject(read), $"{message.GetType().Name}, {serializer.GetType().Name}");
            }
        }
    }

    [TestMethod]
    public void EveryTypeOfTheProtocol_IsInAMessageAbove()
    {
        var reached = new HashSet<Type>();
        foreach (var (message, _) in Messages())
        {
            Reach(message, reached);
        }

        var unreached = typeof(RDCorePlatformJson).Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && !type.IsEnum && !type.IsInterface && !reached.Contains(type))
            .Select(type => type.FullName)
            .ToList();

        Assert.IsEmpty(unreached, "Protocol types no message above has: " + string.Join(", ", unreached));
    }

    // every type a message is made of, through its public properties and the items of its collections.
    private static void Reach(object? value, HashSet<Type> reached)
    {
        if (value is null or string or Uri or JsonElement || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            return;
        }

        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                Reach(item, reached);
            }

            return;
        }

        reached.Add(value.GetType());
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
        {
            Reach(property.GetValue(value), reached);
        }
    }
}

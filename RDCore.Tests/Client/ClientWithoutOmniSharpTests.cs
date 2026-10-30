using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Client;

/// <summary>
/// The language server, started as an IDE starts it, with a client that is not OmniSharp's: one that writes and reads JSON-RPC itself, as HexIDE's does. The requests the
/// language server sends it are answered, and the answers reach what is waiting for them.
/// </summary>
[TestClass]
[TestCategory("Platform")]
public sealed class ClientWithoutOmniSharpTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    private static string AppHost(string name) => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? $"{name}.exe" : name);

    // the module a program is run as, as rdc.exe's shell sends one: its procedures, the module named apart.
    private const string Program = "Public Sub Main()\r\n    Debug.Print MsgBox(\"Continue?\", vbYesNo)\r\nEnd Sub\r\n";

    private const string ModuleText = $"Attribute VB_Name = \"Program\"\r\n{Program}";

    // what HexIDE's LSP client declares (HexIDE.Lsp/VBLspClient.InitializeAsync), as it writes it.
    private const string AsHexIdeDeclares = """
        {
            "textDocument": {
                "publishDiagnostics": {},
                "hover": { "contentFormat": ["plaintext"] },
                "synchronization": { "didSave": true },
                "codeLens": {},
                "diagnostic": {},
                "documentSymbol": { "hierarchicalDocumentSymbolSupport": true },
                "semanticTokens": { "requests": {}, "tokenTypes": ["keyword", "variable"], "tokenModifiers": ["declaration"], "formats": ["relative"] }
            },
            "workspace": { "executeCommand": {}, "workspaceFolders": true, "diagnostics": {}, "fileOperations": {} },
            "window": {}
        }
        """;

    private const string ClaimsDialogs = """
        { "expectedComponent": 1, "expected": { "languageServer": { "sessionExecute": { "isSupported": true }, "sessionInteraction": { "isSupported": true } } } }
        """;

    [TestMethod]
    [DataRow("{}", DisplayName = "a client that declares nothing")]
    [DataRow(AsHexIdeDeclares, DisplayName = "a client that declares what HexIDE does")]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AClientThatShowsDialogs_AnswersTheMsgBoxOfAProgram_AndTheProgramCompletes(string capabilities)
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(capabilities));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());

        var handshake = await rpc.RequestAsync(2, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = Program, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        await platform.ShutdownAsync(4);
    }

    // the order HexIDE's add-in sent its messages in (C:\Dev\Claude\hexide-msgbox-wire-2026-10-30.txt): the document opened and its diagnostics asked for before the
    // platform handshake, which is sent without waiting for them; its requests numbered from 2, so that the handshake is 4, as the language server's msgBox is.
    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AClientInHexIdesOrder_AnswersTheMsgBoxOfAProgram_AndTheProgramCompletes()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(2, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });

        var diagnostics = rpc.RequestAsync(3, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        var handshake = await rpc.RequestAsync(4, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"]);
        StringAssert.Contains(WholeLogOf(platform.Root), "The client expects LanguageServer to provide \"LanguageServer: SessionExecute, SessionInteraction\"",
            "what the client claimed is in the language server's file");
        await platform.ShutdownAsync(6);
    }

    // the platform as a deployment has it, with a workspace of one module, and the language server started from it as an IDE starts it.
    private sealed class Platform : IAsyncDisposable
    {
        private readonly string _root;
        private readonly Process _process;
        private readonly NamedPipeClientStream _pipe;

        private Platform(string root, string workspace, Process process, NamedPipeClientStream pipe)
        {
            _root = root;
            Workspace = new Uri(workspace).AbsoluteUri;
            ModuleUri = new Uri(Path.Combine(workspace, "Program.bas")).AbsoluteUri;
            _process = process;
            _pipe = pipe;
            Rpc = new JsonRpc(pipe, (method, _) =>
            {
                Asked.Enqueue(method);
                return method == RDCorePlatformProtocol.SessionMessageBox ? new JsonObject { ["handled"] = true, ["result"] = 6 } : null;
            });
        }

        public JsonRpc Rpc { get; }

        public string Root => _root;

        public string Workspace { get; }

        public string ModuleUri { get; }

        public ConcurrentQueue<string> Asked { get; } = [];

        public static async Task<Platform> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "RDCore", "raw-client", Guid.NewGuid().ToString("N"));
            var workspace = Path.Combine(root, "Workspace");
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, "Program.bas"), ModuleText);
            File.WriteAllText(Path.Combine(root, PlatformManifest.FileName), JsonSerializer.Serialize(new PlatformManifest
            {
                PlatformVersion = "0.1",
                ExtensionsDirectory = "Extensions",
                LangService = AppHost("RDCore.LanguageServer"),
                ParseServer = AppHost("RDCore.ParseServer"),
                HostService = AppHost("rdc"),
            }));

            var launch = RDCoreLanguageServerLaunch.Prepare(new RDCoreLanguageServerLaunchOptions
            {
                PlatformRoot = root,
                Workspace = new Uri(workspace).AbsoluteUri,
                PipeName = $"rdcore-raw-client-{Environment.ProcessId}-{Guid.NewGuid():N}",
            });
            var start = launch.ToProcessStartInfo();
            start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");

            var process = Process.Start(start)!;
            var pipe = new NamedPipeClientStream(".", launch.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)Bound.TotalMilliseconds);
            return new Platform(root, workspace, process, pipe);
        }

        public JsonObject InitializeParams(string capabilities) => new()
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = Workspace,
            ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = Workspace, ["name"] = "Workspace" }),
            ["trace"] = "off",
            ["locale"] = "en-US",
            ["capabilities"] = JsonNode.Parse(capabilities),
        };

        public void AssertTheProgramPrintedTheAnswer(JsonObject executed)
        {
            var told = $"{executed.ToJsonString()}\nasked: {string.Join(", ", Asked)}\nlogged by the server:\n{string.Join("\n", Rpc.Logged)}\n{LogOf(_root)}";
            Assert.IsNull(executed["error"], told);
            Assert.Contains(RDCorePlatformProtocol.SessionMessageBox, Asked, "the client was asked: " + told);
            Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]!["outcome"]!.GetValue<int>(), told);
            CollectionAssert.Contains(executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), "6",
                "the program printed what the client answered: " + told);
        }

        public async Task ShutdownAsync(int id)
        {
            _ = await Rpc.RequestAsync(id, "shutdown", new JsonObject());
            await Rpc.NotifyAsync("exit", new JsonObject());
            await _process.WaitForExitAsync().WaitAsync(Bound);
        }

        public async ValueTask DisposeAsync()
        {
            await Rpc.DisposeAsync();
            await _pipe.DisposeAsync();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            _process.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // a log may still be held as the processes go: the folder is a temporary one.
            }
        }
    }

    // the end of the language server's log, which says what it did with what it was sent.
    private static string LogOf(string root) => string.Join('\n', WholeLogOf(root).Split('\n').TakeLast(60));

    private static string WholeLogOf(string root)
    {
        var logs = Directory.Exists(root) ? Directory.EnumerateFiles(root, "RDCore.LanguageServer*.log", SearchOption.AllDirectories).ToArray() : [];
        if (logs.Length == 0)
        {
            return "(no language server log)";
        }

        using var stream = new FileStream(logs[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    // JSON-RPC 2.0 over the pipe, as LSP frames it, written by hand as a client that is not OmniSharp's writes it: both ways at once, a request answered whenever its
    // answer comes, and the server's requests answered as they arrive.
    private sealed class JsonRpc : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly Func<string, JsonNode?, JsonNode?> _answer;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
        private readonly SemaphoreSlim _writing = new(1, 1);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _reading;

        public JsonRpc(Stream stream, Func<string, JsonNode?, JsonNode?> answer)
        {
            _stream = stream;
            _answer = answer;
            _reading = Task.Run(ReadAllAsync);
        }

        /// <summary>What the server logged to the client (<c>window/logMessage</c>).</summary>
        public ConcurrentQueue<string> Logged { get; } = [];

        public Task NotifyAsync(string method, JsonObject parameters) => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

        public async Task<JsonObject> RequestAsync(int id, string method, JsonObject parameters)
        {
            var answered = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = answered;
            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            return await answered.Task.WaitAsync(Bound);
        }

        private async Task ReadAllAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var message = await ReadAsync();
                    if (message["method"] is { } called)
                    {
                        var method = called.GetValue<string>();
                        if (message["id"] is { } asked)
                        {
                            // a request from the server is answered: what the client does with it, or nothing.
                            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = asked.DeepClone(), ["result"] = _answer(method, message["params"])?.DeepClone() });
                        }
                        else if (method == "window/logMessage")
                        {
                            Logged.Enqueue(message["params"]?["message"]?.ToString() ?? string.Empty);
                        }
                    }
                    else if (message["id"] is { } id && id.GetValueKind() == JsonValueKind.Number && _pending.TryRemove(id.GetValue<int>(), out var answered))
                    {
                        answered.TrySetResult(message);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or EndOfStreamException or ObjectDisposedException or OperationCanceledException)
            {
                foreach (var pending in _pending.Values)
                {
                    pending.TrySetException(exception);
                }
            }
        }

        private async Task WriteAsync(JsonObject message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await _writing.WaitAsync();
            try
            {
                await _stream.WriteAsync(header);
                await _stream.WriteAsync(body);
                await _stream.FlushAsync();
            }
            finally
            {
                _writing.Release();
            }
        }

        private async Task<JsonObject> ReadAsync()
        {
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await _stream.ReadAsync(one, _stop.Token) == 0)
                {
                    throw new EndOfStreamException("the server closed the pipe");
                }

                header.Append((char)one[0]);
            }

            var length = int.Parse(header.ToString().Split("Content-Length:")[1].Split("\r\n")[0].Trim());
            var body = new byte[length];
            await _stream.ReadExactlyAsync(body, _stop.Token);
            return JsonNode.Parse(body)!.AsObject();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _reading.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // the pipe is disposed after, which ends the read.
            }
        }
    }
}

using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
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

    [TestMethod]
    [DataRow("{}", DisplayName = "a client that declares nothing")]
    [DataRow(AsHexIdeDeclares, DisplayName = "a client that declares what HexIDE does")]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AClientThatShowsDialogs_AnswersTheMsgBoxOfAProgram_AndTheProgramCompletes(string capabilities)
    {
        var root = Path.Combine(Path.GetTempPath(), "RDCore", "raw-client", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Program.bas"), $"Attribute VB_Name = \"Program\"\r\n{Program}");
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

        using var process = Process.Start(start)!;
        var asked = new List<string>();
        try
        {
            using var pipe = new NamedPipeClientStream(".", launch.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)Bound.TotalMilliseconds);
            var rpc = new JsonRpc(pipe, (method, _) =>
            {
                asked.Add(method);
                return method == RDCorePlatformProtocol.SessionMessageBox ? new JsonObject { ["handled"] = true, ["result"] = 6 } : null;
            });

            var initialize = await rpc.RequestAsync("initialize", new JsonObject
            {
                ["processId"] = Environment.ProcessId,
                ["rootUri"] = new Uri(workspace).AbsoluteUri,
                ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = new Uri(workspace).AbsoluteUri, ["name"] = "Workspace" }),
                ["trace"] = "off",
                ["locale"] = "en-US",
                ["capabilities"] = JsonNode.Parse(capabilities),
            });
            Assert.IsNull(initialize["error"], initialize.ToJsonString());
            await rpc.NotifyAsync("initialized", new JsonObject());

            var handshake = await rpc.RequestAsync(RDCorePlatformHandshake.Method, JsonNode.Parse("""
                { "expectedComponent": 1, "expected": { "languageServer": { "sessionExecute": { "isSupported": true }, "sessionInteraction": { "isSupported": true } } } }
                """)!.AsObject());
            Assert.IsNull(handshake["error"], handshake.ToJsonString());

            var executed = await rpc.RequestAsync(RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = Program, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

            Assert.IsNull(executed["error"], $"{executed.ToJsonString()}\n{LogOf(root)}");
            Assert.Contains(RDCorePlatformProtocol.SessionMessageBox, asked, $"the client was asked; it was asked {string.Join(", ", asked)}\n{executed.ToJsonString()}\n{LogOf(root)}");
            Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]!["outcome"]!.GetValue<int>(), $"{executed.ToJsonString()}\n{LogOf(root)}");
            CollectionAssert.Contains(executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), "6", "the program printed what the client answered");

            _ = await rpc.RequestAsync("shutdown", new JsonObject());
            await rpc.NotifyAsync("exit", new JsonObject());
            await process.WaitForExitAsync().WaitAsync(Bound);
        }
        catch (TimeoutException exception)
        {
            Assert.Fail($"{exception.Message}; the client was asked {string.Join(", ", asked)}\n{LogOf(root)}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // a log may still be held as the processes go: the folder is a temporary one.
            }
        }
    }

    // the end of the language server's log, which says what it did with what it was sent.
    private static string LogOf(string root)
    {
        var logs = Directory.Exists(root) ? Directory.EnumerateFiles(root, "RDCore.LanguageServer*.log", SearchOption.AllDirectories).ToArray() : [];
        if (logs.Length == 0)
        {
            return "(no language server log)";
        }

        using var stream = new FileStream(logs[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var lines = new StreamReader(stream).ReadToEnd().Split('\n');
        return string.Join('\n', lines.TakeLast(60));
    }

    // JSON-RPC 2.0 over the pipe, as LSP frames it, written by hand as a client that is not OmniSharp's writes it.
    private sealed class JsonRpc(Stream stream, Func<string, JsonNode?, JsonNode?> answer)
    {
        private int _id;

        public Task NotifyAsync(string method, JsonObject parameters) => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

        public async Task<JsonObject> RequestAsync(string method, JsonObject parameters)
        {
            var id = ++_id;
            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            while (true)
            {
                var message = await ReadAsync().WaitAsync(Bound);
                if (message["id"] is { } answered && message["method"] is null && answered.GetValueKind() == JsonValueKind.Number && answered.GetValue<int>() == id)
                {
                    return message;
                }

                // a request from the server is answered: what the client does with it, or nothing.
                if (message["id"] is { } asked && message["method"] is { } called)
                {
                    await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = asked.DeepClone(), ["result"] = answer(called.GetValue<string>(), message["params"])?.DeepClone() });
                }
            }
        }

        private async Task WriteAsync(JsonObject message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }

        private async Task<JsonObject> ReadAsync()
        {
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    throw new EndOfStreamException("the server closed the pipe");
                }

                header.Append((char)one[0]);
            }

            var length = int.Parse(header.ToString().Split("Content-Length:")[1].Split("\r\n")[0].Trim());
            var body = new byte[length];
            await stream.ReadExactlyAsync(body);
            return JsonNode.Parse(body)!.AsObject();
        }
    }
}

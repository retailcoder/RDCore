using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RDCore.External.Automation;
using RDCore.External.Hosting;
using RDCore.External.Native;
using RDCore.SDK.Server;

namespace RDCore.FormsHost.Spike;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Trace.Line($"start pid={Environment.ProcessId} os={Environment.OSVersion.Platform} headless={Ui.Headless}");
        Ui.Start();
        using var host = new SpikeExternalHost();
        var code = await host.RunAsync(args);
        Trace.Line($"exit {code}");
        Ui.Stop();
        return code;
    }
}

/// <summary>
/// rdc's external mode, composed with Avalonia's forms instead of Microsoft Forms.
/// </summary>
internal sealed class SpikeExternalHost : RDCorePlatformServerHost<ExternalHostApp>
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);
        var server = new AvaloniaAutomationServer();
        services
            .AddSingleton(new ExternalAutomationService(server, new AvaloniaFormHost(server)))
            .AddSingleton(new NativeCallService(UnsupportedNativePlatform.Instance));
    }

    protected override string? LogFileName => "RDCore.FormsHost.Spike.log";
}

/// <summary>
/// The Avalonia UI thread of the process: every window and control lives on it.
/// </summary>
internal static class Ui
{
    private static readonly CancellationTokenSource Stopping = new();
    private static readonly ManualResetEventSlim Ready = new();

    public static bool Headless => Environment.GetEnvironmentVariable("RDFORMS_SPIKE_HEADLESS") == "1";

    public static void Start()
    {
        var thread = new Thread(() =>
        {
            var builder = AppBuilder.Configure<SpikeApp>();
            builder = Headless
                ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                : builder.UsePlatformDetect();
            builder.SetupWithoutStarting();
            Trace.Line($"ui up on thread {Environment.CurrentManagedThreadId}, platform {(Headless ? "headless" : "detected")}");
            Ready.Set();
            Dispatcher.UIThread.MainLoop(Stopping.Token);
        })
        { IsBackground = true, Name = "Avalonia UI" };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        Ready.Wait();
    }

    public static void Stop() => Stopping.Cancel();

    /// <summary>
    /// Runs on the UI thread and waits: at once when this is the UI thread (a call a handler makes, which the event's wait runs here).
    /// </summary>
    public static T Run<T>(Func<T> call) => Dispatcher.UIThread.CheckAccess() ? call() : Dispatcher.UIThread.Invoke(call);

    public static void Run(Action call) => Run<object?>(() =>
    {
        call();
        return null;
    });

    /// <summary>
    /// Dispatches what waits on the UI thread for about <paramref name="slice"/>: what a thread that waits for the handlers of its event does meanwhile.
    /// </summary>
    public static void Pump(TimeSpan slice)
    {
        var frame = new DispatcherFrame();
        DispatcherTimer.RunOnce(() => frame.Continue = false, slice);
        Dispatcher.UIThread.PushFrame(frame);
    }
}

internal sealed class SpikeApp : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());
}

internal static class Trace
{
    private static readonly string? File = Environment.GetEnvironmentVariable("RDFORMS_SPIKE_TRACE");
    private static readonly object Gate = new();

    public static void Line(string text)
    {
        if (File is null)
        {
            return;
        }

        lock (Gate)
        {
            System.IO.File.AppendAllText(File, $"[host t{Environment.CurrentManagedThreadId}] {text}{Environment.NewLine}");
        }
    }
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RDCore.External.Automation;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// Where the RDForms windows of a process run: on a desktop, or headless, with no window anyone can see.
/// </summary>
public enum RDFormsPlatform
{
    /// <summary>
    /// The windows are the desktop's: Windows, X11 or macOS, whichever the process runs on.
    /// </summary>
    Desktop,

    /// <summary>
    /// The windows are drawn and laid out as on a desktop, but are not shown on any screen, and only code can use them.
    /// </summary>
    Headless,
}

// the one Avalonia application of the process, on a thread of its own: Avalonia allows one UI thread to a process, and the thread that sets it up is that thread.
// Every call on an RDForms object is made there, inline when it is made there already, as the calls an event's handlers make are.
internal static class RDFormsUI
{
    private const int Failed = unchecked((int)0x80004005);

    private static readonly object Gate = new();
    private static Dispatcher? _dispatcher;
    private static RDFormsPlatform _platform;

    // how many calls of the program the UI thread is making: an event raised while it makes one is the answer to that call. Touched on the UI thread only.
    private static int _calls;

    // whether the UI thread is making a call of the program, which is what makes an event it raises synchronous.
    public static bool IsInCall => _calls > 0;

    public static Dispatcher Dispatcher => _dispatcher ?? throw new InvalidOperationException("The RDForms UI thread is not started.");

    // starts the UI thread on the platform asked for, once per process: the first start says which, and a later one that asks for the other cannot have it.
    public static void Start(RDFormsPlatform platform)
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
            {
                if (platform != _platform)
                {
                    throw new AutomationException(Failed, $"The RDForms windows of this process are {_platform}, and cannot be {platform} as well.");
                }

                return;
            }

            Exception? failure = null;
            Dispatcher? started = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                Dispatcher own;
                try
                {
                    BuilderFor(platform).SetupWithoutStarting();
                    own = Dispatcher.UIThread;
                    if (!own.CheckAccess())
                    {
                        throw new InvalidOperationException("Avalonia's UI thread was set up on another thread of this process.");
                    }

                    started = own;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    return;
                }
                finally
                {
                    ready.Set();
                }

                own.MainLoop(CancellationToken.None);
            })
            {
                IsBackground = true,
                Name = "RDForms UI",
            };

            if (OperatingSystem.IsWindows())
            {
                // the desktop's drag and drop, clipboard and accessibility need a single-threaded apartment on Windows.
                thread.SetApartmentState(ApartmentState.STA);
            }

            thread.Start();
            ready.Wait();
            if (failure is not null)
            {
                throw new AutomationException(Failed, $"The RDForms windows cannot be started: {failure.Message}");
            }

            _platform = platform;
            _dispatcher = started;
        }
    }

    private static AppBuilder BuilderFor(RDFormsPlatform platform) => platform switch
    {
        RDFormsPlatform.Headless => AppBuilder.Configure<RDFormsApplication>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }),
        _ => AppBuilder.Configure<RDFormsApplication>()
            .UsePlatformDetect()
            .WithInterFont(),
    };

    // makes a call of the program on the UI thread, and answers what it returned. What fails there fails the call, as a server's failure does.
    public static T Run<T>(Func<T> call)
    {
        var dispatcher = Dispatcher;
        return dispatcher.CheckAccess() ? Make(call) : dispatcher.Invoke(() => Make(call));
    }

    public static void Run(Action call) => Run(() =>
    {
        call();
        return true;
    });

    private static T Make<T>(Func<T> call)
    {
        _calls++;
        try
        {
            return call();
        }
        catch (Exception exception) when (exception is not AutomationException)
        {
            throw new AutomationException(Failed, exception.Message);
        }
        finally
        {
            _calls--;
        }
    }

    // does the work that waits on the UI thread: what a DoEvents dispatches, and what an event that waits for its handlers lets through meanwhile.
    public static void Pump() => Dispatcher.RunJobs(null);
}

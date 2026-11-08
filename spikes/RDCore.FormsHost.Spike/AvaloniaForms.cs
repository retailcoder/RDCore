using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;

namespace RDCore.FormsHost.Spike;

/// <summary>
/// An object the program reaches through the automation server: a form, a control, or an event's <c>Return*</c> argument.
/// </summary>
internal abstract class SpikeObject
{
    public IAutomationEventSink? Sink { get; set; }

    public abstract string ClassName { get; }

    public abstract object? Invoke(string member, AutomationInvocation invocation, object?[] arguments);

    protected static AutomationException UnknownName(string member) => new(unchecked((int)0x80020006), $"Unknown name: {member}");
}

/// <summary>
/// Counts the program's calls in progress: an event raised during one is raised by a call the program waits for (<see cref="AutomationEvent.IsSynchronous"/>).
/// </summary>
internal static class Calls
{
    public static int Depth { get; set; }

    public static T Counted<T>(Func<T> call) => Ui.Run(() =>
    {
        Depth++;
        try
        {
            return call();
        }
        finally
        {
            Depth--;
        }
    });

    public static void Counted(Action call) => Counted<object?>(() =>
    {
        call();
        return null;
    });
}

/// <summary>
/// Raises an event of an object to whatever listens to it - the environment host's handlers - and waits for them, as the thread a control raises it on.
/// </summary>
internal static class Events
{
    private static readonly Queue<(SpikeObject Source, string Name, object?[] Arguments, Action<object?[]>? Answered)> Held = new();

    public static bool IsHeld { get; set; }

    public static void Raise(SpikeObject source, string name, object?[] arguments, Action<object?[]>? answered = null)
    {
        if (source.Sink is not { } sink)
        {
            Trace.Line($"event {source.ClassName}.{name}: nobody listens");
            return;
        }

        var synchronous = Calls.Depth > 0;
        if (IsHeld && !synchronous)
        {
            // what the user does while a program runs waits until the program waits for it (DoEvents, a modal Show) or ends.
            Trace.Line($"event {name} held");
            Held.Enqueue((source, name, arguments, answered));
            return;
        }

        Trace.Line($"event {name} -> env host (synchronous={synchronous}) args=[{string.Join(", ", arguments.Select(Describe))}]");
        sink.OnEvent(new AutomationEvent(name, arguments, synchronous, Ui.Pump));
        Trace.Line($"event {name} answered args=[{string.Join(", ", arguments.Select(Describe))}]");
        answered?.Invoke(arguments);
    }

    public static void ReleaseHeld()
    {
        while (Held.TryDequeue(out var held))
        {
            Raise(held.Source, held.Name, held.Arguments, held.Answered);
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "Empty",
        SpikeObject spike => spike.ToString()!,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}

/// <summary>
/// <c>MSForms.ReturnInteger</c>, <c>MSForms.ReturnBoolean</c>: an event's argument whose handler writes the answer to its default member.
/// </summary>
internal sealed class ReturnValue(string className, object value) : SpikeObject
{
    public object Value { get; private set; } = value;

    public override string ClassName { get; } = className;

    public override object? Invoke(string member, AutomationInvocation invocation, object?[] arguments)
    {
        if (member is not ("Value" or "" or "[DISPID=0]"))
        {
            throw UnknownName(member);
        }

        switch (invocation)
        {
            case AutomationInvocation.Get:
                return Value;
            case AutomationInvocation.Let:
                Value = Value is bool ? Convert.ToBoolean(arguments[^1], CultureInfo.InvariantCulture) : Convert.ToInt16(arguments[^1], CultureInfo.InvariantCulture);
                Trace.Line($"{ClassName}.Value = {Value}");
                return null;
            default:
                throw UnknownName(member);
        }
    }

    public override string ToString() => $"{ClassName}({Value})";
}

/// <summary>
/// A control of a form, as an Avalonia control.
/// </summary>
internal sealed class SpikeControl : SpikeObject
{
    public SpikeControl(FormControlDescriptor descriptor)
    {
        Name = descriptor.Name;
        Class = descriptor.Class;
        View = Class.Name switch
        {
            "TextBox" => TextBox(),
            "CommandButton" => Button(),
            "Label" => new TextBlock { Text = Name },
            "OptionButton" => new RadioButton { Content = Name },
            "CheckBox" => new CheckBox { Content = Name },
            "ComboBox" => new ComboBox { MinWidth = 120 },
            _ => new TextBlock { Text = $"[{Class}] {Name}" },
        };
        View.Name = Name;
    }

    public string Name { get; }

    public FormControlClass Class { get; }

    public Control View { get; }

    public override string ClassName => Class.ToString();

    private TextBox TextBox()
    {
        var box = new TextBox { MinWidth = 160 };

        // KeyPress(ByVal KeyAscii As MSForms.ReturnInteger): the character is what the handlers leave; 0 swallows it.
        box.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (e.Text is not [var typed])
            {
                return;
            }

            var keyAscii = new ReturnValue("MSForms.ReturnInteger", (short)typed);
            Events.Raise(this, "KeyPress", [keyAscii]);
            var answer = Convert.ToInt32(keyAscii.Value, CultureInfo.InvariantCulture);
            if (answer == 0)
            {
                e.Handled = true;
            }
            else
            {
                e.Text = ((char)answer).ToString();
            }
        }, RoutingStrategies.Tunnel);
        // Change is raised as the text changes, inside whatever changed it: Avalonia's own TextChanged comes on a later pass of the dispatcher, after a Let from code
        // has returned, which would make it the user's input.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.TextBox.TextProperty)
            {
                Events.Raise(this, "Change", []);
            }
        };
        return box;
    }

    private Button Button()
    {
        var button = new Button { Content = Name };
        button.Click += (_, _) => Events.Raise(this, "Click", []);
        return button;
    }

    public override object? Invoke(string member, AutomationInvocation invocation, object?[] arguments)
    {
        var value = arguments.Length > 0 ? arguments[^1] : null;
        switch (View, member.ToUpperInvariant(), invocation)
        {
            case (TextBox box, "TEXT" or "VALUE" or "", AutomationInvocation.Get):
                return box.Text ?? "";
            case (TextBox box, "TEXT" or "VALUE" or "", AutomationInvocation.Let):
                box.Text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return null;
            case (Button button, "CAPTION", AutomationInvocation.Get):
                return button.Content as string ?? "";
            case (Button button, "CAPTION", AutomationInvocation.Let):
                button.Content = Convert.ToString(value, CultureInfo.InvariantCulture);
                return null;
            case (TextBlock label, "CAPTION", AutomationInvocation.Get):
                return label.Text ?? "";
            case (TextBlock label, "CAPTION", AutomationInvocation.Let):
                label.Text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return null;
            case (_, "NAME", AutomationInvocation.Get):
                return Name;
            case (_, "ENABLED", AutomationInvocation.Get):
                return View.IsEnabled;
            case (_, "ENABLED", AutomationInvocation.Let):
                View.IsEnabled = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                return null;
            case (_, "VISIBLE", AutomationInvocation.Get):
                return View.IsVisible;
            case (_, "VISIBLE", AutomationInvocation.Let):
                View.IsVisible = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                return null;
            case (_, "SETFOCUS", AutomationInvocation.Method):
                View.Focus();
                return null;
            default:
                throw UnknownName($"{Class}.{member}");
        }
    }

    public override string ToString() => $"{Class.Name} {Name}";
}

/// <summary>
/// A UserForm, in an Avalonia window of its own.
/// </summary>
internal sealed class SpikeForm : SpikeObject
{
    private readonly Dictionary<string, SpikeControl> _controls = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherFrame? _modal;
    private bool _destroying;

    public SpikeForm(FormDefinition definition)
    {
        Name = definition.Name;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 6 };
        foreach (var descriptor in definition.Controls)
        {
            panel.Children.Add(Build(descriptor));
        }

        Window = new Window
        {
            Title = definition.Caption ?? definition.Name,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        // the close button asks the program, which may cancel (QueryClose) and unloads the form; the window is the program's to close.
        Window.Closing += (_, e) =>
        {
            if (_destroying)
            {
                return;
            }

            e.Cancel = true;
            Events.Raise(this, FormWindowEvents.Close, []);
        };
    }

    public string Name { get; }

    public Window Window { get; }

    public override string ClassName => "MSForms.UserForm";

    public IReadOnlyDictionary<string, SpikeControl> Controls => _controls;

    private Control Build(FormControlDescriptor descriptor)
    {
        if (descriptor.Children.Count > 0)
        {
            var inner = new StackPanel { Spacing = 4 };
            foreach (var child in descriptor.Children)
            {
                inner.Children.Add(Build(child));
            }

            return new Border
            {
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = Avalonia.Media.Brushes.Gray,
                Padding = new Avalonia.Thickness(6),
                Child = new StackPanel { Children = { new TextBlock { Text = descriptor.Name }, inner } },
            };
        }

        var control = new SpikeControl(descriptor);
        _controls[descriptor.Name] = control;
        return control.View;
    }

    public bool IsShown => Window.IsVisible;

    public void Show(bool modal)
    {
        Window.Show();
        Trace.Line($"form {Name} shown modal={modal}");
        if (!modal)
        {
            return;
        }

        // a modal Show waits until the form is hidden: the UI thread is pumped meanwhile, and the user's input is not held.
        Script.RunOn(this);
        var depth = Calls.Depth;
        var held = Events.IsHeld;
        Calls.Depth = 0;
        Events.IsHeld = false;
        Events.ReleaseHeld();
        _modal = new DispatcherFrame();
        Dispatcher.UIThread.PushFrame(_modal);
        _modal = null;
        Calls.Depth = depth;
        Events.IsHeld = held;
        Trace.Line($"form {Name}: modal Show returns");
    }

    public void Hide()
    {
        Window.Hide();
        Trace.Line($"form {Name} hidden");
        if (_modal is { } modal)
        {
            modal.Continue = false;
        }
    }

    public void Destroy()
    {
        Hide();
        _destroying = true;
        Window.Close();
        Trace.Line($"form {Name} destroyed");
    }

    public override object? Invoke(string member, AutomationInvocation invocation, object?[] arguments)
    {
        if (invocation == AutomationInvocation.Get && arguments.Length == 0 && _controls.TryGetValue(member, out var control))
        {
            return control;
        }

        return (member.ToUpperInvariant(), invocation) switch
        {
            ("CAPTION", AutomationInvocation.Get) => Window.Title ?? "",
            ("CAPTION", AutomationInvocation.Let) => Set(() => Window.Title = Convert.ToString(arguments[^1], CultureInfo.InvariantCulture)),
            ("INSIDEWIDTH", AutomationInvocation.Get) => (float)Window.ClientSize.Width,
            ("INSIDEHEIGHT", AutomationInvocation.Get) => (float)Window.ClientSize.Height,
            _ => throw UnknownName($"UserForm.{member}"),
        };
    }

    private static object? Set(Action set)
    {
        set();
        return null;
    }

    public override string ToString() => $"UserForm {Name}";
}

/// <summary>
/// The forms of the process: Avalonia windows.
/// </summary>
internal sealed class AvaloniaFormHost(AvaloniaAutomationServer server) : IFormHost
{
    public bool IsAvailable => true;

    public object Create(FormDefinition form) => Calls.Counted(() =>
    {
        var created = new SpikeForm(form);
        Trace.Line($"forms/create {form.Name}: {string.Join(", ", created.Controls.Values)}");
        server.Forms.Add(created);
        return (object)created;
    });

    public void Show(object form, bool modal) => Calls.Counted(() => Of(form).Show(modal));

    public void Hide(object form) => Calls.Counted(() => Of(form).Hide());

    public bool IsShown(object form) => Calls.Counted(() => Of(form).IsShown);

    public string GetCaption(object form) => Calls.Counted(() => Of(form).Window.Title ?? "");

    public void SetCaption(object form, string caption) => Calls.Counted(() => Of(form).Window.Title = caption);

    public void Leave(object form) => Calls.Counted(() => Trace.Line($"forms/leave {Of(form).Name}"));

    public void Destroy(object form) => Calls.Counted(() =>
    {
        if (form is SpikeForm spike && server.Forms.Remove(spike))
        {
            spike.Destroy();
        }
    });

    public void Hold(bool running) => Ui.Run(() =>
    {
        Trace.Line($"forms/hold {running}");
        Events.IsHeld = running;
        if (!running)
        {
            Events.ReleaseHeld();
        }
    });

    public void DoEvents() => Ui.Run(() =>
    {
        Trace.Line("forms/doEvents");
        var held = Events.IsHeld;
        Events.IsHeld = false;
        Events.ReleaseHeld();
        Ui.Pump(TimeSpan.FromMilliseconds(1));
        Events.IsHeld = held;
    });

    private static SpikeForm Of(object form) => form as SpikeForm ?? throw new AutomationException(unchecked((int)0x80070057), "Not a form of this host.");
}

/// <summary>
/// The automation server whose objects the forms and their controls are.
/// </summary>
internal sealed class AvaloniaAutomationServer : IAutomationServer
{
    public HashSet<SpikeForm> Forms { get; } = [];

    public bool IsAvailable => true;

    public object CreateObject(string progId) => throw new AutomationException(unchecked((int)0x80040154), $"{progId}: this host makes no objects but forms.");

    public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        => Calls.Counted(() =>
        {
            var answer = Of(target).Invoke(member, invocation, arguments);
            Trace.Line($"invoke {target}.{member} {invocation} [{string.Join(", ", arguments)}] -> {answer}");
            return answer;
        });

    public string? ClassNameOf(object target) => Of(target).ClassName;

    public bool? IsInstanceOf(object target, string library, string className)
        => string.Equals(Of(target).ClassName, $"{library}.{className}", StringComparison.OrdinalIgnoreCase);

    public bool MoveNext(object enumerator, out object? current) => throw new AutomationException(unchecked((int)0x80004001), "No enumerator here.");

    public void Reset(object enumerator) => throw new AutomationException(unchecked((int)0x80004001), "No enumerator here.");

    public void Advise(object source, IAutomationEventSink sink) => Ui.Run(() =>
    {
        Of(source).Sink = sink;
        Trace.Line($"advise {source}");
    });

    public void Unadvise(object source) => Ui.Run(() => Of(source).Sink = null);

    public void Release(object handle) => Ui.Run(() =>
    {
        Trace.Line($"release {handle}");
        if (handle is SpikeForm form && Forms.Remove(form))
        {
            form.Destroy();
        }
    });

    private static SpikeObject Of(object target) => target as SpikeObject ?? throw new AutomationException(unchecked((int)0x80070057), "Not an object of this host.");
}

/// <summary>
/// Scripted input, for a run nobody watches: <c>RDFORMS_SPIKE_SCRIPT="type AmountBox abc; click OkButton"</c> is played into the first form shown modal.
/// </summary>
internal static class Script
{
    private static bool _played;

    public static void RunOn(SpikeForm form)
    {
        if (_played || Environment.GetEnvironmentVariable("RDFORMS_SPIKE_SCRIPT") is not { Length: > 0 } script)
        {
            return;
        }

        _played = true;
        var steps = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var step in steps)
            {
                var parts = step.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                Trace.Line($"script: {step}");
                var control = form.Controls[parts[1]].View;
                switch (parts[0])
                {
                    case "type":
                        control.Focus();
                        foreach (var typed in parts[2])
                        {
                            control.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = typed.ToString() });
                        }

                        break;
                    case "click":
                        control.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                        break;
                    case "close":
                        form.Window.Close();
                        break;
                }
            }
        }, DispatcherPriority.Background);
    }
}

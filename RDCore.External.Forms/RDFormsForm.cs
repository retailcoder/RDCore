using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.Forms.Avalonia.Controls;
using RDCore.Forms.Avalonia.Rendering;
using RDCore.Forms.Model;
using Model = RDCore.Forms.Model;

namespace RDCore.External.Forms.RDForms;

// the handle of one of a form's components, the form itself included: the object the automation server answers for, one to a component, so that a program that
// asks for a control twice gets the same object.
internal sealed class RDFormsObject(RDFormsForm form, ComponentInstance component)
{
    public RDFormsForm Form { get; } = form;

    public ComponentInstance Component { get; } = component;

    public bool IsForm => ReferenceEquals(Component, Form.Model.Root);

    public override string ToString() => Component.ToString();
}

// a form hosted in an RDForms window: its model, its window, the handles of its components, and where their events go. Made and used on the UI thread only.
internal sealed class RDFormsForm : IComponentEventSink
{
    private readonly Dictionary<ComponentInstance, RDFormsObject> _handles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ComponentInstance, IAutomationEventSink> _sinks = new(ReferenceEqualityComparer.Instance);
    private DispatcherFrame? _modal;

    public RDFormsForm(Model.FormDefinition model)
    {
        Model = model;
        Window = new VBFormWindow(model) { UnloadRequestHandler = AskToUnload };
        VBControl.SetEventSink(Window, this);
        Handle = HandleOf(model.Root);
    }

    public Model.FormDefinition Model { get; }

    public VBFormWindow Window { get; }

    public RDFormsObject Handle { get; }

    public bool IsDestroyed { get; private set; }

    public RDFormsObject HandleOf(ComponentInstance component)
    {
        if (!_handles.TryGetValue(component, out var handle))
        {
            handle = new RDFormsObject(this, component);
            _handles[component] = handle;
        }

        return handle;
    }

    public void Advise(ComponentInstance source, IAutomationEventSink sink) => _sinks[source] = sink;

    public void Unadvise(ComponentInstance source) => _sinks.Remove(source);

    // shown modal: the other windows of the form host wait, and so does the call, on a frame of the UI thread's own that hiding the form ends.
    public void ShowModal(IEnumerable<RDFormsForm> others)
    {
        var waiting = others.Where(other => other.Window.IsEnabled).ToList();
        foreach (var other in waiting)
        {
            other.Window.IsEnabled = false;
        }

        try
        {
            // the window raises its first Resize inside Show, and a handler of it can hide the form before the frame is there to end.
            var frame = new DispatcherFrame();
            _modal = frame;
            Window.Show();
            if (frame.Continue && Window.IsVisible)
            {
                Dispatcher.UIThread.PushFrame(frame);
            }
        }
        finally
        {
            _modal = null;
            foreach (var other in waiting)
            {
                other.Window.IsEnabled = true;
            }
        }
    }

    public void Hide()
    {
        Window.Hide();
        EndModal();
    }

    public void Destroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        IsDestroyed = true;
        EndModal();
        _sinks.Clear();
        VBControl.SetEventSink(Window, null);

        // closed from code, the window raises nothing: the program has unloaded the form already.
        Window.Close();
    }

    private void EndModal()
    {
        if (_modal is { } frame)
        {
            frame.Continue = false;
        }
    }

    // an event of one of the form's components, raised on the UI thread: the sink of the component hears it, and what its handlers left in the arguments is what the
    // control reads back.
    public void Raise(ComponentInstance source, EventDefinition definition, FormValue[] arguments)
    {
        if (!_sinks.TryGetValue(source, out var sink))
        {
            return;
        }

        var raised = arguments.Select(argument => RDFormsValues.ToNeutral(argument, HandleOf)).ToArray();
        sink.OnEvent(new AutomationEvent(definition.Name, raised, RDFormsUI.IsInCall, _ => RDFormsUI.Pump()));
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = RDFormsValues.FromNeutral(raised[index], arguments[index]);
        }
    }

    // the window was asked to close, by its user, the desktop or its owner (the UnloadMode says which): closing a form is the program's to decide and to do, as Unload
    // does, so the window stays open and raises nothing, and the program is asked. A form no program listens to has nothing to ask, and closes.
    private void AskToUnload(int mode)
    {
        if (IsDestroyed)
        {
            return;
        }

        if (!_sinks.TryGetValue(Model.Root, out var sink))
        {
            Window.Close();
            return;
        }

        sink.OnEvent(new AutomationEvent(FormWindowEvents.Close, [(short)mode], RDFormsUI.IsInCall, _ => RDFormsUI.Pump()));
    }
}

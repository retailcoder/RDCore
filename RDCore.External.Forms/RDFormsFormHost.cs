using RDCore.External.Automation;
using RDCore.Forms.Import.VB6;
using RDCore.Forms.Model;
using RDCore.Forms.Model.VB;
using RDCore.SDK.Workspace.Forms;
using Model = RDCore.Forms.Model;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// Hosts Visual Basic 6.0 forms in RDForms windows: each form's model is read from its designer's text and its <c>.frx</c>, and is an object of an
/// <see cref="RDFormsAutomationServer"/>.
/// </summary>
/// <remarks>
/// The window raises the events of the form and of its controls through the server's sinks. A user's close does not close it: the form's sink is asked to
/// (<see cref="FormWindowEvents.Close"/>), and the program unloads the form, or does not.
/// </remarks>
/// <param name="server">The server the forms are objects of.</param>
public sealed class RDFormsFormHost(RDFormsAutomationServer server) : IFormHost
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int Failed = unchecked((int)0x80004005);

    // the forms that are not destroyed, which a modal one keeps from being used. Touched on the UI thread only.
    private readonly List<RDFormsForm> _forms = [];

    /// <summary>
    /// The server the forms are objects of.
    /// </summary>
    public RDFormsAutomationServer Server { get; } = server;

    /// <summary>
    /// Whether a form is one of this host's.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    public bool Owns(object form) => Server.Owns(form);

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public object Create(FormDefinition form)
    {
        if (form.Kind != FormDesignerKind.VisualBasic6)
        {
            throw new AutomationException(ClassNotRegistered, $"{form.Name}: RDForms hosts Visual Basic 6.0 forms only.");
        }

        var root = RootOf(form);
        RDFormsUI.Start(Server.Platform);
        return RDFormsUI.Run(() =>
        {
            var hosted = new RDFormsForm(new Model.FormDefinition(root));
            _forms.Add(hosted);
            return hosted.Handle;
        });
    }

    // the form's model: the one its designer's text declares, or, with no text, an empty form with the caption its designer gives it.
    private static ComponentInstance RootOf(FormDefinition form)
    {
        if (form.DesignerText is not { } text)
        {
            return new ComponentInstance(VBClasses.Form, form.Name).Set(VBProperties.Caption, form.Caption ?? form.Name);
        }

        var import = VB6FormReader.Read(text, form.State.IsEmpty ? null : form.State.ToArray());
        if (import.Form is not { } model)
        {
            var errors = import.Diagnostics.Where(diagnostic => diagnostic.Severity == VB6ImportSeverity.Error).Select(diagnostic => diagnostic.ToString());
            throw new AutomationException(Failed, $"{form.Name}: its designer cannot be read. {string.Join(" ", errors)}");
        }

        if (!ReferenceEquals(model.Root.Class, VBClasses.Form))
        {
            throw new AutomationException(ClassNotRegistered, $"{form.Name}: RDForms cannot host a {model.Root.Class.TypeName} yet.");
        }

        return model.Root;
    }

    /// <inheritdoc/>
    public void Show(object form, bool modal)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            if (modal)
            {
                hosted.ShowModal(_forms.Where(other => !ReferenceEquals(other, hosted)));
            }
            else
            {
                hosted.Window.Show();
            }
        });
    }

    /// <inheritdoc/>
    public void Hide(object form)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            hosted.Hide();
        });
    }

    /// <inheritdoc/>
    public bool IsShown(object form)
    {
        var hosted = FormOf(form);
        return RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            return hosted.Window.IsVisible;
        });
    }

    /// <inheritdoc/>
    public string GetCaption(object form)
    {
        var hosted = FormOf(form);
        return RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            return hosted.Model.Root.Get(VBProperties.Caption);
        });
    }

    /// <inheritdoc/>
    public void SetCaption(object form, string caption)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            hosted.Model.Root.Set(VBProperties.Caption, caption);
        });
    }

    /// <inheritdoc/>
    public void Leave(object form)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            hosted.Hide();
        });
    }

    /// <inheritdoc/>
    public void Destroy(object form)
    {
        if (form is not RDFormsObject { IsForm: true } handle)
        {
            return;
        }

        RDFormsUI.Run(() =>
        {
            handle.Form.Destroy();
            _ = _forms.Remove(handle.Form);
        });
    }

    /// <inheritdoc/>
    public void DoEvents()
    {
        if (_forms.Count > 0)
        {
            RDFormsUI.Run(RDFormsUI.Pump);
        }
    }

    private static RDFormsForm FormOf(object form)
        => form is RDFormsObject { IsForm: true } handle ? handle.Form : throw new AutomationException(unchecked((int)0x80020005), "The object is not a form of RDForms.");
}

using RDCore.External.Automation;
using RDCore.Forms.Model;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// The automation server of the RDForms forms a <see cref="RDFormsFormHost"/> hosts: a form and each of its controls is an object of it, whose properties are the
/// properties of its RDForms component and whose controls are reached by their names.
/// </summary>
/// <remarks>
/// It creates nothing by name: a form is created by its form host, and its controls with it. Its objects are called on the RDForms UI thread, which every call is
/// made on, inline when it comes from that thread already, as the calls the handlers of an event make do.
/// </remarks>
public sealed class RDFormsAutomationServer : IAutomationServer
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int NotImplemented = unchecked((int)0x80004001);
    private const string VisualBasicLibrary = "VB";

    /// <summary>
    /// Creates the server of the forms of a process, whose windows are on <paramref name="platform"/>.
    /// </summary>
    /// <param name="platform">
    /// Where the windows are. The process's first form starts the RDForms UI thread on it, once a process: a server of the same process that asks for the other
    /// platform cannot create a form.
    /// </param>
    public RDFormsAutomationServer(RDFormsPlatform platform)
    {
        Platform = platform;
    }

    /// <summary>
    /// Where the windows of the forms are.
    /// </summary>
    public RDFormsPlatform Platform { get; }

    /// <summary>
    /// Whether an object is one of this server's: a form, or one of its controls.
    /// </summary>
    /// <param name="handle">The handle of the object.</param>
    public bool Owns(object handle) => handle is RDFormsObject;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public object CreateObject(string progId) => throw new AutomationException(ClassNotRegistered, $"{progId}: RDForms creates no object by name.");

    /// <inheritdoc/>
    public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
    {
        var handle = HandleOf(target);
        return RDFormsUI.Run(() =>
        {
            Alive(handle);
            var component = handle.Component;
            if (component.Class.Property(member) is { } property)
            {
                return invocation switch
                {
                    AutomationInvocation.Let or AutomationInvocation.Set => Assign(component, property, arguments, culture),
                    _ when arguments.Length == 0 => Read(component, property),
                    _ => throw new AutomationException(BadParameterCount, $"{handle}.{member} takes no argument."),
                };
            }

            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && arguments.Length == 0
                && Control(handle, member) is { } control)
            {
                return control;
            }

            throw new AutomationException(UnknownName, $"{handle} has no member named {member}.");
        });
    }

    // a control of the form, by its name: a control that is an element of an array is reached through the array, which a name alone is not.
    private static RDFormsObject? Control(RDFormsObject handle, string name)
        => handle.IsForm && handle.Form.Model.Find(name) is { } control && !ReferenceEquals(control, handle.Component) ? handle.Form.HandleOf(control) : null;

    private static object? Read(ComponentInstance component, PropertyDefinition property)
        => RDFormsValues.TryToNeutral(component.GetBoxed(property), out var value)
            ? value
            : throw new AutomationException(NotImplemented, $"{component}.{property.Name}: a value of type {property.ValueType.Name} cannot be given to a program.");

    private static object? Assign(ComponentInstance component, PropertyDefinition property, object?[] arguments, CultureInfo culture)
    {
        if (arguments.Length != 1)
        {
            throw new AutomationException(BadParameterCount, $"{component}.{property.Name} takes no argument.");
        }

        var value = RDFormsValues.FromNeutral(arguments[0], property.ValueType, culture);
        if (!component.TrySet(property, value))
        {
            throw new AutomationException(RDFormsValues.InvalidPropertyValue, "Invalid property value");
        }

        return null;
    }

    /// <inheritdoc/>
    public string? ClassNameOf(object target) => HandleOf(target).Component.Class.TypeName;

    /// <inheritdoc/>
    public bool? IsInstanceOf(object target, string library, string className)
        => string.Equals(library, VisualBasicLibrary, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(HandleOf(target).Component.Class.ClassName, className, StringComparison.OrdinalIgnoreCase)
            : null;

    /// <inheritdoc/>
    public bool MoveNext(object enumerator, out object? current)
        => throw new AutomationException(NotImplemented, "RDForms objects have no enumerator.");

    /// <inheritdoc/>
    public void Reset(object enumerator) => throw new AutomationException(NotImplemented, "RDForms objects have no enumerator.");

    /// <inheritdoc/>
    public void Advise(object source, IAutomationEventSink sink)
    {
        var handle = HandleOf(source);
        RDFormsUI.Run(() => handle.Form.Advise(handle.Component, sink));
    }

    /// <inheritdoc/>
    public void Unadvise(object source)
    {
        var handle = HandleOf(source);
        RDFormsUI.Run(() => handle.Form.Unadvise(handle.Component));
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // a form's objects are the form's, and go with it when its host destroys it.
    }

    internal static RDFormsObject HandleOf(object target)
        => target as RDFormsObject ?? throw new AutomationException(unchecked((int)0x80020005), "The object is not one of RDForms.");

    internal static void Alive(RDFormsObject handle)
    {
        if (handle.Form.IsDestroyed)
        {
            throw new AutomationException(unchecked((int)0x80010108), $"{handle}: its form was destroyed.");
        }
    }
}

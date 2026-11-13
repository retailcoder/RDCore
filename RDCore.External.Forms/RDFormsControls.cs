using RDCore.External.Automation;
using RDCore.Forms.Model;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// a VB6 form's Controls collection: every component placed on the form, a menu and its items included, in the order the form's file holds them, depth first.
// Item, its default member, takes a position from 0 or a control's name; _NewEnum enumerates the controls the form has when the loop starts. Used on the UI
// thread only.
internal sealed class RDFormsControls(RDFormsForm form)
{
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int TypeMismatch = unchecked((int)0x80020005);
    private const int BadIndex = unchecked((int)0x8002000B);
    private const int ControlNotFound = unchecked((int)0x800A02DA);

    // the names a late-bound call gives the default member and the enumeration member: DISPID_VALUE and DISPID_NEWENUM.
    private const string DefaultMember = "[DISPID=0]";
    private const string EnumerationMember = "[DISPID=-4]";

    public RDFormsForm Form { get; } = form;

    public object? Invoke(string member, AutomationInvocation invocation, object?[] arguments, CultureInfo culture)
    {
        if (invocation is AutomationInvocation.Get or AutomationInvocation.Method)
        {
            if (Is(member, "Count"))
            {
                return arguments.Length == 0 ? (short)Controls().Count : throw new AutomationException(BadParameterCount, $"{this}.Count takes no argument.");
            }

            if (Is(member, "Item") || Is(member, DefaultMember))
            {
                return arguments.Length == 1
                    ? Form.HandleOf(Item(arguments[0], culture))
                    : throw new AutomationException(BadParameterCount, $"{this}.Item takes one argument.");
            }

            if (Is(member, "_NewEnum") || Is(member, EnumerationMember))
            {
                return new Enumerator([.. Controls().Select(Form.HandleOf)]);
            }
        }

        throw new AutomationException(UnknownName, $"{this} has no member named {member}.");
    }

    public override string ToString() => $"{Form.Handle}.Controls";

    private static bool Is(string member, string name) => string.Equals(member, name, StringComparison.OrdinalIgnoreCase);

    // the form's components after the form itself, which comes first among them.
    private List<ComponentInstance> Controls() => [.. Form.Model.Components.Skip(1)];

    // a control by its name, or by its position: a number, or a String that reads as one, rounded to even. Null is a type mismatch.
    private ComponentInstance Item(object? index, CultureInfo culture)
    {
        if (index is string name)
        {
            if (Form.Model.Find(name, null) is { } named && !ReferenceEquals(named, Form.Model.Root))
            {
                return named;
            }

            if (!double.TryParse(name, NumberStyles.Float, culture, out _))
            {
                throw new AutomationException(ControlNotFound, $"Control '{name}' not found");
            }
        }

        if (index is DBNull)
        {
            throw new AutomationException(TypeMismatch, "Type mismatch");
        }

        var controls = Controls();
        var position = Math.Round(RDFormsValues.DoubleOf(index, culture), MidpointRounding.ToEven);
        return position >= 0 && position < controls.Count
            ? controls[(int)position]
            : throw new AutomationException(BadIndex, $"{this}: there is no control at {position}.");
    }

    // the controls the form had when the loop started, each in turn.
    internal sealed class Enumerator(RDFormsObject[] controls)
    {
        private int _next;

        public bool MoveNext(out object? current)
        {
            current = _next < controls.Length ? controls[_next++] : null;
            return current is not null;
        }

        public void Reset() => _next = 0;
    }
}

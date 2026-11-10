using Avalonia;
using Avalonia.Themes.Simple;

namespace RDCore.External.Forms.RDForms;

// the Avalonia application the RDForms windows run in: the controls' templates come from its theme.
internal sealed class RDFormsApplication : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());
}

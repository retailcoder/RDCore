using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// The forms a session has loaded, as its host tells a debugger of them. A program that completes with a form shown leaves it to the person using it, and the
/// handlers of its events run in the session until it is unloaded, as in MS-VBA.
/// </summary>
public static class LoadedForms
{
    /// <summary>
    /// The number of forms the session has loaded.
    /// </summary>
    /// <param name="session">The session.</param>
    public static int Count(IRuntimeSession session) => SessionForms.Of(session).Loaded.Count;

    /// <summary>
    /// Whether a form the session has loaded is shown.
    /// </summary>
    /// <param name="session">The session.</param>
    public static bool AnyShown(IRuntimeSession session) => SessionForms.Of(session).AnyShown(session);

    /// <summary>
    /// Has <paramref name="noneLoaded"/> called each time the session's last loaded form goes: unloaded, let go of, or forgotten by an <c>End</c>.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="noneLoaded">What is called, on the thread that let the form go.</param>
    public static void WhenNoneLoaded(IRuntimeSession session, Action noneLoaded) => SessionForms.Of(session).NoneLoaded = noneLoaded;
}

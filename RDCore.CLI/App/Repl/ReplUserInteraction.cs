using RDCore.CLI.App.Console;
using RDCore.External.Interaction;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The dialogs of a program run from the shell, asked at the shell's console: a <c>MsgBox</c> is its message and a row of buttons (<see cref="ConsoleMenu"/>), and an
/// <c>InputBox</c> its message and a line to type on (<see cref="ConsoleLineEditor"/>).
/// </summary>
/// <remarks>
/// <para>
/// A <c>MsgBox</c> starts with its default button selected, and the button chosen is the answer. <kbd>Esc</kbd> closes it the way its close button would - the answer
/// is <c>vbCancel</c>, or <c>vbOK</c> for a message with only that button - and does nothing for one that has no way to be closed but to choose
/// (<c>vbYesNo</c>, <c>vbAbortRetryIgnore</c>).
/// </para>
/// <para>
/// An <c>InputBox</c> starts with its default text, selected; <kbd>Enter</kbd> confirms what is typed, and <kbd>Esc</kbd> cancels, which the program is told with
/// <c>vbNullString</c> and not with the zero-length string of a line confirmed with nothing on it.
/// </para>
/// <para>
/// A shell whose input is a script reads a line instead of keys: for a <c>MsgBox</c>, the name or the key of a button (an empty line is the default button, and the end of
/// the input closes it as <kbd>Esc</kbd> would), and for an <c>InputBox</c> the text (the end of the input cancels it).
/// </para>
/// </remarks>
/// <param name="keys">Where the keys are read.</param>
/// <param name="output">Where the dialogs are drawn.</param>
/// <param name="ansi">Whether <paramref name="output"/> understands ANSI escape sequences, which show the selected default text of an input box.</param>
public sealed class ReplUserInteraction(IConsoleKeys keys, TextWriter output, bool ansi = false) : IUserInteraction
{
    private const string Indent = "  ";

    // what a button is called and which key chooses it, as a Windows dialog has it.
    private static string LabelOf(VBMsgBoxResult choice) => choice switch
    {
        VBMsgBoxResult.VBOk => "&OK",
        VBMsgBoxResult.VBCancel => "&Cancel",
        VBMsgBoxResult.VBAbort => "&Abort",
        VBMsgBoxResult.VBRetry => "&Retry",
        VBMsgBoxResult.VBIgnore => "&Ignore",
        VBMsgBoxResult.VBYes => "&Yes",
        VBMsgBoxResult.VBNo => "&No",
        _ => choice.ToString(),
    };

    private static string GlyphOf(VBMsgBoxStyle icon) => icon switch
    {
        VBMsgBoxStyle.VBCritical => "⛔ ",
        VBMsgBoxStyle.VBQuestion => "❓ ",
        VBMsgBoxStyle.VBExclamation => "⚠️ ",
        VBMsgBoxStyle.VBInformation => "ℹ️ ",
        _ => string.Empty,
    };

    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        WriteHeader(GlyphOf(request.Icon) + request.Title, request.Prompt);

        var labels = request.Choices.Select(LabelOf).ToArray();
        var menu = new ConsoleMenu(labels, request.DefaultChoice, canDismiss: request.EscapeResult is not null);
        if (!keys.IsInteractive)
        {
            return AnswerFromLine(request, menu);
        }

        var chosen = menu.Run(keys, output, Indent, token);
        output.WriteLine();
        return chosen is { } index ? request.Choices[index] : request.EscapeResult!.Value;
    }

    // a line of a script names a button by its text or its key; anything else is asked again, until the script runs out.
    private VBMsgBoxResult AnswerFromLine(MessageBoxRequest request, ConsoleMenu menu)
    {
        output.WriteLine(Indent + menu.Render());
        while (true)
        {
            var line = keys.ReadLine(CancellationToken.None)?.Trim();
            if (line is null)
            {
                return request.EscapeResult ?? request.Choices[request.DefaultChoice];
            }

            if (line.Length == 0)
            {
                return request.Choices[request.DefaultChoice];
            }

            var index = Array.FindIndex(menu.Items.ToArray(), item =>
                string.Equals(ConsoleMenu.TextOf(item), line, StringComparison.OrdinalIgnoreCase)
                || line.Length == 1 && ConsoleMenu.KeyOf(item) == char.ToUpperInvariant(line[0]));
            if (index >= 0)
            {
                return request.Choices[index];
            }
        }
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default)
    {
        WriteHeader(request.Title, request.Prompt);
        if (!keys.IsInteractive)
        {
            output.Write($"{Indent}> ");
            output.Flush();
            return keys.ReadLine(token);
        }

        var answer = new ConsoleLineEditor(request.Default).Run(keys, output, $"{Indent}> ", ansi, token);
        output.WriteLine();
        return answer;
    }

    private void WriteHeader(string title, string prompt)
    {
        output.WriteLine();
        if (title.Length > 0)
        {
            output.WriteLine(title);
        }

        foreach (var line in prompt.ReplaceLineEndings("\n").Split('\n'))
        {
            output.WriteLine(Indent + line);
        }
    }
}

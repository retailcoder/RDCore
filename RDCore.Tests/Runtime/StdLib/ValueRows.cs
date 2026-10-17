using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// Evaluates rows of expressions in one program, through the whole platform, and compares what each gave with what MS-VBA gave for it.
/// </summary>
/// <remarks>
/// A row is <c>expression => expected</c>, where what an expression gives is its <c>TypeName</c> and its value (a <c>Date</c> as its serial number), or
/// <c>err</c> and the number of the error it raised. Every row is evaluated after clearing the error the one before it may have left.
/// </remarks>
internal static class ValueRows
{
    private const string Shown = """
        Private Function Shown(ByVal v As Variant) As String
            If IsNull(v) Then
                Shown = "Null"
            ElseIf IsEmpty(v) Then
                Shown = "Empty"
            ElseIf VarType(v) = vbDate Then
                Shown = CStr(CDbl(v))
            Else
                Shown = CStr(v)
            End If
        End Function
        """;

    /// <summary>
    /// Evaluates <paramref name="rows"/> in a workspace of <paramref name="classes"/>, and asserts each gave what it is expected to.
    /// </summary>
    /// <param name="classes">The class modules the expressions may use.</param>
    /// <param name="declarations">Module-level declarations the expressions may use.</param>
    /// <param name="rows">The rows: <c>expression => expected</c>.</param>
    public static async Task AssertRows(IReadOnlyList<(string Name, string Source)> classes, string declarations, params string[] rows)
    {
        var cases = rows.Select(row => row.Split(" => ")).ToArray();
        var body = cases.SelectMany(row => new[]
        {
            "    Err.Clear: v = Empty",
            $"    v = {row[0]}",
            "    If Err.Number <> 0 Then Debug.Print \"err \" & Err.Number Else Debug.Print TypeName(v) & \" \" & Shown(v)",
        });

        var printed = await RunAsync(classes,
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\n{Shown}\r\nPublic Sub Main()\r\n    Dim v As Variant\r\n    On Error Resume Next\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n".ReplaceLineEndings("\r\n"));

        Assert.HasCount(cases.Length, printed, string.Join(" | ", printed));
        var mismatches = cases.Zip(printed)
            .Where(pair => pair.First[1] != pair.Second)
            .Select(pair => $"{pair.First[0]}: expected [{pair.First[1]}], got [{pair.Second}]")
            .ToArray();
        Assert.IsEmpty(mismatches, string.Join("\n", mismatches));
    }

    /// <inheritdoc cref="AssertRows(IReadOnlyList{ValueTuple{string, string}}, string, string[])"/>
    public static Task AssertRows(params string[] rows) => AssertRows([], string.Empty, rows);
}

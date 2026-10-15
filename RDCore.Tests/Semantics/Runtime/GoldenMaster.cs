using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Facts;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A golden master: what a broad set of operations does - what each yields or raises, and the facts an analysis is told of it - recorded once and compared
/// line for line with what the operations do now. A line that differs is a change in behaviour.
/// </summary>
/// <remarks>
/// A snapshot is a file in <c>GoldenMasters</c>, next to the test that records it: a line to an operation, compressed (there are thousands of them). A change
/// that is meant is recorded by running the test with <c>RDCORE_UPDATE_GOLDEN_MASTERS=1</c>; the test prints the lines that differ when it fails, which is how
/// a change to a snapshot is reviewed.
/// </remarks>
internal static class GoldenMaster
{
    private const string UpdateVariable = "RDCORE_UPDATE_GOLDEN_MASTERS";

    /// <summary>
    /// Asserts that the operations do what the snapshot recorded them to do, or records what they do when asked to.
    /// </summary>
    /// <param name="actual">What the operations do, a line to an operation.</param>
    /// <param name="name">The name of the snapshot, in the <c>GoldenMasters</c> folder next to the test.</param>
    /// <param name="source">The test's own file, which the snapshot is kept next to.</param>
    public static void AssertMatches(string actual, string name, [CallerFilePath] string source = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(source)!, "GoldenMasters", name);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = File.Create(path))
            using (var compressed = new GZipStream(file, CompressionLevel.SmallestSize))
            {
                compressed.Write(Encoding.UTF8.GetBytes(actual));
            }

            Assert.Inconclusive($"The golden master was recorded: {path}");
        }

        Assert.IsTrue(File.Exists(path), $"No golden master at {path}: run with {UpdateVariable}=1 to record one.");
        string[] expected;
        using (var file = File.OpenRead(path))
        using (var compressed = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(compressed, Encoding.UTF8))
        {
            expected = reader.ReadToEnd().Split('\n');
        }

        var lines = actual.Split('\n');

        var differences = expected.Zip(lines).Where(pair => pair.First != pair.Second).Take(20)
            .Select(pair => $"- {pair.First}{Environment.NewLine}+ {pair.Second}").ToArray();
        Assert.IsEmpty(differences, $"The operators no longer do what they were recorded to do:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
        Assert.HasCount(expected.Length, lines, "as many operations as were recorded");
    }

    /// <summary>
    /// A value as a snapshot writes it: its type, and what it holds.
    /// </summary>
    public static string Describe(VBTypedValue value) => value switch
    {
        { IsIndeterminate: true } => $"{value.TypeInfo.Name}(?)",
        VBVariantValue variant => $"Variant({Describe(variant.TypedValue)})",
        VBNullValue or VBEmptyValue => value.TypeInfo.Name,
        VBNothingValue => "Nothing",
        VBObjectValue => "Object",
        VBFixedSizeArrayValue array => $"{array.ItemType.Name}(n)",
        VBArrayValue array => $"{array.ItemType.Name}()",
        // the value of a user-defined type is its fields, which a snapshot does not need to know.
        VBUserDefinedTypeValue => $"{value.TypeInfo.Name}{{}}",
        // a trailing byte is part of the data of a String, and of what a comparison compares.
        VBStringValue text => $"{value.TypeInfo.Name}({Escape(text.Value)}{(text.TrailingByte is { } trailing ? $"+0x{trailing:X2}" : string.Empty)})",
        _ => $"{value.TypeInfo.Name}({Format(value.RuntimeValue.BoxedValue)})",
    };

    /// <summary>
    /// What an operation yields or raises.
    /// </summary>
    public static string Describe(RuntimeSemanticsEvaluationResult result)
        => result.ErrorInfo is { } error ? $"error {error.ErrorId}" : result.Result is { } value ? Describe(value) : "nothing";

    /// <summary>
    /// What an operation yields or raises - or throws, which no operation should: an exception out of the semantics of an operator is a defect, and is
    /// recorded as one.
    /// </summary>
    public static string Outcome(Func<RuntimeSemanticsEvaluationResult> evaluate)
    {
        try
        {
            return Describe(evaluate());
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }

    /// <summary>
    /// What a let-coercion yields or raises - or throws, as <see cref="Outcome(Func{RuntimeSemanticsEvaluationResult})"/> has it.
    /// </summary>
    public static string Outcome(Func<LetCoercionResult> evaluate)
    {
        try
        {
            var result = evaluate();
            return result.ErrorInfo is { } error ? $"error {error.ErrorId}"
                : result.Result is { } value ? Describe(value)
                : result.IsApplicable ? "nothing" : "not applicable";
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }

    /// <summary>
    /// A type as a snapshot writes it: an array by its elements, a fixed-length string by its length, and what declares an Enum, a user-defined type or
    /// a class by its kind.
    /// </summary>
    public static string Describe(VBType type) => type switch
    {
        VBFixedSizeArrayType array => $"{Describe(array.ItemType)}(n)",
        VBArrayType array => $"{Describe(array.ItemType)}()",
        VBFixedStringType fixedString => $"String * {fixedString.Length}",
        VBEnumType => $"Enum {type.Name}",
        VBUserDefinedType => $"Type {type.Name}",
        VBClassType => $"Class {type.Name}",
        _ => type.Name,
    };

    /// <summary>
    /// What an analysis is told of an operation, in the order it is told: the operation, then the conversions of its operands.
    /// </summary>
    public static string Describe(RecordingAnalysisObserver observer)
    {
        var facts = observer.Operations.Select(fact => fact switch
            {
                ArithmeticOperatorFact arithmetic => $"op {Describe(fact)} [{arithmetic.Flags}] known={fact.IsValueKnown} error={fact.Error?.ErrorId}",
                ComparisonOperatorFact comparison => $"op {Describe(fact)} [{comparison.Flags}] {comparison.Comparison.Mode} known={fact.IsValueKnown} error={fact.Error?.ErrorId}",
                LogicalOperatorFact logical => $"op {Describe(fact)} [{logical.Flags}] known={fact.IsValueKnown} error={fact.Error?.ErrorId}",
                ConcatOperatorFact concat => $"op {Describe(fact)} [{concat.Flags}] known={fact.IsValueKnown} error={fact.Error?.ErrorId}",
                _ => $"op {fact.GetType().Name} {Describe(fact)} known={fact.IsValueKnown} error={fact.Error?.ErrorId}",
            })
            .Concat(observer.Conversions.Select(fact =>
                $"conv #{fact.Operand} {fact.Source.Name}->{fact.Destination.Name} [{fact.Flags}] known={fact.IsValueKnown} error={fact.Error?.ErrorId}"));
        return string.Concat(facts.Select(fact => $" | {fact}"));
    }

    private static string Describe(OperatorFact fact) => fact.EffectiveType?.Name ?? "-";

    private static string Format(object? value) => value switch
    {
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => $"{value}",
    };

    // a snapshot is a line to an operation: a control character in a String is written as its code.
    private static string Escape(string text)
        => text.Any(char.IsControl) ? string.Concat(text.Select(c => char.IsControl(c) ? $"\\u{(int)c:X4}" : c.ToString())) : text;
}

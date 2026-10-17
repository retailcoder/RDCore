namespace RDCore.SDK.Runtime;

/// <summary>
/// The regional settings a <c>String</c> is read by when it is Let-coerced to a number or a date: <strong>MS-VBAL §5.5.1.2.4</strong>'s
/// <c>regional-number-string</c>, a number written "according to the active host-defined regional settings", and the window two-digit years are in.
/// </summary>
/// <remarks>
/// These are the platform's, set in its configuration (<see cref="Server.Configuration.SdkEnvironmentOptions.Regional"/>), and not the machine's: a program
/// reads a number the same way wherever it runs, unless the platform is told otherwise. The default is en-US, which is what MS-VBA reads on an en-US machine
/// (recorded in Excel): <c>1,000</c> is a thousand, <c>$1</c> is one, and <c>30</c> is the year 2030.
/// </remarks>
/// <param name="DecimalSymbol">What separates the whole part of a number from its fraction.</param>
/// <param name="DigitGroupingSymbol">What groups the digits of a number; ignored where it is read, after the first digit.</param>
/// <param name="CurrencySymbol">What a number may be written with before or after it, as an amount of money.</param>
/// <param name="TwoDigitYearMax">The last year a year written with one or two digits can be: the hundred years ending with it are the ones such a year is in.</param>
public sealed record class RegionalSettings(string DecimalSymbol, string DigitGroupingSymbol, string CurrencySymbol, int TwoDigitYearMax)
{
    /// <summary>
    /// The settings of en-US: <c>.</c>, <c>,</c>, <c>$</c>, and two-digit years up to 2049, as Windows has them today.
    /// </summary>
    public static RegionalSettings Default { get; } = new(".", ",", "$", 2049);
}

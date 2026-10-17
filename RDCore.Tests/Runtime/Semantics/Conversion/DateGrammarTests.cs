using RDCore.Runtime.Semantics.Conversion;

namespace RDCore.Tests.Runtime.Semantics.Conversion;

/// <summary>
/// <strong>MS-VBAL §3.3.3</strong> the grammar of a date and a time, which a <c>String</c> Let-coerced to a <c>Date</c> is read by. Each expectation is what
/// MS-VBA's <c>CDate</c> gives the same text, recorded in Excel (2026-10-17), the current year being 2026.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.3.3 Date Tokens")]
public sealed class DateGrammarTests
{
    private const int CurrentYear = 2026;

    private static double Read(string text)
    {
        Assert.IsTrue(DateGrammar.TryRead(text, CurrentYear, out var value), $"'{text}' is a date");
        return value.ToOADate();
    }

    [TestMethod]
    [DataRow("1/2", 2026, 1, 2, DisplayName = "No year: the current one")]
    [DataRow("Feb 2000", 2000, 2, 1, DisplayName = "A month name and a number that cannot be a day: the year, on the first")]
    [DataRow("31/12/2000", 2000, 12, 31, DisplayName = "A first number that cannot be a month: the day")]
    [DataRow("1,000", 2000, 1, 1, DisplayName = "A comma is a date separator, and a number that cannot be a day is a year")]
    [DataRow("1/2/30", 2030, 1, 2, DisplayName = "A two-digit year up to 49 is in the 2000s")]
    [DataRow("1/2/29", 2029, 1, 2, DisplayName = "A two-digit year")]
    [DataRow("1/2/50", 1950, 1, 2, DisplayName = "A two-digit year from 50 is in the 1900s")]
    public void ADate_IsTheCalendarDateTheRulesMakeOfIt(string text, int year, int month, int day)
        => Assert.AreEqual(new DateTime(year, month, day).ToOADate(), Read(text));

    [TestMethod]
    [DataRow("2.5", 0.086805555555555552, DisplayName = "A dot is a time separator: 2:05")]
    [DataRow("3 PM", 0.625, DisplayName = "An hour and PM")]
    [DataRow("12 AM", 0d, DisplayName = "12 AM is midnight")]
    [DataRow("2000-01-02 13:30", 36527.5625, DisplayName = "A date and a time")]
    [DataRow("1/1/1800 12:00", -36522.5, DisplayName = "A time of a date before 1899-12-30 is added to the day it is on")]
    public void ADateOrATime_IsTheSerialValueMSVBAGivesIt(string text, double serial)
        => Assert.AreEqual(serial, Read(text), 1e-9);

    [TestMethod]
    public void ATwoDigitYear_IsInTheWindowTheRegionalSettingsSay()
    {
        Assert.IsTrue(DateGrammar.TryRead("1/2/30", CurrentYear, out var value, twoDigitYearMax: 2029));
        Assert.AreEqual(1930, value.Year);
    }
}

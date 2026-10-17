using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using System.Globalization;

namespace RDCore.Tests.Runtime.Semantics.Conversion;

/// <summary>
/// <strong>MS-VBAL §5.5.1.2.4</strong> a <c>String</c> Let-coerced to a number, read as MS-VBA reads it with en-US regional settings: each expectation is what
/// <c>CDbl</c>, <c>CInt</c> and <c>CLng</c> give the same text in Excel (2026-10-17), and <c>13</c> or <c>6</c> is the error they raise.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.4 Let-coercion to and from String")]
public sealed class NumericStringTests
{
    private static string Converted(string text, VBNumericType destination, RegionalSettings? regional = null)
    {
        var result = ValueConversions.ToNumeric(new VBStringValue(text), destination, regional);
        return result.IsSuccess
            ? ((VBNumericTypedValue)result.Value!).AsDouble.ToString(CultureInfo.InvariantCulture)
            : $"err{(int)result.Error!}";
    }

    [TestMethod]
    [DataRow("&H10", "16", "16", "16")]
    [DataRow("&HFFFF", "65535", "-1", "65535")]
    [DataRow("&H7FFF", "32767", "32767", "32767")]
    [DataRow("&H8000", "32768", "-32768", "32768")]
    [DataRow("&H7FFFFFFF", "2147483647", "err6", "2147483647")]
    [DataRow("&H80000000", "-2147483648", "err6", "-2147483648")]
    [DataRow("&HFFFFFFFF", "-1", "-1", "-1")]
    [DataRow("&H100000000", "4294967296", "0", "err6")]
    [DataRow("&O7", "7", "7", "7")]
    [DataRow("&O177777", "65535", "-1", "65535")]
    [DataRow("&h1f", "31", "31", "31")]
    [DataRow("&H10e1", "4321", "4321", "4321")]
    [DataRow(" &H10 ", "16", "16", "16")]
    [DataRow("&H", "err13", "err13", "err13")]
    [DataRow("&HG", "err13", "err13", "err13")]
    [DataRow("&H10&", "err13", "err13", "err13")]
    [DataRow("&O8", "err13", "err13", "err13")]
    [DataRow("-&H10", "err13", "err13", "err13")]
    public void AHexadecimalOrOctalLiteral_IsWhatMSVBAMakesOfItInEachType(string text, string asDouble, string asInteger, string asLong)
    {
        Assert.AreEqual(asDouble, Converted(text, VBDoubleType.TypeInfo), "Double");
        Assert.AreEqual(asInteger, Converted(text, VBIntegerType.TypeInfo), "Integer");
        Assert.AreEqual(asLong, Converted(text, VBLongType.TypeInfo), "Long");
    }

    [TestMethod]
    [DataRow("1,000", "1000")]
    [DataRow("1,0,0", "100")]
    [DataRow("1,", "1")]
    [DataRow("1,000.5", "1000.5")]
    [DataRow("1.000,5", "1.0005")]
    [DataRow("1,000.5,0", "1000.5")]
    [DataRow("1,,0", "10")]
    [DataRow("1-", "-1")]
    [DataRow("1 -", "-1")]
    [DataRow("1.5-", "-1.5")]
    [DataRow("1+", "1")]
    [DataRow("+1", "1")]
    [DataRow("- 1", "-1")]
    [DataRow("(1)", "-1")]
    [DataRow("( 1 )", "-1")]
    [DataRow("($1,000.50)", "-1000.5")]
    [DataRow("$1", "1")]
    [DataRow("1$", "1")]
    [DataRow("-$1", "-1")]
    [DataRow("$-1", "-1")]
    [DataRow("$ 1", "1")]
    [DataRow("1 $", "1")]
    [DataRow("$.5", "0.5")]
    [DataRow("1e3", "1000")]
    [DataRow("1d2", "100")]
    [DataRow("1,000e3", "1000000")]
    [DataRow("$1e3", "1000")]
    [DataRow("1.", "1")]
    public void ARegionalNumberString_IsTheNumberMSVBAReadsItAs(string text, string number)
        => Assert.AreEqual(number, Converted(text, VBDoubleType.TypeInfo));

    [TestMethod]
    [DataRow(",1")]
    [DataRow("(-1)")]
    [DataRow("-(1)")]
    [DataRow("$")]
    [DataRow("1$2")]
    [DataRow("--1")]
    [DataRow(".")]
    [DataRow("(1")]
    [DataRow("1)")]
    [DataRow("()")]
    public void AStringMSVBADoesNotReadAsANumber_IsATypeMismatch(string text)
        => Assert.AreEqual("err13", Converted(text, VBDoubleType.TypeInfo));

    // the platform's regional settings are what a string is read by, whatever the machine's are.
    [TestMethod]
    public void TheRegionalSettingsOfThePlatform_AreWhatAStringIsReadBy()
    {
        var european = new RegionalSettings(",", ".", "€", 2049);
        Assert.AreEqual("1000.5", Converted("1.000,5", VBDoubleType.TypeInfo, european));
        Assert.AreEqual("-2", Converted("-2 €", VBDoubleType.TypeInfo, european));
        Assert.AreEqual("err13", Converted("$1", VBDoubleType.TypeInfo, european));
    }

    [TestMethod]
    public void TheRegionalSettingsOfThePlatform_AreENUSUnlessItIsToldOtherwise()
    {
        var options = new SDK.Server.Configuration.SdkEnvironmentOptions();
        Assert.AreEqual(RegionalSettings.Default, RuntimeEnvironmentProfile.From(options).Regional);
        Assert.AreEqual(new RegionalSettings(".", ",", "$", 2049), RegionalSettings.Default);
    }
}

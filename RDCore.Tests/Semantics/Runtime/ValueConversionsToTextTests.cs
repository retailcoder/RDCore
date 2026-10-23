using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Globalization;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// <see cref="ValueConversions.ToText"/> called directly, the way the explicit-conversion functions call it: the
/// "-&gt; String" conversion (MS-VBAL §5.5.1.2.4, §5.5.1.2.6) keeps every byte of a String's data, whoever asks for it.
/// </summary>
/// <remarks>
/// A conversion is a value in and a value out, so the Let-coercion of an assignment, <c>CStr</c>, and the parentheses of
/// MS-VBAL §5.6.6 all get the same answer from it; these pin that answer once rather than through each of them.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.4 Let-coercion to and from String")]
public sealed class ValueConversionsToTextTests
{
    private static VBStringValue Text(VBTypedValue source)
    {
        var conversion = ValueConversions.ToText(source);
        Assert.IsTrue(conversion.IsSuccess, conversion.Error?.ToString());
        return (VBStringValue)conversion.Value!;
    }

    private static VBResizableByteArrayValue Bytes(params byte[] bytes)
    {
        var array = new VBResizableByteArrayValue([(0, bytes.Length - 1)]);
        for (var index = 0; index < bytes.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(bytes[index])), index);
        }
        return array;
    }

    [TestMethod]
    public void ToText_OfAStringWithATrailingByte_KeepsTheByte()
    {
        // "the result is a copy of the source string".
        var text = Text(VBStringValue.FromBytes([0x41, 0x00, 0x42]));

        Assert.AreEqual("A", text.Value);
        Assert.AreEqual((byte)0x42, text.TrailingByte);
    }

    [TestMethod]
    public void ToText_OfANumber_IsInvariant_WhenGivenNoCulture()
        => Assert.AreEqual("2.5", Text(new VBDoubleValue(2.5)).Value);

    [TestMethod]
    public void ToText_OfANumber_UsesTheDecimalSymbolOfTheCultureItIsGiven()
    {
        var conversion = ValueConversions.ToText(new VBDoubleValue(2.5), CultureInfo.GetCultureInfo(1031));

        Assert.AreEqual("2,5", ((VBStringValue)conversion.Value!).Value);
    }

    // MS-VBAL §5.5.1.2.4 prints at most 15 digits of any number; MS-VBA prints every digit of a Currency or a Decimal - CStr, &, Str and Format alike
    // (recorded in Excel), and RD-VBA is MS-VBA.
    [TestMethod]
    [DataRow("922337203685477.5807")]
    [DataRow("12345678901234.5678")]
    [DataRow("-0.0001")]
    [DataRow("1.5")]
    public void ToText_OfACurrency_IsEveryDigitOfIt(string digits)
        => Assert.AreEqual(digits, Text(new VBCurrencyValue(decimal.Parse(digits, CultureInfo.InvariantCulture))).Value);

    [TestMethod]
    [DataRow("12345678901234567.891")]
    [DataRow("0.1234567890123456789")]
    [DataRow("1234567890123456789012345678")]
    public void ToText_OfADecimal_IsEveryDigitOfIt(string digits)
        => Assert.AreEqual(digits, Text(new VBDecimalValue(decimal.Parse(digits, CultureInfo.InvariantCulture))).Value);

    [TestMethod]
    public void ToText_OfTheNullString_IsTheNullString()
        => Assert.IsTrue(Text(VBStringValue.VBNullString).IsNullString);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
    public void ToText_OfAByteArrayOfAnOddLength_KeepsTheLastByte()
        => CollectionAssert.AreEqual(new byte[] { 0x41, 0x00, 0x42 }, Text(Bytes(0x41, 0x00, 0x42)).GetBytes());

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
    public void ToText_OfAByteArrayHoldingALoneSurrogate_KeepsIt()
        => Assert.AreEqual("\uD800", Text(Bytes(0x00, 0xD8)).Value);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
    public void ToText_OfAnUninitializedByteArray_IsAZeroLengthString()
    {
        // "If the byte array is uninitialized, the result is a 0-length string."
        var text = Text(VBResizableByteArrayValue.Empty);

        Assert.AreEqual(string.Empty, text.Value);
        Assert.IsFalse(text.IsNullString);
    }
}

using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.StdLib;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.37 StrConv</strong>: a String cased, or read from or written to the environment's ANSI code page,
/// as a sum of <see cref="VBStrConv"/> values says, and between narrow and wide characters, hiragana and katakana, in the locales
/// those conversions apply to.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.37 StrConv")]
public sealed class StrConvTests
{
    private const int InvalidProcedureCall = (int)VBRuntimeErrorId.InvalidProcedureCallOrArgument;

    private static VBStrConv Conversion(int value) => (VBStrConv)value;

    [TestMethod]
    [DataRow("hello wORLD-foo bar_baz o'neil 1st x.y", 1, "HELLO WORLD-FOO BAR_BAZ O'NEIL 1ST X.Y")]
    [DataRow("hello wORLD-foo bar_baz o'neil 1st x.y", 2, "hello world-foo bar_baz o'neil 1st x.y")]
    [DataRow("ß", 1, "ß")]
    public void StrConv_CasesUpOrDown(string text, int conversion, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), Conversion(conversion))));

    [TestMethod]
    [DataRow("hello wORLD-foo bar_baz o'neil 1st x.y", "Hello World-foo Bar_baz O'neil 1st X.y")]
    [DataRow("hELLO wORLD", "Hello World")]
    [DataRow("o'neil-smith", "O'neil-smith")]
    [DataRow("a(b)c.d", "A(b)c.d")]
    [DataRow("mcdonald's", "Mcdonald's")]
    [DataRow("a\0b\tc\nd\ve\ff\rg h", "A\0B\tC\nD\vE\fF\rG H")]
    public void StrConv_ProperCase_StartsAWordAfterTheSeparatorsOnly(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), VBStrConv.VBProperCase)));

    [TestMethod]
    [DataRow("a b", "A b")]
    [DataRow("a b", "A b")]
    [DataRow("a　b", "A　b")]
    public void StrConv_ProperCase_StartsNoWordAfterAnotherSpace(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), VBStrConv.VBProperCase)));

    [TestMethod]
    public void StrConv_OfZero_IsTheStringUnchanged()
    {
        Assert.AreEqual("abc", TextOf(Module().StrConv(V("abc"), Conversion(0))));
        CollectionAssert.AreEqual(new byte[] { 0x61, 0x00, 0x62 }, BytesOf(Module().StrConv(V(Bytes(0x61, 0x00, 0x62)), Conversion(0))), "a trailing byte stays");
        CollectionAssert.AreEqual(new byte[] { 0x41, 0x00 }, BytesOf(Module().StrConv(V(Bytes(0x61, 0x00, 0x62)), VBStrConv.VBUpperCase)), "a case conversion reads the characters");
    }

    [TestMethod]
    public void StrConv_Unicode_ReadsTheBytesAsTheAnsiCodePagesText()
    {
        var widened = Module().StrConv(V("abc"), VBStrConv.VBUnicode);
        Assert.AreEqual("a\0b\0c\0", TextOf(widened));
        Assert.AreEqual(12, StringOf(widened).ByteLength);

        Assert.AreEqual("abc", TextOf(Module().StrConv(V(Bytes(0x61, 0x62, 0x63)), VBStrConv.VBUnicode)), "a trailing byte is read too");
        Assert.AreEqual("€", TextOf(Module().StrConv(V(Bytes(0x80)), VBStrConv.VBUnicode)));
        Assert.AreEqual("\u0081", TextOf(Module().StrConv(V(Bytes(0x81)), VBStrConv.VBUnicode)));
        Assert.AreEqual("あ", TextOf(Module(codePage: 932).StrConv(V(Bytes(0x82, 0xA0)), VBStrConv.VBUnicode)), "a lead and a trail byte");
    }

    [TestMethod]
    public void StrConv_FromUnicode_WritesTheCharactersAsTheAnsiCodePagesBytes()
    {
        var narrowed = Module().StrConv(V("abc"), VBStrConv.VBFromUnicode);
        CollectionAssert.AreEqual(new byte[] { 0x61, 0x62, 0x63 }, BytesOf(narrowed));
        Assert.AreEqual(1, StringOf(narrowed).Length, "two bytes to a character, and a trailing byte");

        CollectionAssert.AreEqual(new byte[] { 0x80 }, BytesOf(Module().StrConv(V("€"), VBStrConv.VBFromUnicode)));
        CollectionAssert.AreEqual(new byte[] { 0x3F }, BytesOf(Module().StrConv(V("あ"), VBStrConv.VBFromUnicode)), "a character the code page lacks");
        CollectionAssert.AreEqual(new byte[] { 0x82, 0xA0 }, BytesOf(Module(codePage: 932).StrConv(V("あ"), VBStrConv.VBFromUnicode)));
    }

    [TestMethod]
    public void StrConv_ACaseConversion_ComesAfterUnicodeAndBeforeFromUnicode()
    {
        Assert.AreEqual("É", TextOf(Module().StrConv(V(Bytes(0xE9)), Conversion(64 + 1))), "it cases the character the byte is");
        CollectionAssert.AreEqual(new byte[] { 0xC9 }, BytesOf(Module().StrConv(V("é"), Conversion(128 + 1))), "it writes the cased character");
        CollectionAssert.AreEqual(new byte[] { 0x41, 0x62, 0x63 }, BytesOf(Module().StrConv(V("abc"), Conversion(128 + 3))));
        Assert.AreEqual("A\0B\0C\0", TextOf(Module().StrConv(V("abc"), Conversion(64 + 1))));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(256)]
    [DataRow(512)]
    [DataRow(1024)]
    [DataRow(64 + 128)]
    [DataRow(4 + 8)]
    [DataRow(16 + 32)]
    public void StrConv_AnUndefinedOrMutuallyExclusiveConversion_IsError5(int conversion)
        => Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module().StrConv(V("abc"), Conversion(conversion))));

    [TestMethod]
    [DataRow(4, 0)]
    [DataRow(8, 0)]
    [DataRow(16, 0)]
    [DataRow(32, 0)]
    [DataRow(4, 1033)]
    [DataRow(8, 1036)]
    [DataRow(16, 2052)]
    [DataRow(32, 1042)]
    public void StrConv_TheEastAsianConversions_AreError5_WhereTheyDoNotApply(int conversion, int lcid)
        => Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module().StrConv(V("abc"), Conversion(conversion), lcid == 0 ? null : L(lcid))));

    private const int Japanese = 1041;

    [TestMethod]
    [DataRow("abc 123!~", "ａｂｃ　１２３！～")]
    [DataRow("ｶﾞｷﾞﾊﾟﾝ ｳﾞ", "ガギパン　ヴ")]
    [DataRow("｢ｱｲｳ｣､｡･ｰ", "「アイウ」、。・ー")]
    [DataRow("ﾞ", "゛")]
    [DataRow("¥¢", "￥￠")]
    [DataRow("ひらがな漢字", "ひらがな漢字")]
    public void StrConv_Wide_WidensTheNarrowCharacters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module(Japanese).StrConv(V(text), VBStrConv.VBWide)));

    [TestMethod]
    [DataRow("ａｂｃ　１２３！～", "abc 123!~")]
    [DataRow("ガギパン　ヴ", "ｶﾞｷﾞﾊﾟﾝ ｳﾞ")]
    [DataRow("「アイウ」、。・ー", "｢ｱｲｳ｣､｡･ｰ")]
    [DataRow("ひらがな漢字", "ひらがな漢字")]
    public void StrConv_Narrow_NarrowsTheWideCharacters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module(Japanese).StrConv(V(text), VBStrConv.VBNarrow)));

    [TestMethod]
    public void StrConv_KatakanaAndHiragana_ConvertBetweenTheSyllabaries()
    {
        Assert.AreEqual("ヒラガナ ヴヽ", TextOf(Module(Japanese).StrConv(V("ひらがな ゔゝ"), VBStrConv.VBKatakana)));
        Assert.AreEqual("かたかな ゔゞ ヷ", TextOf(Module(Japanese).StrConv(V("カタカナ ヴヾ ヷ"), VBStrConv.VBHiragana)));
        Assert.AreEqual("ｶﾀｶﾅ", TextOf(Module(Japanese).StrConv(V("ｶﾀｶﾅ"), VBStrConv.VBHiragana)), "a halfwidth katakana has no hiragana");
    }

    [TestMethod]
    public void StrConv_TheConversions_AreMadeInOneOrder()
    {
        Assert.AreEqual("かたかな", TextOf(Module(Japanese).StrConv(V("ｶﾀｶﾅ"), VBStrConv.VBWide | VBStrConv.VBHiragana)), "widened, then a hiragana");
        Assert.AreEqual("ﾋﾗｶﾞﾅ", TextOf(Module(Japanese).StrConv(V("ひらがな"), VBStrConv.VBKatakana | VBStrConv.VBNarrow)), "a katakana, then narrowed");
        Assert.AreEqual("ＡＢＣ", TextOf(Module(Japanese).StrConv(V("abc"), VBStrConv.VBUpperCase | VBStrConv.VBWide)));
    }

    [TestMethod]
    public void StrConv_TheLocale_IsLocaleIDsOrTheEnvironments()
    {
        Assert.AreEqual("ａｂｃ", TextOf(Module().StrConv(V("abc"), VBStrConv.VBWide, L(2052))), "Chinese is an East Asian locale");
        Assert.AreEqual("ㄱ", TextOf(Module().StrConv(V("\uFFA1"), VBStrConv.VBWide, L(1042))), "so is Korean, whose halfwidth Hangul widens");
        Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module(Japanese).StrConv(V("abc"), VBStrConv.VBWide, L(1033))));
    }

    [TestMethod]
    public void StrConv_ProperCase_InAnEastAsianLocale_StartsAWordAfterAnIdeographicSpace()
    {
        Assert.AreEqual("Abc\u3000Def", TextOf(Module(Japanese).StrConv(V("abc\u3000def"), VBStrConv.VBProperCase)));
        Assert.AreEqual("Abc\u3000def", TextOf(Module(1033).StrConv(V("abc\u3000def"), VBStrConv.VBProperCase)));
    }

    [TestMethod]
    public void StrConv_OfNullEmptyOrANumber()
    {
        Assert.IsTrue(IsNull(Module().StrConv(VNull, VBStrConv.VBUpperCase)));
        Assert.IsTrue(IsNull(Module().StrConv(VNull, Conversion(-1))), "a Null String before Conversion's check");
        Assert.AreEqual(string.Empty, TextOf(Module().StrConv(VEmpty, VBStrConv.VBUpperCase)));
        Assert.AreEqual("123", TextOf(Module().StrConv(V(123), VBStrConv.VBUpperCase)));
    }

    [TestMethod]
    public void StrConv_OfAValueWithNoStringForm_IsError13_BeforeConversionsCheck()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().StrConv(VMissing, Conversion(-1))));

    [TestMethod]
    public void StrConv_CasesByItsLocaleID_OrTheEnvironmentsCulture()
    {
        Assert.AreEqual("İ", TextOf(Module().StrConv(V("i"), VBStrConv.VBUpperCase, L(1055))), "Turkish has a dotted I");
        Assert.AreEqual("ı", TextOf(Module().StrConv(V("I"), VBStrConv.VBLowerCase, L(1055))), "and a dotless i");
        Assert.AreEqual("İkı", TextOf(Module().StrConv(V("iKI"), VBStrConv.VBProperCase, L(1055))));
        Assert.AreEqual("I", TextOf(Module().StrConv(V("i"), VBStrConv.VBUpperCase)));

        var turkish = Module(lcid: 1055);
        Assert.AreEqual("İ", TextOf(turkish.StrConv(V("i"), VBStrConv.VBUpperCase)), "the environment's, left out");
        Assert.AreEqual("İ", TextOf(turkish.StrConv(V("i"), VBStrConv.VBUpperCase, L(0))), "and at 0");
        Assert.AreEqual("I", TextOf(turkish.StrConv(V("i"), VBStrConv.VBUpperCase, L(1033))));
    }

    [TestMethod]
    [DataRow(12345)]
    [DataRow(1024)]
    [DataRow(2048)]
    [DataRow(-1)]
    public void StrConv_WithALocaleIDThatNamesNoLocale_IsError5(int lcid)
        => Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module().StrConv(V("abc"), VBStrConv.VBUpperCase, L(lcid))));

    [TestMethod]
    public void StrConv_WithALocaleID_ConvertsInTheEnvironmentsCodePage()
    {
        // "the default code page of the system": a Japanese LocaleID does not make it 932.
        CollectionAssert.AreEqual(new byte[] { 0x80 }, BytesOf(Module().StrConv(V("€"), VBStrConv.VBFromUnicode, L(1041))));
        Assert.AreEqual("€", TextOf(Module().StrConv(V(Bytes(0x80)), VBStrConv.VBUnicode, L(1041))));
    }

    [TestMethod]
    public void StrConv_FromSource_WithTheLocaleLeftOut()
        => CollectionAssert.AreEqual(new[] { "Hello World", "ABC", "String", "Null" }, Run(
            "Debug.Print StrConv(\"hELLO wORLD\", vbProperCase)",
            "Debug.Print StrConv(\"abc\", vbUpperCase)",
            "Debug.Print TypeName(StrConv(\"a\", vbUpperCase))",
            "Debug.Print StrConv(Null, vbUpperCase)"));

    [TestMethod]
    public void StrConv_FromSource_RoundTripsThroughAByteArray()
        => CollectionAssert.AreEqual(new[] { "3", "abc", "2", "abc" }, RunWith([Field("B", VBResizableByteArrayType.TypeInfo)],
            "Debug.Print LenB(StrConv(\"abc\", vbFromUnicode))",
            "Debug.Print StrConv(StrConv(\"abc\", vbFromUnicode), vbUnicode)",
            "B = StrConv(\"abc\", vbFromUnicode)",
            "Debug.Print UBound(B)",
            "Debug.Print StrConv(B, vbUnicode)"));

    [TestMethod]
    public void StrConv_FromSource_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print StrConv(\"abc\", Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print StrConv(\"abc\", vbUpperCase, Null)"));
        Assert.AreEqual(InvalidProcedureCall, RunToError("Debug.Print StrConv(\"abc\", vbNarrow)"));
        Assert.AreEqual(InvalidProcedureCall, RunToError("Debug.Print StrConv(\"abc\", vbUnicode + vbFromUnicode)"));
    }
}

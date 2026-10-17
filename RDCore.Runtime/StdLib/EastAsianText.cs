using RDCore.Runtime.StdLib.Formatting;
using System.Collections.Frozen;
using System.Text;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// <c>StrConv</c>'s East Asian conversions: <c>vbWide</c> and <c>vbNarrow</c> between the narrow (single-byte) characters and their
/// wide (double-byte) forms, and <c>vbKatakana</c> and <c>vbHiragana</c> between the two Japanese syllabaries.
/// </summary>
/// <remarks>
/// <para>
/// The wide form of a narrow character is the one Unicode gives as its compatibility mapping: the ASCII characters from <c>!</c> to
/// <c>~</c> and the fullwidth forms (U+FF01 to U+FF5E), a space and the ideographic space (U+3000), the halfwidth katakana, Japanese
/// punctuation and Hangul (U+FF61 to U+FFDC) and the characters they are forms of, and the symbols <c>¢ £ ¬ ¯ ¦ ¥ ₩</c> and their
/// fullwidth forms (U+FFE0 to U+FFE6). A halfwidth voiced or semi-voiced sound mark after a halfwidth katakana that has a voiced or
/// semi-voiced form makes that one katakana (<c>ｶﾞ</c> is <c>ガ</c>), and a voiced katakana is narrowed to the two (<c>ガ</c> is
/// <c>ｶﾞ</c>). Anything else is left as it is: there are no halfwidth hiragana, so a hiragana stays wide.
/// </para>
/// <para>
/// A hiragana's katakana is the one 0x60 code points up (U+3041 to U+3096, and the iteration marks <c>ゝ ゞ</c>), and the other way
/// round; a katakana with no hiragana (<c>ヷ</c> to <c>ヺ</c>, the halfwidth ones) stays.
/// </para>
/// </remarks>
internal static class EastAsianText
{
    private const char VoicedMark = '\u3099';
    private const char SemiVoicedMark = '\u309A';
    private const char HalfwidthVoicedMark = '\uFF9E';
    private const char HalfwidthSemiVoicedMark = '\uFF9F';
    private const int KanaDistance = 0x60;

    private static readonly FrozenDictionary<char, char> Wide;
    private static readonly FrozenDictionary<char, char> Narrow;
    private static readonly FrozenDictionary<(char Kana, char Mark), char> Voiced;
    private static readonly FrozenDictionary<char, (char Kana, char Mark)> Unvoiced;

    static EastAsianText()
    {
        var wide = new Dictionary<char, char> { [' '] = '\u3000' };
        for (var character = '!'; character <= '~'; character++)
        {
            wide[character] = (char)(character + 0xFEE0);
        }

        // a halfwidth Hangul letter and its wide form, a compatibility jamo, both map to the same conjoining jamo.
        var jamo = new Dictionary<char, char>();
        for (var compatibility = '\u3131'; compatibility <= '\u3164'; compatibility++)
        {
            jamo.TryAdd(compatibility.ToString().Normalize(NormalizationForm.FormKC)[0], compatibility);
        }

        // a halfwidth form's compatibility mapping is the wide character it is a form of; the two sound marks map to combining
        // marks, and are written as the spacing ones.
        for (var halfwidth = '\uFF61'; halfwidth <= '\uFFDC'; halfwidth++)
        {
            var mapped = halfwidth.ToString().Normalize(NormalizationForm.FormKC);
            if (mapped.Length == 1 && mapped[0] != halfwidth)
            {
                wide[halfwidth] = mapped[0] switch
                {
                    VoicedMark => '\u309B',
                    SemiVoicedMark => '\u309C',
                    var full when jamo.TryGetValue(full, out var compatibility) => compatibility,
                    var full => full,
                };
            }
        }

        foreach (var (narrow, fullwidth) in new[] { ('\u00A2', '\uFFE0'), ('\u00A3', '\uFFE1'), ('\u00AC', '\uFFE2'), ('\u00AF', '\uFFE3'), ('\u00A6', '\uFFE4'), ('\u00A5', '\uFFE5'), ('\u20A9', '\uFFE6') })
        {
            wide[narrow] = fullwidth;
        }

        Wide = wide.ToFrozenDictionary();
        Narrow = wide.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

        // the katakana that a sound mark composes with, as Unicode composes them: カ and ゛ are ガ, ハ and ゜ are パ, ウ and ゛ are ヴ.
        var voiced = new Dictionary<(char, char), char>();
        for (var kana = '\u30A1'; kana <= '\u30FA'; kana++)
        {
            foreach (var mark in new[] { VoicedMark, SemiVoicedMark })
            {
                var composed = string.Concat(kana, mark).Normalize(NormalizationForm.FormC);
                if (composed.Length == 1)
                {
                    voiced[(kana, mark)] = composed[0];
                }
            }
        }

        Voiced = voiced.ToFrozenDictionary();
        Unvoiced = voiced.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);
    }

    /// <summary><c>vbWide</c>: each narrow character as its wide form.</summary>
    public static string Widen(string text)
    {
        var output = new TextBuilder(stackalloc char[Math.Min(text.Length, 256)]);
        try
        {
            for (var index = 0; index < text.Length; index++)
            {
                var wide = Wide.TryGetValue(text[index], out var mapped) ? mapped : text[index];
                if (index + 1 < text.Length && text[index + 1] is HalfwidthVoicedMark or HalfwidthSemiVoicedMark
                    && Voiced.TryGetValue((wide, text[index + 1] == HalfwidthVoicedMark ? VoicedMark : SemiVoicedMark), out var composed))
                {
                    wide = composed;
                    index++;
                }

                output.Append(wide);
            }

            return output.ToString();
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary><c>vbNarrow</c>: each wide character that has a narrow form as that form, a voiced katakana as two.</summary>
    public static string Narrowed(string text)
    {
        var output = new TextBuilder(stackalloc char[Math.Min(text.Length + 16, 256)]);
        try
        {
            foreach (var character in text)
            {
                if (Unvoiced.TryGetValue(character, out var parts) && Narrow.TryGetValue(parts.Kana, out var narrowKana))
                {
                    output.Append(narrowKana);
                    output.Append(parts.Mark == VoicedMark ? HalfwidthVoicedMark : HalfwidthSemiVoicedMark);
                    continue;
                }

                output.Append(character switch
                {
                    VoicedMark => HalfwidthVoicedMark,
                    SemiVoicedMark => HalfwidthSemiVoicedMark,
                    _ => Narrow.TryGetValue(character, out var narrow) ? narrow : character,
                });
            }

            return output.ToString();
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary><c>vbKatakana</c>: each hiragana as its katakana.</summary>
    public static string ToKatakana(string text) => Shifted(text, '\u3041', '\u3096', KanaDistance);

    /// <summary><c>vbHiragana</c>: each katakana as its hiragana.</summary>
    public static string ToHiragana(string text) => Shifted(text, '\u30A1', '\u30F6', -KanaDistance);

    // the characters of one syllabary, and its two iteration marks, as the other's: one character for one.
    private static string Shifted(string text, char first, char last, int distance)
        => string.Create(text.Length, (text, first, last, distance), static (characters, state) =>
        {
            var (text, first, last, distance) = state;
            var firstMark = (char)(distance > 0 ? '\u309D' : '\u30FD');
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                characters[index] = character >= first && character <= last || character == firstMark || character == firstMark + 1
                    ? (char)(character + distance)
                    : character;
            }
        });
}

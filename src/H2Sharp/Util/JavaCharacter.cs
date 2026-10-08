/*
 * Ported from java.lang.Character (the JDK, OpenJDK).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The four character classes the reference asks java.lang.Character about, and the two helpers it
 * asks about a string. None of them exist under those names in .NET, and none of them may be
 * replaced by the nearest .NET method:
 *
 * - Character.isJavaIdentifierStart is a letter, a letter number, a currency symbol or a connector
 *   punctuation. char.IsLetter is narrower (it leaves out the letter numbers), and
 *   char.IsLetterOrDigit is wider (it takes in the marks that may only continue a name).
 * - Character.isJavaIdentifierPart is the same set plus a non-spacing mark, a combining spacing
 *   mark, a decimal digit, and the two categories Java calls ignorable: the control characters and
 *   the format characters.
 * - Character.isSpaceChar is three categories, not one: a space separator and the line and
 *   paragraph separators, so U+2028 is a space character although it is not in the category that
 *   is called a space.
 * - Character.isWhitespace is those three categories except for the three no-break spaces (U+00A0,
 *   U+2007 and U+202F), plus the five whitespace controls and the four separators U+001C to
 *   U+001F. That exclusion is not an accident and not a typo: the JDK's own answer for U+00A0 is
 *   "a space character and not whitespace", and an earlier version of this file believed otherwise
 *   and was corrected by JavaCharacterOracleTests.
 *
 * Every one of these is checked against the reference JVM for all 0x110000 code points by
 * JavaCharacterOracleTests, because these are Unicode tables and a table copied by hand is a table
 * that is wrong somewhere. The rules above were read off the JDK's own answers: of the 65 control
 * characters and 170 format characters, every one continues a name, and of the 13 enclosing marks
 * none does.
 */
using System;
using System.Globalization;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// The character classes of java.lang.Character, for the places where the reference's answer and
/// the .NET answer are not the same answer.
/// </summary>
public static class JavaCharacter
{
    /// <summary>
    /// The first code point Java treats as a surrogate, and the last.
    /// </summary>
    private const int MinSurrogate = 0xD800;

    private const int MaxSurrogate = 0xDFFF;

    /// <summary>
    /// The first leading surrogate, and the first code point above the basic multilingual plane.
    /// </summary>
    private const int MaxHighSurrogate = 0xDBFF;

    private const int MinSupplementary = 0x10000;

    /// <summary>
    /// Whether the character is the leading half of a surrogate pair, which comes first.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character is a leading surrogate</returns>
    public static bool IsHighSurrogate(char c) => (int)c is >= MinSurrogate and <= MaxHighSurrogate;

    /// <summary>
    /// Whether the character is the trailing half of a surrogate pair.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character is a trailing surrogate</returns>
    public static bool IsLowSurrogate(char c) => (int)c is > MaxHighSurrogate and <= MaxSurrogate;

    /// <summary>
    /// How many characters a code point is written in: one, or two for a character above the basic
    /// multilingual plane.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>1 or 2</returns>
    public static int CharCount(int codePoint) => codePoint >= MinSupplementary ? 2 : 1;

    /// <summary>
    /// The code point at a place in a string, which is the character itself unless it is the
    /// leading half of a pair, in which case the pair.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="index">the place to read at</param>
    /// <returns>the code point</returns>
    /// <remarks>
    /// The reference reads past the end of a string in two places and gets a code point the
    /// characters did not really make; whether that matters is the caller's question. This answers
    /// what String answers, which for an unpaired surrogate is the surrogate itself.
    /// </remarks>
    public static int CodePointAt(string s, int index)
    {
        char c = s[index];
        if (IsHighSurrogate(c) && index + 1 < s.Length && IsLowSurrogate(s[index + 1]))
        {
            return char.ConvertToUtf32(c, s[index + 1]);
        }

        return c;
    }

    /// <summary>
    /// Whether the character may begin a Java name.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character may begin a name</returns>
    public static bool IsJavaIdentifierStart(char c) => IsStartCategory(CategoryOf(c));

    /// <summary>
    /// Whether the code point may begin a Java name.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>true if the code point may begin a name</returns>
    public static bool IsJavaIdentifierStart(int codePoint) => IsStartCategory(CategoryOf(codePoint));

    /// <summary>
    /// Whether the character may continue a Java name.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character may continue a name</returns>
    public static bool IsJavaIdentifierPart(char c)
        => IsPartCategory(CategoryOf(c)) || IsIgnorableControl(c);

    /// <summary>
    /// Whether the code point may continue a Java name.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>true if the code point may continue a name</returns>
    public static bool IsJavaIdentifierPart(int codePoint)
        => IsPartCategory(CategoryOf(codePoint)) || IsIgnorableControl(codePoint);

    /// <summary>
    /// Whether the character is a space on its own.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character is a space character</returns>
    public static bool IsSpaceChar(char c) => IsSeparatorCategory(CategoryOf(c));

    /// <summary>
    /// Whether the code point is a space on its own.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>true if the code point is a space character</returns>
    public static bool IsSpaceChar(int codePoint) => IsSeparatorCategory(CategoryOf(codePoint));

    /// <summary>
    /// Whether the character is whitespace, which is a space character except for the three
    /// no-break spaces, plus the controls Java calls whitespace on their own.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character is whitespace</returns>
    public static bool IsWhitespace(char c)
        => c is >= (char)0x09 and <= (char)0x0D
            || c is >= (char)0x1C and <= (char)0x1F
            || (IsSpaceChar(c) && c is not ((char)0x00A0 or (char)0x2007 or (char)0x202F));

    /// <summary>
    /// Whether the code point is whitespace.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>true if the code point is whitespace</returns>
    public static bool IsWhitespace(int codePoint)
        => codePoint is >= 0x09 and <= 0x0D
            || codePoint is >= 0x1C and <= 0x1F
            || (IsSpaceChar(codePoint) && codePoint is not (0x00A0 or 0x2007 or 0x202F));

    /// <summary>
    /// Whether the character is a letter: one of the five letter categories, or a letter number.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character is a letter</returns>
    public static bool IsLetter(char c) => IsLetterCategory(CategoryOf(c));

    /// <summary>
    /// Whether the code point is a letter.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>true if the code point is a letter</returns>
    public static bool IsLetter(int codePoint) => IsLetterCategory(CategoryOf(codePoint));

    /// <summary>
    /// Whether the character is a decimal digit.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>true if the character is a digit</returns>
    public static bool IsDigit(char c) => c is >= '0' and <= '9';

    /// <summary>
    /// Whether the code point is a decimal digit.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>true if the code point is a digit</returns>
    public static bool IsDigit(int codePoint)
        => codePoint is >= '0' and <= '9'
            || CategoryOf(codePoint) == UnicodeCategory.DecimalDigitNumber;

    /// <summary>
    /// The Unicode category of a character.
    /// </summary>
    /// <param name="c">the character</param>
    /// <returns>the category</returns>
    public static UnicodeCategory CategoryOf(char c) => CharUnicodeInfo.GetUnicodeCategory(c);

    /// <summary>
    /// The Unicode category of a code point.
    /// </summary>
    /// <param name="codePoint">the code point</param>
    /// <returns>the category</returns>
    public static UnicodeCategory CategoryOf(int codePoint)
        => codePoint is >= 0 and <= 0xFFFF
            ? CharUnicodeInfo.GetUnicodeCategory((char)codePoint)
            : Rune.GetUnicodeCategory((Rune)codePoint);

    private static bool IsLetterCategory(UnicodeCategory category)
        => category
            is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber;

    private static bool IsSeparatorCategory(UnicodeCategory category)
        => category
            is UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator;

    private static bool IsStartCategory(UnicodeCategory category)
        => category
            is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.CurrencySymbol;

    /// <remarks>
    /// The start set and four more: the two kinds of mark that join a name to the letter before
    /// them, a decimal digit, and the two categories Java calls ignorable. "Ignorable" is Java's
    /// word for the control characters and the format characters, and the JDK's own list of them is
    /// one switch with twenty ranges in it; the categories say the same thing, which the oracle
    /// over every code point confirms to the character: 65 of 65 control characters and 170 of 170
    /// format characters continue a name. The enclosing mark is deliberately not in the set, because
    /// the JDK lets none of its 13 continue a name.
    /// </remarks>
    /// <summary>
    /// Whether the code point is a control character that is not whitespace, which are the ones
    /// Java lets a name skip over.
    /// </summary>
    /// <remarks>
    /// Of the 65 control characters, 56 continue a name and the nine that do not are exactly the
    /// five whitespace controls and the four separators Java calls whitespace: a tab inside a name
    /// is not part of the name and is not an ignorable character either. The oracle found this one
    /// at U+0009 after the categories alone had said yes to all 65.
    /// </remarks>
    private static bool IsIgnorableControl(int codePoint)
        => CategoryOf(codePoint) == UnicodeCategory.Control
            && codePoint is not (>= 0x09 and <= 0x0D or >= 0x1C and <= 0x1F);

    /// <summary>
    /// Whether the character is a control character that is not whitespace.
    /// </summary>
    private static bool IsIgnorableControl(char c) => IsIgnorableControl((int)c);

    private static bool IsPartCategory(UnicodeCategory category)
        => category
            is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.Format;
}

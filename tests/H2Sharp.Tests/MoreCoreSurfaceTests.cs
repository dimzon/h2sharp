/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 254: small public surfaces of the value/utility layer — Java character rules,
 * comparison modes, the store's byte buffer and prepared-command metadata.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Store;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MoreCoreSurfaceTests
{
    [Fact]
    public void JavaCharacterAnswersUnicodeQuestions()
    {
        Assert.True(JavaCharacter.IsHighSurrogate('\uD83C'));
        Assert.False(JavaCharacter.IsLowSurrogate('\uD83C'));
        Assert.True(JavaCharacter.IsLowSurrogate('\uDF89'));
        Assert.Equal(2, JavaCharacter.CharCount(0x1F389));
        Assert.Equal(1, JavaCharacter.CharCount('A'));
        Assert.Equal(0x1F389, JavaCharacter.CodePointAt("\U0001F389", 0));

        Assert.True(JavaCharacter.IsJavaIdentifierStart('_'));
        Assert.True(JavaCharacter.IsJavaIdentifierStart('A'));
        Assert.False(JavaCharacter.IsJavaIdentifierStart('1'));
        Assert.True(JavaCharacter.IsJavaIdentifierPart('1'));
        Assert.True(JavaCharacter.IsLetter('é'));
        Assert.False(JavaCharacter.IsLetter('1'));
        Assert.True(JavaCharacter.IsWhitespace('\t'));
        Assert.True(JavaCharacter.IsSpaceChar('\u00A0'));
        Assert.False(JavaCharacter.IsSpaceChar('a'));
        Assert.True(JavaCharacter.IsDigit('9'));
        Assert.False(JavaCharacter.IsDigit('x'));
        Assert.Equal(9, JavaCharacter.DigitValue('9'));
        Assert.Equal('A', JavaCharacter.ToUpperCase('a'));
        Assert.Equal('a', JavaCharacter.ToLowerCase('A'));
    }

    [Fact]
    public void CompareModesFollowTheirNamesAndStrength()
    {
        CompareMode off = CompareMode.GetInstance(CompareMode.Off, CompareMode.Identical);
        Assert.Equal(CompareMode.Off, off.GetName());
        Assert.Equal(CompareMode.Identical, off.GetStrength());
        Assert.True(off.EqualsChars("abc", 0, "abc", 0, false));
        Assert.False(off.EqualsChars("abc", 0, "xbc", 0, false));
        Assert.True(off.EqualsChars("ABC", 0, "abc", 0, true));
        Assert.True(off.CompareString("a", "b", false) < 0);
        Assert.True(off.CompareString("b", "a", false) > 0);

        CompareMode def = CompareMode.GetInstance(CompareMode.Default, CompareMode.Tertiary);
        Assert.Equal(CompareMode.Default, def.GetName());
        Assert.Equal(CompareMode.Tertiary, def.GetStrength());
        Assert.Throws<NotSupportedException>(() => def.EqualsChars("i", 0, "i", 0, true));
        Assert.Equal(def, CompareMode.GetInstance(CompareMode.Default, CompareMode.Tertiary));
        Assert.NotEqual(def, off);
        Assert.Equal(def.GetHashCode(), CompareMode.GetInstance(CompareMode.Default, CompareMode.Tertiary).GetHashCode());
    }

    [Fact]
    public void StoreByteArrayBufferKeepsItsPosition()
    {
        H2Sharp.Store.Data data = H2Sharp.Store.Data.Create(8);
        data.WriteInt(42);
        Assert.Equal(4, data.Length());
        data.Write([1, 2, 3], 0, 3);
        Assert.Equal(7, data.Length());
        data.FillAligned();
        Assert.Equal(16, data.Length());

        data.SetPos(0);
        Assert.Equal(42, data.ReadInt());
        byte[] bytes = new byte[3];
        data.Read(bytes, 0, 3);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);

        data.Reset();
        Assert.Equal(0, data.Length());
        data.SetPos(0);
        Assert.Equal(0, data.ReadByte());

        using MemoryStream target = new();
        H2Sharp.Store.Data.CopyString(new StringReader("abc\n"), target);
        Assert.Equal("abc\n", Encoding.UTF8.GetString(target.ToArray()));
    }

    [Fact]
    public void PreparedCommandAnswersReadOnlyAndRetry()
    {
        SessionLocal session = new(new Database(ConnectionInfo.Memory("prepared_surface")), "sa");
        try
        {
            CommandInterface command = session.PrepareCommand("SELECT 1");
            Prepared prepared = Assert.IsType<CommandContainer>(command).GetPreparedCommand();
            Assert.True(prepared.IsReadOnly());
            Assert.False(prepared.IsRetryable());
        }
        finally
        {
            session.GetDatabase().Close();
        }
    }
}

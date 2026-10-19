/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.SoundexFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A SOUNDEX or DIFFERENCE function.
/// </summary>
public sealed class SoundexFunction : Function1_2
{
    /// <summary>
    /// SOUNDEX() (non-standard).
    /// </summary>
    public const int Soundex = 0;

    /// <summary>
    /// DIFFERENCE() (non-standard).
    /// </summary>
    public const int Difference = Soundex + 1;

    private static readonly string[] Names = [
        "SOUNDEX", "DIFFERENCE"
    ];

    private const string SoundexIndex = //
        "71237128722455712623718272\0\0\0\0\0\0"
        + "71237128722455712623718272";

    private readonly int function;

    /// <summary>
    /// Creates a soundex function.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument, or null</param>
    /// <param name="function">one of <see cref="Soundex"/> or <see cref="Difference"/></param>
    public SoundexFunction(Expression arg1, Expression? arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        switch (function)
        {
            case Soundex:
                return ValueVarchar.Get(new string(GetSoundex(v1.GetString()!)), session);
            case Difference:
                return ValueInteger.Get(GetDifference(v1.GetString()!, v2!.GetString()!));
            default:
                throw DbException.GetInternalError("function=" + function);
        }
    }

    private static int GetDifference(string s1, string s2)
    {
        // TODO function difference: compatibility with SQL Server and HSQLDB
        char[] b1 = GetSoundex(s1), b2 = GetSoundex(s2);
        int e = 0;
        for (int i = 0; i < 4; i++)
        {
            if (b1[i] == b2[i])
            {
                e++;
            }
        }

        return e;
    }

    private static char[] GetSoundex(string s)
    {
        char[] chars = ['0', '0', '0', '0'];
        char lastDigit = '0';
        for (int i = 0, j = 0, l = s.Length; i < l && j < 4; i++)
        {
            char c = s[i];
            if (c >= 'A' && c <= 'z')
            {
                char newDigit = SoundexIndex[c - 'A'];
                if (newDigit != '\0')
                {
                    if (j == 0)
                    {
                        // Converts a-z to A-Z
                        chars[j++] = (char)(c & 0xdf);
                        lastDigit = newDigit;
                    }
                    else if (newDigit <= '6')
                    {
                        if (newDigit != lastDigit)
                        {
                            chars[j++] = lastDigit = newDigit;
                        }
                    }
                    else if (newDigit == '7')
                    {
                        lastDigit = newDigit;
                    }
                }
            }
        }

        return chars;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right != null)
        {
            right = right.Optimize(session);
        }

        switch (function)
        {
            case Soundex:
                type = TypeInfo.GetTypeInfo(Value.Varchar, 4, 0, null);
                break;
            case Difference:
                type = TypeInfo.TypeInteger;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        if (left.IsConstant() && (right == null || right.IsConstant()))
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}

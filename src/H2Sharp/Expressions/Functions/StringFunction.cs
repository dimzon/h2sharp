/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.StringFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The static helpers locates, inserts and translates are named LocateValue,
 * InsertValue and TranslateValue: the function codes carry the reference's own
 * names without the suffix, and C# will not let one class hold a constant and a
 * method under one name. The suffix is the same one Value.CastToValue uses.
 */
using System;
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// An string function with multiple arguments.
/// </summary>
public sealed class StringFunction : FunctionN
{
    /// <summary>
    /// LOCATE() (non-standard).
    /// </summary>
    public const int Locate = 0;

    /// <summary>
    /// INSERT() (non-standard).
    /// </summary>
    public const int Insert = Locate + 1;

    /// <summary>
    /// REPLACE() (non-standard).
    /// </summary>
    public const int Replace = Insert + 1;

    /// <summary>
    /// LPAD().
    /// </summary>
    public const int Lpad = Replace + 1;

    /// <summary>
    /// RPAD().
    /// </summary>
    public const int Rpad = Lpad + 1;

    /// <summary>
    /// TRANSLATE() (non-standard).
    /// </summary>
    public const int Translate = Rpad + 1;

    private static readonly string[] Names = ["LOCATE", "INSERT", "REPLACE", "LPAD", "RPAD", "TRANSLATE"];

    private readonly int function;

    /// <summary>
    /// Creates a string function with two or three arguments.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument</param>
    /// <param name="arg3">the third argument, or null</param>
    /// <param name="function">the function code</param>
    public StringFunction(Expression arg1, Expression arg2, Expression? arg3, int function)
        : base(arg3 == null ? [arg1, arg2] : [arg1, arg2, arg3])
    {
        this.function = function;
    }

    /// <summary>
    /// Creates a string function with four arguments.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument</param>
    /// <param name="arg3">the third argument</param>
    /// <param name="arg4">the fourth argument</param>
    /// <param name="function">the function code</param>
    public StringFunction(Expression arg1, Expression arg2, Expression arg3, Expression arg4, int function)
        : base([arg1, arg2, arg3, arg4])
    {
        this.function = function;
    }

    /// <summary>
    /// Creates a string function with the given arguments.
    /// </summary>
    /// <param name="args">the arguments</param>
    /// <param name="function">the function code</param>
    public StringFunction(Expression[] args, int function)
        : base(args)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v1 = args[0].GetValue(session), v2 = args[1].GetValue(session);
        switch (function)
        {
            case Locate:
                {
                    if (v1 == ValueNull.Instance || v2 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    Value? v3 = args.Length >= 3 ? args[2].GetValue(session) : null;
                    if (v3 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    v1 = ValueInteger.Get(
                        LocateValue(v1.GetString()!, v2.GetString()!, v3 == null ? 1 : v3.GetInt()));
                    break;
                }

            case Insert:
                {
                    Value v3 = args[2].GetValue(session), v4 = args[3].GetValue(session);
                    if (v2 != ValueNull.Instance && v3 != ValueNull.Instance)
                    {
                        string? s = InsertValue(v1.GetString(), v2.GetInt(), v3.GetInt(), v4.GetString());
                        v1 = s != null ? ValueVarchar.Get(s, session) : ValueNull.Instance;
                    }

                    break;
                }

            case Replace:
                {
                    if (v1 == ValueNull.Instance || v2 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    string after;
                    if (args.Length >= 3)
                    {
                        Value v3 = args[2].GetValue(session);
                        if (v3 == ValueNull.Instance && session.GetMode().Enum != Mode.ModeEnum.Oracle)
                        {
                            return ValueNull.Instance;
                        }

                        after = v3.GetString() ?? "";
                    }
                    else
                    {
                        after = "";
                    }

                    v1 = ValueVarchar.Get(
                        StringUtils.ReplaceAll(v1.GetString()!, v2.GetString()!, after), session);
                    break;
                }

            case Lpad:
            case Rpad:
                if (v1 == ValueNull.Instance || v2 == ValueNull.Instance)
                {
                    return ValueNull.Instance;
                }

                string? padding;
                if (args.Length >= 3)
                {
                    Value v3 = args[2].GetValue(session);
                    if (v3 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    padding = v3.GetString();
                }
                else
                {
                    padding = null;
                }

                v1 = ValueVarchar.Get(
                    StringUtils.Pad(v1.GetString()!, v2.GetInt(), padding, function == Rpad), session);
                break;

            case Translate:
                {
                    if (v1 == ValueNull.Instance || v2 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    Value v3 = args[2].GetValue(session);
                    if (v3 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    string matching = v2.GetString()!;
                    string replacement = v3.GetString()!;
                    if (session.GetMode().Enum == Mode.ModeEnum.Db2)
                    {
                        (matching, replacement) = (replacement, matching);
                    }

                    v1 = ValueVarchar.Get(TranslateValue(v1.GetString()!, matching, replacement)!, session);
                    break;
                }

            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v1;
    }

    private static int LocateValue(string search, string s, int start)
    {
        if (start < 0)
        {
            return JavaLastIndexOf(search, s, s.Length + start) + 1;
        }

        return JavaIndexOf(search, s, start == 0 ? 0 : start - 1) + 1;
    }

    /// <summary>
    /// The index of the first occurrence of <paramref name="search"/> at or after
    /// <paramref name="fromIndex"/>, with the reference's (java.lang.String's) edge rules for a
    /// start position outside the string.
    /// </summary>
    private static int JavaIndexOf(string search, string s, int fromIndex)
    {
        if (fromIndex < 0)
        {
            fromIndex = 0;
        }
        else if (fromIndex >= s.Length)
        {
            return search.Length == 0 ? s.Length : -1;
        }

        return s.IndexOf(search, fromIndex, StringComparison.Ordinal);
    }

    /// <summary>
    /// The index of the last occurrence of <paramref name="search"/> at or before
    /// <paramref name="fromIndex"/>, with the reference's (java.lang.String's) edge rules for a
    /// start position outside the string.
    /// </summary>
    private static int JavaLastIndexOf(string search, string s, int fromIndex)
    {
        if (fromIndex < 0)
        {
            return -1;
        }

        if (search.Length == 0)
        {
            return Math.Min(fromIndex, s.Length);
        }

        if (fromIndex >= s.Length)
        {
            fromIndex = s.Length - 1;
        }

        return s.LastIndexOf(search, fromIndex, StringComparison.Ordinal);
    }

    private static string? InsertValue(string? s1, int start, int length, string? s2)
    {
        if (s1 == null)
        {
            return s2;
        }

        if (s2 == null)
        {
            return s1;
        }

        int len1 = s1.Length;
        int len2 = s2.Length;
        start--;
        if (start < 0 || length <= 0 || len2 == 0 || start > len1)
        {
            return s1;
        }

        if (start + length > len1)
        {
            length = len1 - start;
        }

        return s1[..start] + s2 + s1[(start + length)..];
    }

    private static string? TranslateValue(string? original, string? findChars, string? replaceChars)
    {
        if (StringUtils.IsNullOrEmpty(original) || StringUtils.IsNullOrEmpty(findChars))
        {
            return original;
        }

        // If it stays null, then no replacements have been made.
        StringBuilder? builder = null;

        // If shorter than findChars, then characters are removed (if null, we don't access
        // replaceChars at all).
        int replaceSize = replaceChars == null ? 0 : replaceChars.Length;
        for (int i = 0, size = original.Length; i < size; i++)
        {
            char ch = original[i];
            int index = findChars.IndexOf(ch);
            if (index >= 0)
            {
                if (builder == null)
                {
                    builder = new StringBuilder(size);
                    if (i > 0)
                    {
                        _ = builder.Append(original, 0, i);
                    }
                }

                if (index < replaceSize)
                {
                    ch = replaceChars![index];
                }
            }

            _ = builder?.Append(ch);
        }

        return builder == null ? original : builder.ToString();
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        switch (function)
        {
            case Locate:
                type = TypeInfo.TypeInteger;
                break;
            case Insert:
            case Replace:
            case Lpad:
            case Rpad:
            case Translate:
                type = TypeInfo.TypeVarchar;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        if (allConst)
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}

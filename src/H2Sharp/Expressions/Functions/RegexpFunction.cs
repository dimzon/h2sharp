/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.RegexpFunction, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * D-001/D-019: pattern and replacement go to BCL unchanged; no Java regex adapter.
 */
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>A regular expression function.</summary>
public sealed class RegexpFunction : FunctionN
{
    // Keep the reference casing to avoid a PascalCase collision with regexpReplace/regexpSubstr.
    public const int REGEXP_LIKE = 0;
    public const int REGEXP_REPLACE = REGEXP_LIKE + 1;
    public const int REGEXP_SUBSTR = REGEXP_REPLACE + 1;
    private static readonly string[] Names = ["REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR"];
    private readonly int function;

    /// <summary>Creates a regular expression function.</summary>
    public RegexpFunction(int function) : base(new Expression[function == REGEXP_LIKE ? 3 : 6]) => this.function = function;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v1 = args[0].GetValue(session);
        Value v2 = args[1].GetValue(session);
        int length = args.Length;
        switch (function)
        {
            case REGEXP_LIKE:
            {
                Value? v3 = length >= 3 ? args[2].GetValue(session) : null;
                if (v1 == ValueNull.Instance || v2 == ValueNull.Instance || v3 == ValueNull.Instance) return ValueNull.Instance;
                string regexp = v2.GetString()!;
                string? regexpMode = v3?.GetString();
                RegexOptions flags = MakeRegexpFlags(regexpMode, false);
                try { v1 = ValueBoolean.Get(new Regex(regexp, flags).IsMatch(v1.GetString()!)); }
                catch (ArgumentException e) { throw DbException.Get(ErrorCode.LikeEscapeError1, e, regexp); }
                break;
            }
            case REGEXP_REPLACE:
            {
                string? input = v1.GetString();
                if (session.GetMode().Enum == Mode.ModeEnum.Oracle)
                {
                    string? replacement = args[2].GetValue(session).GetString();
                    int position = length >= 4 ? args[3].GetValue(session).GetInt() : 1;
                    int occurrence = length >= 5 ? args[4].GetValue(session).GetInt() : 0;
                    string? regexpMode = length >= 6 ? args[5].GetValue(session).GetString() : null;
                    if (input is null) v1 = ValueNull.Instance;
                    else v1 = RegexpReplace(session, input, v2.GetString() ?? "", replacement ?? "", position, occurrence, regexpMode);
                }
                else
                {
                    if (length > 4) throw DbException.Get(ErrorCode.InvalidParameterCount2, GetName(), "3..4");
                    Value v3 = args[2].GetValue(session);
                    Value? v4 = length == 4 ? args[3].GetValue(session) : null;
                    if (v1 == ValueNull.Instance || v2 == ValueNull.Instance || v3 == ValueNull.Instance || v4 == ValueNull.Instance)
                        v1 = ValueNull.Instance;
                    else v1 = RegexpReplace(session, input!, v2.GetString()!, v3.GetString()!, 1, 0, v4?.GetString());
                }
                break;
            }
            case REGEXP_SUBSTR:
                v1 = RegexpSubstr(v1, v2, length >= 3 ? args[2].GetValue(session) : null,
                    length >= 4 ? args[3].GetValue(session) : null, length >= 5 ? args[4].GetValue(session) : null,
                    length >= 6 ? args[5].GetValue(session) : null, session);
                break;
            default: throw DbException.GetInternalError("function=" + function);
        }
        return v1;
    }

    private static Value RegexpReplace(SessionLocal session, string input, string regexp, string replacement,
        int position, int occurrence, string? regexpMode)
    {
        // D-019: no mode-dependent backslash/dollar rewrite. The replacement is BCL syntax.
        bool postgres = session.GetMode().Enum == Mode.ModeEnum.PostgreSql;
        RegexOptions flags = MakeRegexpFlags(regexpMode, postgres);
        if (postgres && (regexpMode is null || regexpMode.Length == 0 || !regexpMode.Contains('g'))) occurrence = 1;
        Regex regex;
        try { regex = new Regex(regexp, flags); } // D-001: regexp is passed as received.
        catch (ArgumentException e) { throw DbException.Get(ErrorCode.LikeEscapeError1, e, regexp); }
        try
        {
            if (position < 1 || position - 1 > input.Length) throw new ArgumentOutOfRangeException(nameof(position));
            if (occurrence == 0)
                // Native BCL startAt semantics. Unlike Java Matcher.replaceAll, this does not
                // reset a previously specified region to the full input; documented in B5.
                return ValueVarchar.Get(regex.Replace(input, replacement, -1, position - 1), session);
            Match match = regex.Match(input, position - 1);
            for (int index = 1; index < occurrence && match.Success; index++) match = match.NextMatch();
            if (!match.Success || occurrence < 0) return ValueVarchar.Get(input, session);
            // Match.Result expands the original replacement text using standard .NET rules.
            return ValueVarchar.Get(input[..match.Index] + match.Result(replacement) + input[(match.Index + match.Length)..], session);
        }
        catch (ArgumentException e) { throw DbException.Get(ErrorCode.LikeEscapeError1, e, replacement); }
    }

    private static Value RegexpSubstr(Value inputString, Value regexpArg, Value? positionArg, Value? occurrenceArg,
        Value? regexpModeArg, Value? subexpressionArg, SessionLocal session)
    {
        if (inputString == ValueNull.Instance || regexpArg == ValueNull.Instance || positionArg == ValueNull.Instance
            || occurrenceArg == ValueNull.Instance || subexpressionArg == ValueNull.Instance) return ValueNull.Instance;
        string regexp = regexpArg.GetString()!;
        int position = positionArg is not null ? positionArg.GetInt() - 1 : 0;
        int requestedOccurrence = occurrenceArg?.GetInt() ?? 1;
        string? regexpMode = regexpModeArg?.GetString();
        int subexpression = subexpressionArg?.GetInt() ?? 0;
        RegexOptions flags = MakeRegexpFlags(regexpMode, false);
        Regex regex;
        try { regex = new Regex(regexp, flags); }
        catch (ArgumentException e) { throw DbException.Get(ErrorCode.LikeEscapeError1, e, regexp); }
        string input = inputString.GetString()!;
        if (position < 0 || position > input.Length) return ValueNull.Instance;
        Match match = regex.Match(input, position);
        for (int occurrence = 1; occurrence < requestedOccurrence && match.Success; occurrence++) match = match.NextMatch();
        if (!match.Success || Array.IndexOf(regex.GetGroupNumbers(), subexpression) < 0) return ValueNull.Instance;
        Group group = match.Groups[subexpression];
        return group.Success ? ValueVarchar.Get(group.Value, session) : ValueNull.Instance;
    }

    private static RegexOptions MakeRegexpFlags(string? stringFlags, bool ignoreGlobalFlag)
    {
        // Pattern.UNICODE_CASE becomes native .NET invariant matching options. No inline flags
        // or any other text are inserted into the SQL pattern.
        RegexOptions flags = RegexOptions.CultureInvariant;
        if (stringFlags is not null)
            foreach (char flag in stringFlags)
                switch (flag)
                {
                    case 'i': flags |= RegexOptions.IgnoreCase; break;
                    case 'c': flags &= ~RegexOptions.IgnoreCase; break;
                    case 'n': flags |= RegexOptions.Singleline; break;
                    case 'm': flags |= RegexOptions.Multiline; break;
                    case 'g' when ignoreGlobalFlag: break;
                    default: throw DbException.Get(ErrorCode.InvalidValue2, stringFlags);
                }
        return flags;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        int min, max;
        switch (function)
        {
            case REGEXP_LIKE: min = 2; max = 3; type = TypeInfo.TypeBoolean; break;
            case REGEXP_REPLACE: min = 3; max = 6; type = TypeInfo.TypeVarchar; break;
            case REGEXP_SUBSTR: min = 2; max = 6; type = TypeInfo.TypeVarchar; break;
            default: throw DbException.GetInternalError("function=" + function);
        }
        int len = args.Length;
        if (len < min || len > max) throw DbException.Get(ErrorCode.InvalidParameterCount2, GetName(),
            min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture));
        return allConst ? TypedValueExpression.GetTypedIfNull(GetValue(session), type) : this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}

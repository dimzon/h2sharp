/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.CompareLike.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pattern matching comparison expression: WHERE NAME LIKE ?
 */
using System;
using System.Text;
using System.Text.RegularExpressions;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Pattern matching comparison expression: WHERE NAME LIKE ?
/// </summary>
public sealed class CompareLike : Condition
{
    /// <summary>
    /// The type of comparison.
    /// </summary>
    public enum LikeType
    {
        /// <summary>
        /// LIKE.
        /// </summary>
        Like,

        /// <summary>
        /// ILIKE (case-insensitive LIKE).
        /// </summary>
        ILike,

        /// <summary>
        /// REGEXP.
        /// </summary>
        Regexp,
    }

    private const int Match = 0, One = 1, Any = 2;

    // The reference's DEFAULT_ESCAPE setting, whose default is a single backslash.
    private const string DefaultEscape = "\\";

    /// <summary>
    /// Compiled regular expressions, keyed by the pattern and whether case is ignored. The
    /// reference compiles the pattern into a Pattern every time one initializes it; this cache is
    /// a recorded deviation (accepted, see issue #12 A1): the semantics of a compiled pattern do
    /// not depend on how it was produced, and a parameter pattern would otherwise pay the full
    /// interpretation cost per row. Compiled beyond what the reference does, for the same reason.
    /// </summary>
    private static readonly SmallLRUCache<(bool IgnoreCase, string Pattern), Regex> RegexCache =
        SmallLRUCache<(bool IgnoreCase, string Pattern), Regex>.NewInstance(100);


    private readonly CompareMode compareMode;

    private readonly string defaultEscape;

    private readonly LikeType likeType;

    private Expression left;

    private readonly bool not;

    private readonly bool whenOperand;

    private Expression right;

    private Expression? escape;

    private bool isInit;

    private char[]? patternChars;

    private string? patternString;

    /// <summary>
    /// one of MATCH / ONE / ANY
    /// </summary>
    private int[]? patternTypes;

    private int patternLength;

    private Regex? patternRegexp;

    private bool ignoreCase;

    private bool fastCompare;

    private bool invalidPattern;

    /// <summary>
    /// indicates that we can shortcut the comparison and use startsWith
    /// </summary>
    private bool shortcutToStartsWith;

    /// <summary>
    /// indicates that we can shortcut the comparison and use endsWith
    /// </summary>
    private bool shortcutToEndsWith;

    /// <summary>
    /// indicates that we can shortcut the comparison and use contains
    /// </summary>
    private bool shortcutToContains;

    /// <summary>
    /// Creates a pattern matching comparison.
    /// </summary>
    /// <param name="compareMode">the compare mode</param>
    /// <param name="left">the expression before LIKE</param>
    /// <param name="not">whether this is NOT LIKE</param>
    /// <param name="whenOperand">whether this is a when operand</param>
    /// <param name="right">the pattern</param>
    /// <param name="escape">the escape expression, or null</param>
    /// <param name="likeType">the type of comparison</param>
    public CompareLike(
        CompareMode compareMode, Expression left, bool not, bool whenOperand, Expression right,
        Expression? escape, LikeType likeType)
    {
        this.compareMode = compareMode;
        defaultEscape = DefaultEscape;
        this.likeType = likeType;
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
        this.right = right;
        this.escape = escape;
    }

    /// <summary>
    /// Creates a pattern matching comparison with an explicit default escape.
    /// </summary>
    /// <remarks>
    /// The reference also has a constructor taking a <c>Database</c>, which it uses to read
    /// <c>Settings.defaultEscape</c>; <c>Settings</c> is not ported, so the default is the
    /// reference's own default (empty).
    /// </remarks>
    /// <param name="compareMode">the compare mode</param>
    /// <param name="defaultEscape">the default escape</param>
    /// <param name="left">the expression before LIKE</param>
    /// <param name="not">whether this is NOT LIKE</param>
    /// <param name="whenOperand">whether this is a when operand</param>
    /// <param name="right">the pattern</param>
    /// <param name="escape">the escape expression, or null</param>
    /// <param name="likeType">the type of comparison</param>
    public CompareLike(
        CompareMode compareMode, string defaultEscape, Expression left, bool not, bool whenOperand,
        Expression right, Expression? escape, LikeType likeType)
    {
        this.compareMode = compareMode;
        this.defaultEscape = defaultEscape;
        this.likeType = likeType;
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
        this.right = right;
        this.escape = escape;
    }

    private static char? GetEscapeChar(string s)
        => s.Length == 0 ? null : s[0];

    /// <inheritdoc/>
    public override bool NeedParentheses() => true;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        if (not)
        {
            _ = builder.Append(" NOT");
        }

        switch (likeType)
        {
            case LikeType.Like:
            case LikeType.ILike:
                _ = builder.Append(likeType == LikeType.Like ? " LIKE " : " ILIKE ");
                right.GetSql(builder, sqlFlags, AutoParentheses);
                if (escape is not null)
                {
                    escape.GetSql(builder.Append(" ESCAPE "), sqlFlags, AutoParentheses);
                }

                break;
            case LikeType.Regexp:
                _ = builder.Append(" REGEXP ");
                right.GetSql(builder, sqlFlags, AutoParentheses);
                break;
            default:
                throw Message.DbException.GetUnsupportedException(likeType.ToString());
        }

        return builder;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        if (likeType == LikeType.ILike || left.GetTypeInfo().GetValueType() == Value.VarcharIgnorecase)
        {
            ignoreCase = true;
        }

        if (escape is not null)
        {
            escape = escape.Optimize(session);
        }

        if (whenOperand)
        {
            return this;
        }

        if (left.IsValueSet())
        {
            Value l = left.GetValue(session);
            if (l is ValueNull)
            {
                // NULL LIKE something > NULL
                return TypedValueExpression.Unknown;
            }
        }

        if (right.IsValueSet() && (escape is null || escape.IsValueSet()))
        {
            if (left.IsValueSet())
            {
                return ValueExpression.Get(ValueBoolean.Get(GetValue(session).GetBoolean()));
            }

            Value r = right.GetValue(session);
            if (r is ValueNull)
            {
                // something LIKE NULL > NULL
                return TypedValueExpression.Unknown;
            }

            Value? e = escape is null ? null : escape.GetValue(session);
            if (e is ValueNull)
            {
                return TypedValueExpression.Unknown;
            }

            string p = r.GetString()!;
            InitPattern(p, GetEscapeChar(e));
            if (invalidPattern)
            {
                return TypedValueExpression.Unknown;
            }

            if (likeType != LikeType.Regexp && p == "%")
            {
                // optimization for X LIKE '%'
                return new SearchedCase(
                [
                    new ConditionIsNull(left, not: true, whenOperand: false),
                    ValueExpression.Get(ValueBoolean.Get(!not)),
                    TypedValueExpression.Unknown,
                ]).Optimize(session);
            }

            if (IsFullMatch())
            {
                // optimization for X LIKE 'Hello': convert to X = 'Hello'
                Value value = ignoreCase ? ValueVarcharIgnoreCase.Get(patternString!) : ValueVarchar.Get(patternString!);
                Expression expr = ValueExpression.Get(value);
                return new Comparison(not ? Comparison.NotEqual : Comparison.Equal, left, expr, false)
                    .Optimize(session);
            }

            isInit = true;
        }

        return this;
    }

    private char? GetEscapeChar(Value? e)
    {
        if (e is null)
        {
            return GetEscapeChar(defaultEscape);
        }

        string? es = e.GetString();
        char? esc;
        if (es is null)
        {
            esc = GetEscapeChar(defaultEscape);
        }
        else if (es.Length == 0)
        {
            esc = null;
        }
        else if (es.Length > 1)
        {
            throw Message.DbException.Get(Api.ErrorCode.LikeEscapeError1, es);
        }
        else
        {
            esc = es[0];
        }

        return esc;
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (not || whenOperand || likeType == LikeType.Regexp || left is not ExpressionColumn)
        {
            return;
        }

        ExpressionColumn l = (ExpressionColumn)left;
        if (filter != l.GetTableFilter()
            || !TypeInfo.HaveSameOrdering(
                l.GetTypeInfo(), ignoreCase ? TypeInfo.TypeVarcharIgnorecase : TypeInfo.TypeVarchar))
        {
            return;
        }

        // parameters are always evaluatable, but we need to check if the value is set (at prepare
        // time) otherwise we would need to prepare at execute time, which may be slower.
        if (!right.IsEverything(ExpressionVisitor.IndependentVisitor))
        {
            return;
        }

        if (escape is not null && !escape.IsEverything(ExpressionVisitor.IndependentVisitor))
        {
            return;
        }

        string p = right.GetValue(session).GetString()!;
        if (!isInit)
        {
            Value? e = escape is null ? null : escape.GetValue(session);
            if (e is ValueNull)
            {
                // should already be optimized
                throw Message.DbException.GetInternalError();
            }

            InitPattern(p, GetEscapeChar(e));
        }

        if (invalidPattern)
        {
            return;
        }

        if (patternLength <= 0 || patternTypes![0] != Match)
        {
            // can't use an index
            return;
        }

        if (!DataType.IsStringType(l.GetColumn()!.GetTypeInfo().GetValueType()))
        {
            // column is not a varchar - can't use the index
            return;
        }

        // Get the MATCH prefix and see if we can create an index condition from that.
        int maxMatch = 0;
        StringBuilder buff = new();
        while (maxMatch < patternLength && patternTypes![maxMatch] == Match)
        {
            _ = buff.Append(patternChars![maxMatch++]);
        }

        string begin = buff.ToString();
        if (maxMatch == patternLength)
        {
            filter.AddIndexCondition(IndexCondition.Get(
                Comparison.Equal, l, ValueExpression.Get(ValueVarchar.Get(begin))));
        }
        else if (begin.Length > 0)
        {
            // TODO check if this is correct according to Unicode rules (code points)
            char next = begin[begin.Length - 1];
            for (int i = 1; i < 2000; i++)
            {
                string end = begin[..^1] + (char)(next + i);
                if (compareMode.CompareString(begin, end, ignoreCase) < 0)
                {
                    filter.AddIndexCondition(IndexCondition.Get(
                        Comparison.BiggerEqual, l, ValueExpression.Get(ValueVarchar.Get(begin))));
                    filter.AddIndexCondition(IndexCondition.Get(
                        Comparison.Smaller, l, ValueExpression.Get(ValueVarchar.Get(end))));
                    break;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
        => GetValue(session, left.GetValue(session));

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value left)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, left);
        }

        return GetValue(session, left).GetBoolean();
    }

    private Value GetValue(SessionLocal session, Value left)
    {
        if (left is ValueNull)
        {
            return ValueNull.Instance;
        }

        if (!isInit)
        {
            Value r = right.GetValue(session);
            if (r is ValueNull)
            {
                return ValueNull.Instance;
            }

            string p = r.GetString()!;
            Value? e = escape is null ? null : escape.GetValue(session);
            if (e is ValueNull)
            {
                return ValueNull.Instance;
            }

            InitPattern(p, GetEscapeChar(e));
        }

        if (invalidPattern)
        {
            return ValueNull.Instance;
        }

        string value = left.GetString()!;
        bool result;
        if (likeType == LikeType.Regexp)
        {
            result = patternRegexp!.IsMatch(value);
        }
        else if (shortcutToStartsWith)
        {
            result = RegionMatches(value, ignoreCase, 0, patternString!, 0, patternLength - 1);
        }
        else if (shortcutToEndsWith)
        {
            result = RegionMatches(
                value, ignoreCase, value.Length - patternLength + 1, patternString!, 1, patternLength - 1);
        }
        else if (shortcutToContains)
        {
            // The slice view, not a substring: the reference builds one (substring(1, len-1)),
            // and a row-shape shortcut runs per row, so the allocation is cut here
            // (issue #12 A2) while the comparison stays byte-identical.
            result = ignoreCase
                ? ContainsIgnoreCase(value, patternString!, 1, patternLength - 2)
                : value.AsSpan().IndexOf(
                    patternString!.AsSpan(1, patternLength - 2), StringComparison.Ordinal) >= 0;
        }
        else
        {
            result = CompareAt(value, 0, 0, value.Length, patternChars!, patternTypes!);
        }

        return ValueBoolean.Get(not ^ result);
    }

    private static bool RegionMatches(
        string s, bool ignoreCase, int toffset, string other, int ooffset, int len)
    {
        if (toffset < 0 || ooffset < 0 || toffset + len > s.Length || ooffset + len > other.Length)
        {
            return false;
        }

        return string.Compare(
            s, toffset, other, ooffset, len,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0;
    }

    private static bool ContainsIgnoreCase(string src, string what, int start, int length)
    {
        if (length == 0)
        {
            // Empty string is contained
            return true;
        }

        char firstLo = char.ToLowerInvariant(what[start]);
        char firstUp = char.ToUpperInvariant(what[start]);
        for (int i = src.Length - length; i >= 0; i--)
        {
            // Quick check before calling the more expensive regionMatches()
            char ch = src[i];
            if (ch != firstLo && ch != firstUp)
            {
                continue;
            }

            if (RegionMatches(src, true, i, what, start, length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the compiled regular expression for a pattern, cached across uses.
    /// </summary>
    /// <param name="p">the pattern</param>
    /// <param name="ignoreCase">whether case is ignored</param>
    /// <returns>the compiled expression</returns>
    private static Regex GetCompiledRegex(string p, bool ignoreCase)
    {
        // CultureInvariant: the reference's Pattern.compile is locale-independent, and .NET's
        // IgnoreCase is not (a Turkish dotless i and the like). Compiled: the same deviation the
        // cache is (issue #12 A1) — a compiled Regex answers the same as an interpreted one.
        static Regex Compile(string p, bool ignoreCase) => ignoreCase
            ? new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant);

        (bool, string) key = (ignoreCase, p);
        lock (RegexCache)
        {
            if (RegexCache.TryGetValue(key, out Regex? cached))
            {
                return cached;
            }
        }

        Regex compiled = Compile(p, ignoreCase);
        lock (RegexCache)
        {
            if (RegexCache.TryGetValue(key, out Regex? raced))
            {
                return raced;
            }

            _ = RegexCache.Put(key, compiled);
            return compiled;
        }
    }


    private bool CompareAt(string s, int pi, int si, int sLen, char[] pattern, int[] types)
    {
        for (; pi < patternLength; pi++)
        {
            switch (types[pi])
            {
                case Match:
                    if (si >= sLen || !Compare(pattern, s, pi, si++))
                    {
                        return false;
                    }

                    break;
                case One:
                    if (si++ >= sLen)
                    {
                        return false;
                    }

                    break;
                case Any:
                    if (++pi >= patternLength)
                    {
                        return true;
                    }

                    while (si < sLen)
                    {
                        if (Compare(pattern, s, pi, si) && CompareAt(s, pi, si, sLen, pattern, types))
                        {
                            return true;
                        }

                        si++;
                    }

                    return false;
                default:
                    throw Message.DbException.GetInternalError(types[pi].ToString());
            }
        }

        return si == sLen;
    }

    private bool Compare(char[] pattern, string s, int pi, int si)
        => pattern[pi] == s[si] || (!fastCompare && compareMode.EqualsChars(patternString!, pi, s, si, ignoreCase));

    /// <inheritdoc/>
    public override bool IsWhenConditionOperand() => whenOperand;

    /// <summary>
    /// Tests if the value matches the pattern.
    /// </summary>
    /// <param name="testPattern">the pattern</param>
    /// <param name="value">the value</param>
    /// <param name="escapeChar">the escape character</param>
    /// <returns>true if the value matches</returns>
    public bool Test(string testPattern, string value, char escapeChar)
    {
        InitPattern(testPattern, escapeChar);
        return Test(value);
    }

    /// <summary>
    /// Tests if the value matches the initialized pattern.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>true if the value matches</returns>
    public bool Test(string value)
    {
        if (invalidPattern)
        {
            return false;
        }

        return CompareAt(value, 0, 0, value.Length, patternChars!, patternTypes!);
    }

    /// <summary>
    /// Initializes the pattern.
    /// </summary>
    /// <param name="p">the pattern</param>
    /// <param name="escapeChar">the escape character</param>
    public void InitPattern(string? p, char? escapeChar)
    {
        if (compareMode.GetName() == CompareMode.Off && !ignoreCase)
        {
            fastCompare = true;
        }

        if (likeType == LikeType.Regexp)
        {
            patternString = p;
            try
            {
                patternRegexp = GetCompiledRegex(p!, ignoreCase);
            }
            catch (ArgumentException)
            {
                throw Message.DbException.Get(Api.ErrorCode.LikeEscapeError1, p);
            }

            return;
        }

        patternLength = 0;
        if (p is null)
        {
            patternTypes = null;
            patternChars = null;
            return;
        }

        int len = p.Length;
        patternChars = new char[len];
        patternTypes = new int[len];
        bool lastAny = false;
        for (int i = 0; i < len; i++)
        {
            char c = p[i];
            int type;
            if (escapeChar is not null && escapeChar.Value == c)
            {
                if (i >= len - 1)
                {
                    invalidPattern = true;
                    return;
                }

                c = p[++i];
                type = Match;
                lastAny = false;
            }
            else if (c == '%')
            {
                if (lastAny)
                {
                    continue;
                }

                type = Any;
                lastAny = true;
            }
            else if (c == '_')
            {
                type = One;
            }
            else
            {
                type = Match;
                lastAny = false;
            }

            patternTypes[patternLength] = type;
            patternChars[patternLength++] = c;
        }

        for (int i = 0; i < patternLength - 1; i++)
        {
            if (patternTypes[i] == Any && patternTypes[i + 1] == One)
            {
                patternTypes[i] = One;
                patternTypes[i + 1] = Any;
            }
        }

        patternString = new string(patternChars, 0, patternLength);

        // Clear optimizations
        shortcutToStartsWith = false;
        shortcutToEndsWith = false;
        shortcutToContains = false;

        // optimizes the common case of LIKE 'foo%'
        if (compareMode.GetName() == CompareMode.Off && patternLength > 1)
        {
            int maxMatch = 0;
            while (maxMatch < patternLength && patternTypes[maxMatch] == Match)
            {
                maxMatch++;
            }

            if (maxMatch == patternLength - 1 && patternTypes[patternLength - 1] == Any)
            {
                shortcutToStartsWith = true;
                return;
            }
        }

        // optimizes the common case of LIKE '%foo'
        if (compareMode.GetName() == CompareMode.Off && patternLength > 1)
        {
            if (patternTypes[0] == Any)
            {
                int maxMatch = 1;
                while (maxMatch < patternLength && patternTypes[maxMatch] == Match)
                {
                    maxMatch++;
                }

                if (maxMatch == patternLength)
                {
                    shortcutToEndsWith = true;
                    return;
                }
            }
        }

        // optimizes the common case of LIKE '%foo%'
        if (compareMode.GetName() == CompareMode.Off && patternLength > 2)
        {
            if (patternTypes[0] == Any)
            {
                int maxMatch = 1;
                while (maxMatch < patternLength && patternTypes[maxMatch] == Match)
                {
                    maxMatch++;
                }

                if (maxMatch == patternLength - 1 && patternTypes[patternLength - 1] == Any)
                {
                    shortcutToContains = true;
                }
            }
        }
    }

    private bool IsFullMatch()
    {
        if (patternTypes is null)
        {
            return false;
        }

        foreach (int type in patternTypes)
        {
            if (type != Match)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new CompareLike(compareMode, defaultEscape, left, !not, false, right, escape, likeType);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        left.MapColumns(resolver, level, state);
        right.MapColumns(resolver, level, state);
        escape?.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        left.SetEvaluatable(tableFilter, b);
        right.SetEvaluatable(tableFilter, b);
        escape?.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        left.UpdateAggregate(session, stage);
        right.UpdateAggregate(session, stage);
        escape?.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => left.IsEverything(visitor) && right.IsEverything(visitor)
            && (escape is null || escape.IsEverything(visitor));

    /// <inheritdoc/>
    public override int GetCost() => left.GetCost() + right.GetCost() + 3;

    /// <inheritdoc/>
    public override int GetSubexpressionCount() => escape is null ? 2 : 3;

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        switch (index)
        {
            case 0:
                return left;
            case 1:
                return right;
            case 2:
                if (escape is not null)
                {
                    return escape;
                }

                goto default;
            default:
                throw new System.IndexOutOfRangeException();
        }
    }
}

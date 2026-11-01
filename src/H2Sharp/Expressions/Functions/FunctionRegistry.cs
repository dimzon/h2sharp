/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Data-driven entries for built-in functions whose grammar is simply a fixed or bounded
 * argument list. Functions that read keywords, flags, field names or JSON parameters keep
 * their own parser branches.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

internal sealed class FunctionRegistration
{
    internal FunctionRegistration(
        string name, int minArguments, int maxArguments, Func<Expression[], Expression> create)
    {
        Name = name;
        MinArguments = minArguments;
        MaxArguments = maxArguments;
        Create = create;
    }

    internal string Name { get; }

    internal int MinArguments { get; }

    internal int MaxArguments { get; }

    internal Func<Expression[], Expression> Create { get; }
}

internal static class FunctionRegistry
{
    private static readonly Dictionary<string, FunctionRegistration> Functions = Create();

    internal static bool TryGet(string name, out FunctionRegistration registration)
        => Functions.TryGetValue(name, out registration!);

    private static Dictionary<string, FunctionRegistration> Create()
    {
        Dictionary<string, FunctionRegistration> functions = new(StringComparer.OrdinalIgnoreCase);

        Add(functions, "ABS", 1, 1, a => new MathFunction(a[0], null, MathFunction.Abs));
        Add(functions, "MOD", 2, 2, a => new MathFunction(a[0], a[1], MathFunction.Mod));
        Add(functions, "SIN", 1, 1, a => new MathFunction1(a[0], MathFunction1.Sin));
        Add(functions, "COS", 1, 1, a => new MathFunction1(a[0], MathFunction1.Cos));
        Add(functions, "TAN", 1, 1, a => new MathFunction1(a[0], MathFunction1.Tan));
        Add(functions, "COT", 1, 1, a => new MathFunction1(a[0], MathFunction1.Cot));
        Add(functions, "SINH", 1, 1, a => new MathFunction1(a[0], MathFunction1.Sinh));
        Add(functions, "COSH", 1, 1, a => new MathFunction1(a[0], MathFunction1.Cosh));
        Add(functions, "TANH", 1, 1, a => new MathFunction1(a[0], MathFunction1.Tanh));
        Add(functions, "ASIN", 1, 1, a => new MathFunction1(a[0], MathFunction1.Asin));
        Add(functions, "ACOS", 1, 1, a => new MathFunction1(a[0], MathFunction1.Acos));
        Add(functions, "ATAN", 1, 1, a => new MathFunction1(a[0], MathFunction1.Atan));
        Add(functions, "ATAN2", 2, 2, a => new MathFunction2(a[0], a[1], MathFunction2.Atan2));
        Add(functions, "LOG10", 1, 1, a => new MathFunction1(a[0], MathFunction1.Log10));
        Add(functions, "LN", 1, 1, a => new MathFunction1(a[0], MathFunction1.Ln));
        Add(functions, "EXP", 1, 1, a => new MathFunction1(a[0], MathFunction1.Exp));
        Add(functions, "POWER", 2, 2, a => new MathFunction2(a[0], a[1], MathFunction2.Power));
        Add(functions, "SQRT", 1, 1, a => new MathFunction1(a[0], MathFunction1.Sqrt));
        Add(functions, "FLOOR", 1, 1, a => new MathFunction(a[0], null, MathFunction.Floor));
        Add(functions, "CEIL", 1, 1, a => new MathFunction(a[0], null, MathFunction.Ceil));
        Add(functions, "CEILING", 1, 1, a => new MathFunction(a[0], null, MathFunction.Ceil));
        Add(functions, "ROUND", 1, 2, a => new MathFunction(
            a[0], a.Length > 1 ? a[1] : null, MathFunction.Round));
        Add(functions, "ROUNDMAGIC", 1, 1, a => new MathFunction(a[0], null, MathFunction.Roundmagic));
        Add(functions, "SIGN", 1, 1, a => new MathFunction(a[0], null, MathFunction.Sign));
        Add(functions, "TRUNC", 1, 2, a => new MathFunction(
            a[0], a.Length > 1 ? a[1] : null, MathFunction.Trunc));
        Add(functions, "TRUNCATE", 1, 2, a => new MathFunction(
            a[0], a.Length > 1 ? a[1] : null, MathFunction.Trunc));
        Add(functions, "DEGREES", 1, 1, a => new MathFunction1(a[0], MathFunction1.Degrees));
        Add(functions, "RADIANS", 1, 1, a => new MathFunction1(a[0], MathFunction1.Radians));

        Add(functions, "BITAND", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitand));
        Add(functions, "BITOR", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitor));
        Add(functions, "BITXOR", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitxor));
        Add(functions, "BITNOT", 1, 1, a => new BitFunction(a[0], null, BitFunction.Bitnot));
        Add(functions, "BITNAND", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitnand));
        Add(functions, "BITNOR", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitnor));
        Add(functions, "BITXNOR", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitxnor));
        Add(functions, "BITGET", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Bitget));
        Add(functions, "BITCOUNT", 1, 1, a => new BitFunction(a[0], null, BitFunction.Bitcount));
        Add(functions, "LSHIFT", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Lshift));
        Add(functions, "RSHIFT", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Rshift));
        Add(functions, "ULSHIFT", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Ulshift));
        Add(functions, "URSHIFT", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Urshift));
        Add(functions, "ROTATELEFT", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Rotateleft));
        Add(functions, "ROTATERIGHT", 2, 2, a => new BitFunction(a[0], a[1], BitFunction.Rotateright));

        Add(functions, "DAYNAME", 1, 1, a => new DayMonthNameFunction(a[0], DayMonthNameFunction.DayName));
        Add(functions, "MONTHNAME", 1, 1, a => new DayMonthNameFunction(a[0], DayMonthNameFunction.MonthName));
        Add(functions, "CARDINALITY", 1, 1, a => new CardinalityExpression(a[0], false));
        Add(functions, "ARRAY_MAX_CARDINALITY", 1, 1, a => new CardinalityExpression(a[0], true));

        Add(functions, "LOCATE", 2, 3, a => new StringFunction(
            a[0], a[1], a.Length > 2 ? a[2] : null, StringFunction.Locate));
        Add(functions, "INSERT", 4, 4, a => new StringFunction(
            a[0], a[1], a[2], a[3], StringFunction.Insert));
        Add(functions, "REPLACE", 2, 3, a => new StringFunction(
            a[0], a[1], a.Length > 2 ? a[2] : null, StringFunction.Replace));
        Add(functions, "LPAD", 2, 3, a => new StringFunction(
            a[0], a[1], a.Length > 2 ? a[2] : null, StringFunction.Lpad));
        Add(functions, "RPAD", 2, 3, a => new StringFunction(
            a[0], a[1], a.Length > 2 ? a[2] : null, StringFunction.Rpad));
        Add(functions, "TRANSLATE", 3, 3, a => new StringFunction(
            a[0], a[1], a[2], StringFunction.Translate));

        Add(functions, "UPPER", 1, 1, a => new StringFunction1(a[0], StringFunction1.Upper));
        Add(functions, "LOWER", 1, 1, a => new StringFunction1(a[0], StringFunction1.Lower));
        Add(functions, "ASCII", 1, 1, a => new StringFunction1(a[0], StringFunction1.Ascii));
        Add(functions, "CHAR", 1, 1, a => new StringFunction1(a[0], StringFunction1.Char));
        Add(functions, "CHR", 1, 1, a => new StringFunction1(a[0], StringFunction1.Char));
        Add(functions, "STRINGENCODE", 1, 1, a => new StringFunction1(a[0], StringFunction1.Stringencode));
        Add(functions, "STRINGDECODE", 1, 1, a => new StringFunction1(a[0], StringFunction1.Stringdecode));
        Add(functions, "STRINGTOUTF8", 1, 1, a => new StringFunction1(a[0], StringFunction1.Stringtoutf8));
        Add(functions, "UTF8TOSTRING", 1, 1, a => new StringFunction1(a[0], StringFunction1.Utf8tostring));
        Add(functions, "HEXTORAW", 1, 1, a => new StringFunction1(a[0], StringFunction1.Hextoraw));
        Add(functions, "RAWTOHEX", 1, 1, a => new StringFunction1(a[0], StringFunction1.Rawtohex));
        Add(functions, "SPACE", 1, 1, a => new StringFunction1(a[0], StringFunction1.Space));
        Add(functions, "QUOTE_IDENT", 1, 1, a => new StringFunction1(a[0], StringFunction1.QuoteIdent));
        Add(functions, "REPEAT", 2, 2, a => new StringFunction2(a[0], a[1], StringFunction2.Repeat));

        Add(functions, "SOUNDEX", 1, 1, a => new SoundexFunction(a[0], null, SoundexFunction.Soundex));
        Add(functions, "DIFFERENCE", 2, 2, a => new SoundexFunction(a[0], a[1], SoundexFunction.Difference));
        Add(functions, "NULLIF", 2, 2, a => new NullIfFunction(a[0], a[1]));

        Add(functions, "TRIM_ARRAY", 2, 2, a => new ArrayFunction(
            a[0], a[1], null, ArrayFunction.TrimArray));
        Add(functions, "ARRAY_CONTAINS", 2, 2, a => new ArrayFunction(
            a[0], a[1], null, ArrayFunction.ArrayContains));
        Add(functions, "ARRAY_SLICE", 3, 3, a => new ArrayFunction(
            a[0], a[1], a[2], ArrayFunction.ArraySlice));

        Add(functions, "COMPRESS", 1, 2, a => new CompressFunction(
            a[0], a.Length > 1 ? a[1] : null, CompressFunction.Compress));
        Add(functions, "EXPAND", 1, 1, a => new CompressFunction(a[0], null, CompressFunction.Expand));

        Add(functions, "ABORT_SESSION", 1, 1, a => new SessionControlFunction(a[0], SessionControlFunction.AbortSession));
        Add(functions, "CANCEL_SESSION", 1, 1, a => new SessionControlFunction(a[0], SessionControlFunction.CancelSession));
        Add(functions, "DISK_SPACE_USED", 1, 1, a => new TableInfoFunction(a[0], null, TableInfoFunction.DiskSpaceUsed));
        Add(functions, "ESTIMATED_ENVELOPE", 2, 2, a => new TableInfoFunction(
            a[0], a[1], TableInfoFunction.EstimatedEnvelope));

        Add(functions, "DATA_TYPE_SQL", 4, 4, a => new DataTypeSQLFunction(a[0], a[1], a[2], a[3]));
        Add(functions, "SIGNAL", 2, 2, a => new SignalFunction(a[0], a[1]));
        Add(functions, "TRUNCATE_VALUE", 3, 3, a => new TruncateValueFunction(a[0], a[1], a[2]));

        Add(functions, "ZERO", 0, 0, _ => ValueExpression.Get(ValueInteger.Get(0)));
        Add(functions, "PI", 0, 0, _ => ValueExpression.Get(ValueDouble.Get(Math.PI)));
        Add(functions, "AUTOCOMMIT", 0, 0, _ => new SysInfoFunction(SysInfoFunction.Autocommit));
        Add(functions, "DATABASE_PATH", 0, 0, _ => new SysInfoFunction(SysInfoFunction.DatabasePath));
        Add(functions, "H2VERSION", 0, 0, _ => new SysInfoFunction(SysInfoFunction.H2version));
        Add(functions, "LOCK_MODE", 0, 0, _ => new SysInfoFunction(SysInfoFunction.LockMode));
        Add(functions, "LOCK_TIMEOUT", 0, 0, _ => new SysInfoFunction(SysInfoFunction.LockTimeout));
        Add(functions, "MEMORY_FREE", 0, 0, _ => new SysInfoFunction(SysInfoFunction.MemoryFree));
        Add(functions, "MEMORY_USED", 0, 0, _ => new SysInfoFunction(SysInfoFunction.MemoryUsed));
        Add(functions, "READONLY", 0, 0, _ => new SysInfoFunction(SysInfoFunction.Readonly));
        Add(functions, "SESSION_ID", 0, 0, _ => new SysInfoFunction(SysInfoFunction.SessionId));
        Add(functions, "TRANSACTION_ID", 0, 0, _ => new SysInfoFunction(SysInfoFunction.TransactionId));

        return functions;
    }

    private static void Add(
        Dictionary<string, FunctionRegistration> functions,
        string name,
        int minArguments,
        int maxArguments,
        Func<Expression[], Expression> create)
        => functions[name] = new FunctionRegistration(name, minArguments, maxArguments, create);
}

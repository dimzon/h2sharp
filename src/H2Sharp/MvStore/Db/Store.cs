/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.Store.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to getInvalidExpressionTypeException, the one static of the reference class that the
 * ported function layer calls. The rest of Store — the MVStore, its tables and their lifecycle —
 * is not ported; the file exists so that the call sites name the reference's method instead of
 * each inlining it (BitFunction inlined it earlier for want of this class).
 *
 * The reference takes a Typed, which is an Expression or a Value. Every call site in the
 * reference — MathFunction, DateTimeFunction, ArrayFunction, BitFunction, FieldReference,
 * ArrayElementReference — passes an Expression, so this takes one too; the other half of the
 * instance-of in the reference's UNKNOWN branch cannot happen here.
 */
using H2Sharp.Api;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// The database storage. Only the expression type check the function layer needs is ported.
/// </summary>
public sealed partial class Store
{
    /// <summary>
    /// The error for an expression of a type a function cannot take, which names the offending
    /// expression when its type is not even known.
    /// </summary>
    /// <param name="param">the parameter the expression was passed to</param>
    /// <param name="e">the expression</param>
    /// <returns>the error, which is not thrown by this method</returns>
    public static H2SharpException GetInvalidExpressionTypeException(string param, Expression e)
    {
        TypeInfo type = e.GetTypeInfo();
        if (type.GetValueType() == Value.Unknown)
        {
            return DbException.Get(ErrorCode.UnknownDataType1, e.GetTraceSql());
        }

        return DbException.Get(ErrorCode.InvalidValue2, type.GetTraceSql(), param);
    }
}

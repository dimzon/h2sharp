/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.SearchedCase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A searched case.
/// </summary>
public sealed class SearchedCase : OperationN
{
    /// <summary>
    /// Creates an empty searched case.
    /// </summary>
    public SearchedCase()
        : base(new Expression[4])
    {
    }

    /// <summary>
    /// Creates a searched case with the given arguments.
    /// </summary>
    /// <param name="args">the arguments</param>
    public SearchedCase(Expression[] args)
        : base(args)
    {
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        int len = args.Length - 1;
        for (int i = 0; i < len; i += 2)
        {
            if (args[i].GetBooleanValue(session))
            {
                return args[i + 1].GetValue(session).CastToValue(type!, session);
            }
        }

        if ((len & 1) == 0)
        {
            return args[len].GetValue(session).CastToValue(type!, session);
        }

        return ValueNull.Instance;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        TypeInfo typeInfo = TypeInfo.TypeUnknown;
        int len = args.Length - 1;
        bool allConst = true;
        for (int i = 0; i < len; i += 2)
        {
            Expression condition = args[i].Optimize(session);
            Expression result = args[i + 1].Optimize(session);
            if (allConst)
            {
                if (condition.IsConstant())
                {
                    if (condition.GetBooleanValue(session))
                    {
                        return result;
                    }
                }
                else
                {
                    allConst = false;
                }
            }

            args[i] = condition;
            args[i + 1] = result;
            typeInfo = CombineTypes(typeInfo, result);
        }

        if ((len & 1) == 0)
        {
            Expression result = args[len].Optimize(session);
            if (allConst)
            {
                return result;
            }

            args[len] = result;
            typeInfo = CombineTypes(typeInfo, result);
        }
        else if (allConst)
        {
            return ValueExpression.Null;
        }

        if (typeInfo.GetValueType() == Value.Unknown)
        {
            typeInfo = TypeInfo.TypeVarchar;
        }

        type = typeInfo;
        return this;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("CASE");
        int len = args.Length - 1;
        for (int i = 0; i < len; i += 2)
        {
            _ = builder.Append(" WHEN ");
            args[i].GetUnenclosedSql(builder, sqlFlags);
            _ = builder.Append(" THEN ");
            args[i + 1].GetUnenclosedSql(builder, sqlFlags);
        }

        if ((len & 1) == 0)
        {
            _ = builder.Append(" ELSE ");
            args[len].GetUnenclosedSql(builder, sqlFlags);
        }

        return builder.Append(" END");
    }

    /// <summary>
    /// Combines the result type with the type of one branch.
    /// </summary>
    /// <remarks>
    /// The reference keeps this on <c>SimpleCase</c>, which is not ported; the arithmetic is the
    /// reference's and is what <c>CASE</c> needs here.
    /// </remarks>
    private static TypeInfo CombineTypes(TypeInfo typeInfo, Expression e)
    {
        if (!e.IsNullConstant())
        {
            TypeInfo t = e.GetTypeInfo();
            int valueType = t.GetValueType();
            if (valueType != Value.Unknown && valueType != Value.Null)
            {
                typeInfo = TypeInfo.GetHigherType(typeInfo, t);
            }
        }

        return typeInfo;
    }
}

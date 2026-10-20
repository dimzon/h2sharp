/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.BinaryOperation.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A mathematical expression.
 *
 * What is left out, and why: the operations over intervals and over date and time values
 * need the classes that compute them, and those are not ported yet. The operations over
 * numbers are here, and the ones that are not refuse by name.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A mathematical expression: the sum, the difference, the product or the quotient of two
/// expressions.
/// </summary>
public class BinaryOperation : Operation2
{
    /// <summary>
    /// The kind of the operation.
    /// </summary>
    public enum OpType
    {
        /// <summary>
        /// This operation represents an addition as in 1 + 2.
        /// </summary>
        PLUS,

        /// <summary>
        /// This operation represents a subtraction as in 2 - 1.
        /// </summary>
        MINUS,

        /// <summary>
        /// This operation represents a multiplication as in 2 * 3.
        /// </summary>
        MULTIPLY,

        /// <summary>
        /// This operation represents a division as in 4 / 2.
        /// </summary>
        DIVIDE
    }

    private readonly OpType opType;

    private bool convertRight = true;

    /// <summary>
    /// Creates a mathematical expression.
    /// </summary>
    /// <param name="opType">the kind of the operation</param>
    /// <param name="left">the left argument</param>
    /// <param name="right">the right argument</param>
    public BinaryOperation(OpType opType, Expression left, Expression right)
        : base(left, right)
    {
        this.opType = opType;
    }

    /// <inheritdoc/>
    public override bool NeedParentheses() => true;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        // The space stays: without it the text could become "--1", which is a remark.
        left.GetSql(builder, sqlFlags, Expression.WithParentheses).Append(' ')
            .Append(GetOperationToken()).Append(' ');
        return right.GetSql(builder, sqlFlags, Expression.WithParentheses);
    }

    private string GetOperationToken() => opType switch
    {
        OpType.PLUS => "+",
        OpType.MINUS => "-",
        OpType.MULTIPLY => "*",
        OpType.DIVIDE => "/",
        _ => throw DbException.GetInternalError("opType=" + opType),
    };

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value l = left.GetValue(session).ConvertToType(type!);
        Value r = right.GetValue(session);
        if (convertRight)
        {
            r = r.ConvertToType(type!);
        }

        switch (opType)
        {
            case OpType.PLUS:
                if (l is ValueNull || r is ValueNull)
                {
                    return ValueNull.Instance;
                }

                return l.Add(r);
            case OpType.MINUS:
                if (l is ValueNull || r is ValueNull)
                {
                    return ValueNull.Instance;
                }

                return l.Subtract(r);
            case OpType.MULTIPLY:
                if (l is ValueNull || r is ValueNull)
                {
                    return ValueNull.Instance;
                }

                return l.Multiply(r);
            case OpType.DIVIDE:
                if (l is ValueNull || r is ValueNull)
                {
                    return ValueNull.Instance;
                }

                return l.Divide(r, type!);
            default:
                throw DbException.GetInternalError("type=" + opType);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        TypeInfo leftType = left.GetTypeInfo();
        TypeInfo rightType = right.GetTypeInfo();
        int l = leftType.GetValueType();
        int r = rightType.GetValueType();
        if ((l == Value.Null && r == Value.Null) || (l == Value.Unknown && r == Value.Unknown))
        {
            // (? + ?) - the decimal is the safest type, and a string is not a number.
            type = TypeInfo.TypeNumericFloatingPoint;
        }
        else if (DataType.IsIntervalType(l) || DataType.IsIntervalType(r))
        {
            // The operations over intervals need the class that computes them.
            throw new TODOException(
                "BinaryOperation.optimize: the operations over intervals are not ported");
        }
        else if (DataType.IsDateTimeType(l) || DataType.IsDateTimeType(r))
        {
            throw new TODOException(
                "BinaryOperation.optimize: the operations over date and time are not ported");
        }
        else
        {
            int dataType = Value.GetHigherOrder(l, r);
            if (dataType == Value.Numeric)
            {
                OptimizeNumeric(leftType, rightType);
            }
            else if (dataType == Value.Decfloat)
            {
                OptimizeDecfloat(leftType, rightType);
            }
            else if (dataType == Value.Enum)
            {
                type = TypeInfo.TypeInteger;
            }
            else if (DataType.IsCharacterStringType(dataType))
            {
                // The reference joins two strings with a plus when the mode allows it; the
                // regular mode does not, and this port has no other mode.
                throw new TODOException(
                    "BinaryOperation.optimize: the concatenation of two strings is not ported");
            }
            else
            {
                type = TypeInfo.GetTypeInfo(dataType);
            }
        }

        if (left.IsConstant() && right.IsConstant())
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }

    /// <summary>
    /// The type of the result of an operation over two exact numbers.
    /// </summary>
    /// <param name="leftType">the type of the left argument</param>
    /// <param name="rightType">the type of the right argument</param>
    private void OptimizeNumeric(TypeInfo leftType, TypeInfo rightType)
    {
        leftType = leftType.ToNumericType();
        rightType = rightType.ToNumericType();
        long leftPrecision = leftType.GetPrecision();
        long rightPrecision = rightType.GetPrecision();
        int leftScale = leftType.GetScale();
        int rightScale = rightType.GetScale();
        long precision;
        int scale;
        switch (opType)
        {
            case OpType.PLUS:
            case OpType.MINUS:
                // The precision is implementation-defined, the scale is the larger of the two,
                // and the other argument is widened to match it.
                if (leftScale < rightScale)
                {
                    leftPrecision += rightScale - leftScale;
                    scale = rightScale;
                }
                else
                {
                    rightPrecision += leftScale - rightScale;
                    scale = leftScale;
                }

                // One more digit for the largest precision.
                precision = Math.Max(leftPrecision, rightPrecision) + 1;
                break;
            case OpType.MULTIPLY:
                // The precision is implementation-defined, the scale is the sum of the two,
                // and the precision is the sum of the two.
                precision = leftPrecision + rightPrecision;
                scale = leftScale + rightScale;
                break;
            case OpType.DIVIDE:
                {
                    // The precision and the scale are implementation-defined.
                    long scaleAsLong = leftScale - rightScale + (rightPrecision * 2);
                    if (scaleAsLong >= ValueNumeric.MaximumScale)
                    {
                        scale = ValueNumeric.MaximumScale;
                    }
                    else if (scaleAsLong <= 0)
                    {
                        scale = 0;
                    }
                    else
                    {
                        scale = (int)scaleAsLong;
                    }

                    // The divisor can be multiplied by no more than 10^rightScale, so its
                    // precision grows by that much and the result follows the scale.
                    precision = leftPrecision + rightScale - leftScale + scale;

                    // A precision that is too large is reduced together with the scale.
                    if (precision > Engine.Constants.MaxNumericPrecision)
                    {
                        long sub = Math.Min(precision - Engine.Constants.MaxNumericPrecision, scale);
                        precision -= sub;
                        scale -= (int)sub;
                    }

                    break;
                }

            default:
                throw DbException.GetInternalError("type=" + opType);
        }

        type = TypeInfo.GetTypeInfo(Value.Numeric, precision, scale, null);
    }

    /// <summary>
    /// The type of the result of an operation over two decimal floating point numbers.
    /// </summary>
    /// <param name="leftType">the type of the left argument</param>
    /// <param name="rightType">the type of the right argument</param>
    private void OptimizeDecfloat(TypeInfo leftType, TypeInfo rightType)
    {
        leftType = leftType.ToDecfloatType();
        rightType = rightType.ToDecfloatType();
        long leftPrecision = leftType.GetPrecision();
        long rightPrecision = rightType.GetPrecision();
        long precision;
        switch (opType)
        {
            case OpType.PLUS:
            case OpType.MINUS:
            case OpType.DIVIDE:
                // One more digit for the largest precision.
                precision = Math.Max(leftPrecision, rightPrecision) + 1;
                break;
            case OpType.MULTIPLY:
                // The sum of the precisions.
                precision = leftPrecision + rightPrecision;
                break;
            default:
                throw DbException.GetInternalError("type=" + opType);
        }

        type = TypeInfo.GetTypeInfo(Value.Decfloat, precision, 0, null);
    }
}

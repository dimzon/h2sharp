/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.SessionControlFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * * Cancelling the target's running command. Commands run synchronously in this port and
 *   no current command is tracked, so CANCEL_SESSION calls the session's cancel, which
 *   refuses with a TODOException instead of answering TRUE without stopping anything.
 * * The admin check. The port has no users, so there is nobody to ask.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// An ABORT_SESSION() or CANCEL_SESSION() function.
/// </summary>
public sealed class SessionControlFunction : Function1
{
    /// <summary>ABORT_SESSION().</summary>
    public const int AbortSession = 0;

    /// <summary>CANCEL_SESSION().</summary>
    public const int CancelSession = AbortSession + 1;

    private static readonly string[] Names =
    [
        "ABORT_SESSION", "CANCEL_SESSION",
    ];

    private readonly int function;

    /// <summary>
    /// Creates a session control function.
    /// </summary>
    /// <param name="arg">the session id expression</param>
    /// <param name="function">one of the constants above</param>
    public SessionControlFunction(Expression arg, int function)
        : base(arg)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = arg.GetValue(session);
        if (v is ValueNull)
        {
            return ValueNull.Instance;
        }

        int targetSessionId = v.GetInt();
        foreach (SessionLocal s in session.GetDatabase().GetSessions())
        {
            if (s.GetId() == targetSessionId)
            {
                switch (function)
                {
                    case AbortSession:
                        // The reference also cancels the command the target is running; a
                        // synchronous port has none to cancel, and closing the session is
                        // the rest of what ABORT_SESSION does.
                        s.Close();
                        return ValueBoolean.True;
                    case CancelSession:
                        // The reference cancels the command the session is running. This
                        // port runs commands synchronously and does not track a current
                        // command, so SessionLocal.cancel refuses with a TODOException;
                        // calling it keeps the refusal honest instead of answering TRUE
                        // without stopping anything.
                        s.Cancel();
                        return ValueBoolean.True;
                    default:
                        throw DbException.GetInternalError("function=" + function);
                }
            }
        }

        return ValueBoolean.False;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        type = TypeInfo.TypeBoolean;
        return this;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.ReadOnly:
            case ExpressionVisitor.QueryComparable:
                return false;
            default:
                break;
        }

        return base.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}

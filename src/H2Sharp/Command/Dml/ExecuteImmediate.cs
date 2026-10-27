/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.ExecuteImmediate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;

namespace H2Sharp.Command.Dml;

/// <summary>EXECUTE IMMEDIATE.</summary>
public sealed class ExecuteImmediate : Prepared
{
    private readonly Expression statement;

    /// <summary>Creates the statement.</summary>
    public ExecuteImmediate(SessionLocal session, Expression statement) : base(session)
        => this.statement = statement.Optimize(session);

    /// <inheritdoc/>
    public override long Update()
    {
        string? sql = statement.GetValue(GetSession()).GetString();
        if (sql is null)
        {
            throw DbException.GetInvalidValueException("SQL command", null);
        }

        using CommandInterface command = GetSession().PrepareCommand(sql);
        if (command.IsQuery())
        {
            throw DbException.Get(ErrorCode.SyntaxError2, sql, "<not a query>");
        }

        return command.ExecuteUpdate(false).GetUpdateCount();
    }

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.ExecuteImmediately;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;
}

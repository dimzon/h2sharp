/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.command.dml.RunScriptCommand.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Util;

namespace H2Sharp.Command.Dml;

/// <summary>
/// The RUNSCRIPT statement: reads an SQL script from a file and executes its statements.
/// </summary>
public sealed class RunScriptCommand : ScriptBase
{
    /// <summary>The UTF-8 byte order mark.</summary>
    private const char Utf8Bom = '\uFEFF';

    private bool quirksMode;
    private bool variableBinary;
    private bool from1X;

    /// <summary>Creates the command.</summary>
    /// <param name="session">the session</param>
    public RunScriptCommand(SessionLocal session) : base(session)
    {
    }

    /// <inheritdoc/>
    public override long Update()
    {
        // D-004: checkAdmin is a no-op, the session has full access.
        int count = 0;
        bool oldQuirksMode = GetSession().IsQuirksMode();
        bool oldVariableBinary = GetSession().IsVariableBinary();
        try
        {
            OpenInput();
            if (Reader is not null)
            {
                // The reader may have consumed the BOM as a character while detecting it.
                // TextReader.Read() is used here exactly as the reference's Reader does.
                if (Reader.Peek() == Utf8Bom)
                {
                    _ = Reader.Read();
                }

                if (quirksMode)
                {
                    GetSession().SetQuirksMode(true);
                }

                if (variableBinary)
                {
                    GetSession().SetVariableBinary(true);
                }

                using ScriptReader reader = new(Reader);
                while (true)
                {
                    string? sql = reader.ReadStatement();
                    if (sql is null)
                    {
                        break;
                    }

                    Execute(sql);
                    count++;
                    if ((count & 127) == 0)
                    {
                        CheckCanceled();
                    }
                }
            }
        }
        finally
        {
            if (quirksMode)
            {
                GetSession().SetQuirksMode(oldQuirksMode);
            }

            if (variableBinary)
            {
                GetSession().SetVariableBinary(oldVariableBinary);
            }

            CloseIO();
        }

        return count;
    }

    private void Execute(string sql)
    {
        if (from1X)
        {
            sql = sql.Trim();
            if (sql.StartsWith("--", StringComparison.Ordinal))
            {
                int i = 2, l = sql.Length;
                char c;
                do
                {
                    if (i >= l)
                    {
                        return;
                    }

                    c = sql[i++];
                }
                while (c != '\n' && c != '\r');
                sql = StringUtils.TrimSubstring(sql, i);
            }

            const string LobPrefix = "INSERT INTO SYSTEM_LOB_STREAM VALUES(";
            if (sql.StartsWith(LobPrefix, StringComparison.Ordinal))
            {
                int idx = sql.IndexOf(", NULL, '", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    sql = new StringBuilder(sql.Length + 1)
                        .Append(sql, 0, idx + 8).Append("X'")
                        .Append(sql, idx + 9, sql.Length - (idx + 9)).ToString();
                }
            }
        }

        try
        {
            CommandInterface container = GetSession().PrepareCommand(sql);
            if (container.IsQuery())
            {
                container.ExecuteQuery(0, -1, false);
            }
            else
            {
                container.ExecuteUpdate(null!);
            }
        }
        catch (H2SharpException e)
        {
            throw DbException.AddSql(e, sql);
        }
    }

    /// <summary>Enables H2 1.x script quirks.</summary>
    public void SetFrom1X()
    {
        variableBinary = quirksMode = from1X = true;
    }

    /// <summary>Enables quirks mode while the script runs.</summary>
    /// <param name="value">whether to enable it</param>
    public void SetQuirksMode(bool value) => quirksMode = value;

    /// <summary>Treats BINARY in the script as VARBINARY.</summary>
    /// <param name="value">whether to enable it</param>
    public void SetVariableBinary(bool value) => variableBinary = value;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.RunScript;
}

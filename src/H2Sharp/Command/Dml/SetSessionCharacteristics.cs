/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.SetSessionCharacteristics.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Result;

namespace H2Sharp.Command.Dml;

/// <summary>
/// This class represents the statement SET SESSION CHARACTERISTICS.
/// </summary>
public class SetSessionCharacteristics : Prepared
{
    private readonly IsolationLevel isolationLevel;

    public SetSessionCharacteristics(SessionLocal session, IsolationLevel isolationLevel)
        : base(session)
    {
        this.isolationLevel = isolationLevel;
    }

    public override bool IsTransactional() => false;

    public override long Update()
    {
        GetSession().SetIsolationLevel(isolationLevel);
        return 0;
    }

    public override bool NeedRecompile() => false;

    public override ResultInterface? QueryMeta() => null;

    public override int GetCommandType() => CommandInterface.Set;
}

/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>The statement ALTER TYPE.</summary>
public sealed class AlterType : AlterDomain
{
    private string value = string.Empty;

    /// <summary>Creates the statement.</summary>
    public AlterType(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the value to add to the enum.</summary>
    public void SetValue(string value) => this.value = value;

    /// <inheritdoc/>
    protected override long Update(Schema schema, Domain domain)
    {
        TypeInfo dataType = domain.GetDataType();
        if (dataType.GetValueType() != Value.Enum)
        {
            throw DbException.Get(ErrorCode.WrongObjectType, domain.GetName());
        }

        ExtTypeInfoEnum oldExtTypeInfo = (ExtTypeInfoEnum)dataType.GetExtTypeInfo()!;
        int count = oldExtTypeInfo.GetCount();
        string?[] newValues = new string?[count + 1];
        for (int i = 0; i < count; i++)
        {
            newValues[i] = oldExtTypeInfo.GetEnumerator(i);
        }

        newValues[count] = value;
        domain.SetDataType(TypeInfo.GetTypeInfo(Value.Enum, -1L, -1, new ExtTypeInfoEnum(newValues)));
        schema.GetDatabase().UpdateMeta(GetSession(), domain);
        ForAllDependencies(GetSession(), domain, CopyDomain, null, false);
        return 0;
    }

    private static bool CopyDomain(Domain domain, Column column)
    {
        column.SetType(domain.GetDataType());
        return true;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterType;
}

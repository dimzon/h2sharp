/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Setting.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Globalization;
using System.Text;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// A persistent database setting.
/// </summary>
public sealed class Setting : DbObject
{
    private int intValue;

    private string? stringValue;

    public Setting(Database database, int id, string settingName)
        : base(database, id, settingName, Trace.Setting)
    {
    }

    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => builder.Append(GetName());

    public void SetIntValue(int value)
    {
        intValue = value;
    }

    public int GetIntValue()
    {
        return intValue;
    }

    public void SetStringValue(string value)
    {
        stringValue = value;
    }

    public string? GetStringValue()
    {
        return stringValue;
    }

    public override string GetCreateSql()
    {
        StringBuilder buff = new("SET ");
        _ = GetSql(buff, HasSql.DefaultSqlFlags).Append(' ');
        if (stringValue is not null)
        {
            _ = buff.Append(stringValue);
        }
        else
        {
            _ = buff.Append(intValue.ToString(CultureInfo.InvariantCulture));
        }

        return buff.ToString();
    }

    public override int GetTypeId()
    {
        return DbObject.Setting;
    }

    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        // The reference also removes the meta row here; the meta table is not ported, so
        // there is nothing to remove and the caller's query-cache invalidation is the whole
        // effect.
        Invalidate();
    }

    public override void CheckRename()
    {
        throw DbException.GetUnsupportedException("RENAME");
    }
}

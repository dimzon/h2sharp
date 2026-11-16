/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.FunctionsPostgreSQL.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The tables that answer "not implemented" in the reference answer exactly as it does. The
 * privilege functions answer TRUE with the same comment the reference carries. PG_GET_USERBYID
 * needs users (D-004) and TO_DATE/TO_TIMESTAMP need ToDateParser/ToDateTokenizer; both call out
 * what is missing instead of answering with an invention.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Server.Pg;
using H2Sharp.Schemas;
using Index = H2Sharp.Indexes.Index;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// Functions for PostgreSQL compatibility mode.
/// </summary>
public sealed class FunctionsPostgreSQL : ModeFunction
{
    private const int CurrentDatabase = 3001;
    private const int Currtid2 = CurrentDatabase + 1;
    private const int FormatType = Currtid2 + 1;
    private const int HasDatabasePrivilege = FormatType + 1;
    private const int HasSchemaPrivilege = HasDatabasePrivilege + 1;
    private const int HasTablePrivilege = HasSchemaPrivilege + 1;
    private const int Lastval = HasTablePrivilege + 1;
    private const int Version = Lastval + 1;
    private const int ObjDescription = Version + 1;
    private const int PgEncodingToChar = ObjDescription + 1;
    private const int PgGetExpr = PgEncodingToChar + 1;
    private const int PgGetIndexdef = PgGetExpr + 1;
    private const int PgGetUserbyid = PgGetIndexdef + 1;
    private const int PgPostmasterStartTime = PgGetUserbyid + 1;
    private const int PgRelationSize = PgPostmasterStartTime + 1;
    private const int PgTotalRelationSize = PgRelationSize + 1;
    private const int PgTableIsVisible = PgTotalRelationSize + 1;
    private const int SetConfig = PgTableIsVisible + 1;
    private const int ArrayToString = SetConfig + 1;
    private const int PgStatGetNumscans = ArrayToString + 1;
    private const int ToDate = PgStatGetNumscans + 1;
    private const int ToTimestamp = ToDate + 1;
    private const int GenRandomUuid = ToTimestamp + 1;

    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.Ordinal);

    static FunctionsPostgreSQL()
    {
        Functions["CURRENT_DATABASE"] = new FunctionInfo(
            "CURRENT_DATABASE", CurrentDatabase, 0, Value.Varchar, true, false);
        Functions["CURRTID2"] = new FunctionInfo("CURRTID2", Currtid2, 2, Value.Integer, true, false);
        Functions["FORMAT_TYPE"] = new FunctionInfo("FORMAT_TYPE", FormatType, 2, Value.Varchar, false, true);
        Functions["HAS_DATABASE_PRIVILEGE"] = new FunctionInfo(
            "HAS_DATABASE_PRIVILEGE", HasDatabasePrivilege, VarArgs, Value.Boolean, true, false);
        Functions["HAS_SCHEMA_PRIVILEGE"] = new FunctionInfo(
            "HAS_SCHEMA_PRIVILEGE", HasSchemaPrivilege, VarArgs, Value.Boolean, true, false);
        Functions["HAS_TABLE_PRIVILEGE"] = new FunctionInfo(
            "HAS_TABLE_PRIVILEGE", HasTablePrivilege, VarArgs, Value.Boolean, true, false);
        Functions["LASTVAL"] = new FunctionInfo("LASTVAL", Lastval, 0, Value.Bigint, true, false);
        Functions["VERSION"] = new FunctionInfo("VERSION", Version, 0, Value.Varchar, true, false);
        Functions["OBJ_DESCRIPTION"] = new FunctionInfo(
            "OBJ_DESCRIPTION", ObjDescription, VarArgs, Value.Varchar, true, false);
        Functions["PG_ENCODING_TO_CHAR"] = new FunctionInfo(
            "PG_ENCODING_TO_CHAR", PgEncodingToChar, 1, Value.Varchar, true, true);
        Functions["PG_GET_EXPR"] = new FunctionInfo("PG_GET_EXPR", PgGetExpr, VarArgs, Value.Varchar, true, true);
        Functions["PG_GET_INDEXDEF"] = new FunctionInfo(
            "PG_GET_INDEXDEF", PgGetIndexdef, VarArgs, Value.Varchar, true, false);
        Functions["PG_GET_USERBYID"] = new FunctionInfo(
            "PG_GET_USERBYID", PgGetUserbyid, 1, Value.Varchar, true, false);
        Functions["PG_POSTMASTER_START_TIME"] = new FunctionInfo(
            "PG_POSTMASTER_START_TIME", PgPostmasterStartTime, 0, Value.TimestampTz, true, false);
        Functions["PG_RELATION_SIZE"] = new FunctionInfo(
            "PG_RELATION_SIZE", PgRelationSize, VarArgs, Value.Bigint, true, false);
        Functions["PG_TOTAL_RELATION_SIZE"] = new FunctionInfo(
            "PG_TOTAL_RELATION_SIZE", PgTotalRelationSize, VarArgs, Value.Bigint, true, false);
        Functions["PG_TABLE_IS_VISIBLE"] = new FunctionInfo(
            "PG_TABLE_IS_VISIBLE", PgTableIsVisible, 1, Value.Boolean, true, false);
        Functions["SET_CONFIG"] = new FunctionInfo("SET_CONFIG", SetConfig, 3, Value.Varchar, true, false);
        Functions["ARRAY_TO_STRING"] = new FunctionInfo(
            "ARRAY_TO_STRING", ArrayToString, VarArgs, Value.Varchar, false, true);
        Functions["PG_STAT_GET_NUMSCANS"] = new FunctionInfo(
            "PG_STAT_GET_NUMSCANS", PgStatGetNumscans, 1, Value.Integer, true, true);
        Functions["TO_DATE"] = new FunctionInfo("TO_DATE", ToDate, 2, Value.Date, true, true);
        Functions["TO_TIMESTAMP"] = new FunctionInfo("TO_TIMESTAMP", ToTimestamp, 2, Value.TimestampTz, true, true);
        Functions["GEN_RANDOM_UUID"] = new FunctionInfo("GEN_RANDOM_UUID", GenRandomUuid, 0, Value.Uuid, true, false);
    }

    /// <summary>Returns the mode-specific function of that name, or null.</summary>
    /// <param name="upperName">the uppercase name</param>
    /// <returns>the function, or null</returns>
    public static FunctionsPostgreSQL? GetFunction(string upperName)
        => Functions.TryGetValue(upperName, out FunctionInfo? info) ? new FunctionsPostgreSQL(info) : null;

    private FunctionsPostgreSQL(FunctionInfo info) : base(info)
    {
    }

    /// <inheritdoc/>
    protected override void CheckParameterCount(int len)
    {
        int min, max;
        switch (Info.Type)
        {
            case HasDatabasePrivilege:
            case HasSchemaPrivilege:
            case HasTablePrivilege:
                min = 2;
                max = 3;
                break;
            case ObjDescription:
            case PgRelationSize:
            case PgTotalRelationSize:
                min = 1;
                max = 2;
                break;
            case PgGetIndexdef:
                if (len != 1 && len != 3)
                {
                    throw DbException.Get(
                        Api.ErrorCode.InvalidParameterCount2, Info.Name, "1, 3");
                }

                return;
            case PgGetExpr:
            case ArrayToString:
                min = 2;
                max = 3;
                break;
            default:
                throw DbException.GetInternalError("type=" + Info.Type);
        }

        if (len < min || len > max)
        {
            throw DbException.Get(
                Api.ErrorCode.InvalidParameterCount2, Info.Name, min + ".." + max);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        switch (Info.Type)
        {
            case CurrentDatabase:
                return new CurrentGeneralValueSpecification(
                    CurrentGeneralValueSpecification.CurrentCatalog).Optimize(session);
            case GenRandomUuid:
                // PostgreSQL uses version 4.
                return new RandFunction(
                    ValueExpression.Get(ValueInteger.Get(4)), RandFunction.RandomUuid).Optimize(session);
            default:
                bool allConst = OptimizeArguments(session);
                type = TypeInfo.GetTypeInfo(Info.ReturnDataType);
                if (allConst)
                {
                    return ValueExpression.Get(GetValue(session));
                }

                break;
        }

        return this;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value?[]? values = GetArgumentsValues(session);
        if (values is null)
        {
            return ValueNull.Instance;
        }

        Value? v0 = GetNullOrValue(session, values, 0);
        Value? v1 = GetNullOrValue(session, values, 1);
        Value? v2 = GetNullOrValue(session, values, 2);
        switch (Info.Type)
        {
            case Currtid2:
                // Not implemented, as in the reference.
                return ValueInteger.Get(1);
            case FormatType:
                return v0 is not ValueNull
                    ? ValueVarchar.Get(PgServer.FormatType(v0!.GetInt()))
                    : ValueNull.Instance;
            case HasDatabasePrivilege:
            case HasSchemaPrivilege:
            case HasTablePrivilege:
            case PgTableIsVisible:
                // Not implemented, as in the reference.
                return ValueBoolean.True;
            case Lastval:
                {
                    Value result = session.GetLastIdentity();
                    if (result == ValueNull.Instance)
                    {
                        throw DbException.Get(
                            Api.ErrorCode.CurrentSequenceValueIsNotDefinedInSession1, "lastval()");
                    }

                    return result!.ConvertToType(TypeInfo.TypeBigint);
                }
            case Version:
                return ValueVarchar.Get("PostgreSQL " + Constants.PgVersion
                    + " server protocol using H2 " + Constants.FullVersion);
            case ObjDescription:
            case PgGetExpr:
                // Not implemented, as in the reference.
                return ValueNull.Instance;
            case PgEncodingToChar:
                return ValueVarchar.Get(EncodingToChar(v0!.GetInt()));
            case PgGetIndexdef:
                return GetIndexdef(session, v0!.GetInt(), v1, v2);
            case PgPostmasterStartTime:
                return session.GetDatabase().GetSystemSession().GetSessionStart();
            case PgRelationSize:
                // The optional second argument is ignored, as in the reference.
                return RelationSize(session, v0!, total: false);
            case PgTotalRelationSize:
                // The optional second argument is ignored, as in the reference.
                return RelationSize(session, v0!, total: true);
            case SetConfig:
                // Not implemented, as in the reference.
                return v1!.ConvertToType(TypeInfo.TypeVarchar, session);
            case ArrayToString:
                {
                    if (v0 == ValueNull.Instance || v1 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    if (v0!.GetValueType() != Value.Array)
                    {
                        throw DbException.GetInvalidValueException("ARRAY_TO_STRING array", v0);
                    }

                    string? nullString = v2?.GetString();
                    string separator = v1!.GetString()!;
                    // The reference joins through StringJoiner: the separator goes between the
                    // values it kept, not between the elements it saw.
                    System.Text.StringBuilder builder = new();
                    Value[] list = ((ValueArray)v0).GetList();
                    bool appended = false;
                    for (int i = 0; i < list.Length; i++)
                    {
                        Value v = list[i];
                        if (v != ValueNull.Instance)
                        {
                            if (appended)
                            {
                                _ = builder.Append(separator);
                            }

                            _ = builder.Append(v.GetString());
                            appended = true;
                        }
                        else if (nullString is not null)
                        {
                            if (appended)
                            {
                                _ = builder.Append(separator);
                            }

                            _ = builder.Append(nullString);
                            appended = true;
                        }
                    }

                    return ValueVarchar.Get(builder.ToString());
                }
            case PgStatGetNumscans:
                // Not implemented, as in the reference.
                return ValueInteger.Get(0);
            case PgGetUserbyid:
                throw new TODOException(
                    "FunctionsOracle.PG_GET_USERBYID: users and roles are not ported (D-004)");
            case ToDate:
            case ToTimestamp:
                throw new TODOException(
                    "FunctionsOracle." + Info.Name + ": ToDateParser and ToDateTokenizer are not ported");
            default:
                throw DbException.GetInternalError("type=" + Info.Type);
        }
    }

    private static string EncodingToChar(int code) => code switch
    {
        0 => "SQL_ASCII",
        6 => "UTF8",
        8 => "LATIN1",
        // This function returns empty string for unknown encodings.
        _ => code < 40 ? "UTF8" : "",
    };

    private static Value GetIndexdef(SessionLocal session, int indexId, Value? ordinalPosition, Value? pretty)
    {
        foreach (Schema schema in session.GetDatabase().GetSchemas())
        {
            foreach (Index index in schema.GetAllIndexes())
            {
                if (index.GetId() == indexId)
                {
                    int ordinal;
                    if (ordinalPosition is null || (ordinal = ordinalPosition.GetInt()) == 0)
                    {
                        return ValueVarchar.Get(index.GetCreateSql()!);
                    }

                    IndexColumn[] columns = index.GetIndexColumns();
                    if (ordinal >= 1 && ordinal <= columns.Length)
                    {
                        return ValueVarchar.Get(columns[ordinal - 1].GetColumn().GetName());
                    }

                    break;
                }
            }
        }

        return ValueNull.Instance;
    }

    private static Value RelationSize(SessionLocal session, Value tableOidOrName, bool total)
    {
        Table? table;
        if (tableOidOrName.GetValueType() == Value.Integer)
        {
            int tid = tableOidOrName.GetInt();
            foreach (Schema schema in session.GetDatabase().GetSchemas())
            {
                foreach (Table candidate in schema.GetAllTablesAndViews(session))
                {
                    if (tid == candidate.GetId())
                    {
                        table = candidate;
                        goto found;
                    }
                }
            }

            return ValueNull.Instance;
        }
        else
        {
            // The reference parses the name here; the port resolves the single (possibly
            // schema-qualified) identifier the same way.
            string name = tableOidOrName.GetString()!;
            string? schemaName = null;
            int dot = name.IndexOf('.');
            string tableName = name;
            if (dot >= 0)
            {
                schemaName = name[..dot];
                tableName = name[(dot + 1)..];
            }

            table = session.GetDatabase()
                .GetSchema(schemaName ?? session.GetCurrentSchemaName())
                .GetTableOrView(session, tableName);
        }

    found:
        return ValueBigint.Get(table!.GetDiskSpaceUsed(total, approximate: false));
    }
}

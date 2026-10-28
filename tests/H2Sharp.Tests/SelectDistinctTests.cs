/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What SELECT DISTINCT answers, checked against the reference running its own.
 *
 * Both sides build the same table with duplicate names and a null, plan the same select
 * with the distinct flag, and read. Duplicates collapse to one row each, the null to one
 * null, and the rows come out in the comparison order both sides sort their distinct
 * maps with. What is not covered here is deliberate: the index fast path needs a
 * secondary index, which is still a stub, and DISTINCT ON needs its expression list.
 */
using System.Collections.Generic;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SelectDistinctTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(ConnectionInfo.Memory("select_distinct")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaSession = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_distinct_e2e");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.engine.Database db = session.getDatabase();
        """;

    private const string JavaTable = """
        org.h2.command.ddl.CreateTableData data = new org.h2.command.ddl.CreateTableData();
        data.schema = db.getSchema("PUBLIC");
        data.id = 0;
        data.tableName = "T";
        org.h2.table.Column idCol = new org.h2.table.Column("ID", org.h2.value.TypeInfo.TYPE_INTEGER);
        idCol.setNullable(false);
        data.columns.add(idCol);
        data.columns.add(new org.h2.table.Column("NAME", org.h2.value.TypeInfo.TYPE_VARCHAR));
        data.persistIndexes = false;
        data.persistData = false;
        org.h2.mvstore.db.MVTable table = new org.h2.mvstore.db.MVTable(data, db.getStore());
        org.h2.table.IndexColumn[] cols = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("ID")) };
        table.addIndex(session, "I", 7, cols, 1,
            org.h2.index.IndexType.createPrimaryKey(false, false), true, null);
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(3), org.h2.value.ValueVarchar.get("c") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("b") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("b") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(4), org.h2.value.ValueNull.INSTANCE }));
        """;

    private const string JavaQuery = """
        org.h2.result.ResultInterface r = select.query(-1);
        """;

    private const string JavaPrint = """
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            StringBuilder line = new StringBuilder("=");
            for (int i = 0; i < row.length; i++) {
                if (i > 0) {
                    line.append(':');
                }
                line.append(row[i] == org.h2.value.ValueNull.INSTANCE ? "-" : row[i].getString());
            }
            System.out.println(line);
        }
        conn.close();
        """;

    private static MVTable NewKeyedTable(SessionLocal session, TransactionStore store)
    {
        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(
            session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
            0, "T", [id, name], persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(4), ValueNull.Instance]));
        return table;
    }

    private static List<string> Print(ResultInterface result, int columns)
    {
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            List<string> cells = [];
            for (int i = 0; i < columns; i++)
            {
                cells.Add(row[i] is ValueNull ? "-" : row[i]!.GetString()!);
            }

            rows.Add("=" + string.Join(":", cells));
        }

        return rows;
    }

    private static void RunDistinct(
        int[] columns, string javaColumns, long limit, List<string>? expected)
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            List<Expression> expressions = [];
            foreach (int column in columns)
            {
                expressions.Add(new ExpressionColumn(table.GetColumn(column), f, 0));
            }

            select.SetExpressions([.. expressions]);
            select.SetDistinct();
            select.Init();
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);
            if (limit >= 0)
            {
                select.SetFetch(ValueExpression.Get(ValueBigint.Get(limit)));
            }

            List<string> actual = Print(select.Run(-1), columns.Length);

            string javaLimits = limit >= 0
                ? "select.setFetch(org.h2.expression.ValueExpression.get(\n"
                    + $"    org.h2.value.ValueInteger.get({limit})));\n"
                : string.Empty;
            List<string> java = OracleRunner.Run(
                [],
                JavaSession + JavaTable + javaColumns + JavaDistinct + javaLimits + JavaQuery + JavaPrint,
                null, null, null);
            if (expected is not null)
            {
                Assert.Equal(expected, actual);
            }

            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private const string JavaDistinct = """
        select.setDistinct();
        select.init();
        select.prepareExpressions();
        select.preparePlan();
        """;

    private static string JavaColumns(params string[] names)
    {
        string body = """
            org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
            org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
            select.addTableFilter(f, true);
            java.util.ArrayList<org.h2.expression.Expression> exprs = new java.util.ArrayList<>();
            """;
        foreach (string name in names)
        {
            body += $"""
                org.h2.expression.ExpressionColumn col_{name} = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "{name}");
                col_{name}.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                exprs.add(col_{name});
                """;
        }

        return body + "select.setExpressions(exprs);\n";
    }

    [JavaOracleFact]
    public void DistinctNameCollapsesDuplicatesAndNull()
    {
        RunDistinct([1], JavaColumns("NAME"), -1, null);
    }

    [JavaOracleFact]
    public void DistinctOverUniqueColumnsKeepsEveryRow()
    {
        RunDistinct([0, 1], JavaColumns("ID", "NAME"), -1, null);
    }

    [JavaOracleFact]
    public void DistinctWithLimitTrimsAfterDedup()
    {
        RunDistinct([1], JavaColumns("NAME"), 2, null);
    }
}

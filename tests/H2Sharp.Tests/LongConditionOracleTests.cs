/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Long AND/OR chains: the pinned H2 parser's shape, traversals and SQL answers. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class LongConditionOracleTests
{
    [JavaOracleFact]
    public void ParserUsesBinaryPairsAndFlatLongChainsLikeTheReference()
    {
        string[] inputs = ["1 AND 1", "1 OR 0", Chain("1", " AND 1", 10000), Chain("0", " OR 0", 10000)];
        SessionLocal session = new(new Database(ConnectionInfo.Memory("long_condition_shape")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> port = [];
        try
        {
            foreach (string sql in inputs)
            {
                Expression expression = new Parser(session).ParseExpression(sql);
                port.Add("=" + expression.GetType().Name + ":" + expression.GetSubexpressionCount());
                // SQL generation, visitors and cost walks happen before constant folding.
                Assert.True(expression.GetSql(0).Length >= sql.Length);
                Assert.True(expression.IsEverything(ExpressionVisitor.DeterministicVisitor));
                Assert.Equal(0, expression.GetCost());
            }
        }
        finally { session.Close(); }
        List<string> java = OracleRunner.Run(inputs, """
            try (java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:long_condition_shape")) {
              org.h2.engine.SessionLocal session=(org.h2.engine.SessionLocal)((org.h2.jdbc.JdbcConnection)c).getSession();
              for(String sql:inputs) {
                org.h2.expression.Expression e=new org.h2.command.Parser(session).parseExpression(sql);
                System.out.println("="+e.getClass().getSimpleName()+":"+e.getSubexpressionCount());
                e.getSQL(0); e.isEverything(org.h2.expression.ExpressionVisitor.DETERMINISTIC_VISITOR); e.getCost();
              }
            }
            """);
        Assert.Equal(["=ConditionAndOr:2", "=ConditionAndOr:2", "=ConditionAndOrN:10001", "=ConditionAndOrN:10001"], port);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void NativeTenThousandAndReproductionAndLongOrExecuteLikeTheReference()
    {
        Check([
            "SELECT " + Chain("1", " AND 1", 10000),
            "SELECT " + Chain("0", " OR 0", 10000),
            "SELECT " + Chain("1", " AND 1", 10000) + " AND NULL",
            "SELECT " + Chain("0", " OR 0", 10000) + " OR NULL",
            "SELECT " + Chain("1", " AND 1", 10000) + " AND 0",
            "SELECT " + Chain("0", " OR 0", 10000) + " OR 1",
            "SELECT 2 AND -5, 0 OR -1, CAST(0.5 AS NUMERIC) AND CAST(1 AS REAL), CAST(0 AS BIGINT) OR CAST(2 AS DOUBLE)",
        ]);
    }

    [JavaOracleFact]
    public void SharedDialectPreservesFractionalNumericLiterals()
    {
        string fractional = Dialect.AppendNumeric(new StringBuilder(), 0.5m).ToString();
        string integral = Dialect.AppendNumeric(new StringBuilder(), 5m).ToString();
        string zero = Dialect.AppendNumeric(new StringBuilder(), 0.00m).ToString();
        Assert.Equal("0.5", fractional);
        Assert.Equal("CAST(5 AS NUMERIC(1))", integral);
        Check([
            "SELECT " + fractional + ", " + integral + ", " + zero,
            "SELECT " + fractional + " AND " + integral,
            "SELECT CAST(" + fractional + " AS NUMERIC(1,1)), CAST(" + fractional + " AS NUMERIC)",
        ]);
    }

    [JavaOracleFact]
    public void EqualCostOperandsKeepTheReferencesStableOrder()
    {
        string sql = string.Join(" AND ", Enumerable.Range(0, 32).Select(i => "@V" + i));
        SessionLocal session = new(new Database(ConnectionInfo.Memory("condition_sort_order")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        string port;
        try
        {
            for (int i = 0; i < 32; i++) session.SetVariable("V" + i, ValueBoolean.Get(i % 2 == 0));
            Expression expression = new Parser(session).ParseExpression(sql);
            string before = expression.GetSql(0);
            port = expression.Optimize(session).GetSql(0);
            Assert.Equal(before, port);
        }
        finally { session.Close(); }
        List<string> java = OracleRunner.Run([sql], """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:condition_sort_order")) {
              org.h2.engine.SessionLocal s=(org.h2.engine.SessionLocal)((org.h2.jdbc.JdbcConnection)c).getSession();
              for(int i=0;i<32;i++) s.setVariable("V"+i,org.h2.value.ValueBoolean.get(i%2==0));
              org.h2.expression.Expression e=new org.h2.command.Parser(s).parseExpression(inputs[0]);
              System.out.println(hex(e.optimize(s).getSQL(0)));
            }
            """);
        Assert.Equal([OracleRunner.Hex(port)], java);
    }

    [JavaOracleFact]
    public void LongNonConstantPredicatesMapAndRunLikeTheReference()
    {
        Check([
            "CREATE TABLE T(ID INT)",
            "INSERT INTO T VALUES(NULL),(0),(1),(2)",
            "SELECT ID FROM T WHERE " + Chain("ID >= 1", " AND ID >= 1", 10000) + " ORDER BY ID",
            "SELECT ID FROM T WHERE " + Chain("ID >= 1", " AND ID >= 1", 10000) + " AND ID < 2 ORDER BY ID",
            "SELECT ID FROM T WHERE NOT (" + Chain("ID >= 1", " AND ID >= 1", 10000) + ") ORDER BY ID",
            "SELECT ID FROM T WHERE " + Chain("ID IS NULL", " OR ID IS NULL", 10000) + " ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void FlatMergingAndPairOptimizationsKeepThreeValuedLogic()
    {
        Check([
            "CREATE TABLE T(A INT,B INT)",
            "INSERT INTO T VALUES(1,1),(1,2),(2,1),(2,NULL),(NULL,2)",
            "SELECT A,B FROM T WHERE A=1 OR A=2 OR A=3 ORDER BY A,B",
            "SELECT A,B FROM T WHERE (A=1 OR A=2) OR A=3 OR A=4 ORDER BY A,B",
            "SELECT A,B FROM T WHERE (A=1 AND B=1) OR (A=1 AND B=2) ORDER BY A,B",
            "SELECT A,B FROM T WHERE A=B AND B=1 AND A>0 ORDER BY A,B",
            "SELECT A,B FROM T WHERE NOT (A=B AND B=1 AND A>0) ORDER BY A,B",
            "SELECT A,B FROM T WHERE (A>0 AND B>0) AND A<3 AND B<3 ORDER BY A,B",
            "SELECT (TRUE AND (NULL AND TRUE)) IS NULL, (FALSE OR (NULL OR FALSE)) IS NULL",
            "SELECT NOT (FALSE OR NULL OR FALSE), NOT (TRUE AND NULL AND TRUE)",
        ]);
    }

    private static string Chain(string first, string operand, int count)
        => first + string.Concat(Enumerable.Repeat(operand, count));

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("Data Source=long_condition_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            if (!statements[i].StartsWith("SELECT", StringComparison.Ordinal))
            {
                port.Add($"={i}:U:{command.ExecuteNonQuery()}");
                continue;
            }
            using DbDataReader reader = command.ExecuteReader();
            port.Add($"={i}:C:{reader.FieldCount}");
            while (reader.Read())
            {
                List<string> cells = [];
                for (int col = 0; col < reader.FieldCount; col++)
                {
                    object value = reader.GetValue(col);
                    cells.Add(value is DBNull ? "NULL" : value is bool b ? b ? "TRUE" : "FALSE"
                        : Convert.ToString(value, CultureInfo.InvariantCulture)!);
                }
                port.Add($"={i}:R:" + string.Join('|', cells));
            }
        }
        List<string> java = OracleRunner.Run(statements, """
            try (java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:long_condition")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                java.sql.ResultSet r=s.getResultSet(); int count=r.getMetaData().getColumnCount();
                System.out.println("="+i+":C:"+count);
                while(r.next()) {
                  StringBuilder b=new StringBuilder("="+i+":R:");
                  for(int col=1;col<=count;col++) { if(col>1)b.append('|'); String v=r.getString(col); b.append(v==null?"NULL":v); }
                  System.out.println(b);
                }
              }
            }
            """, extraJavaArgs: ["-Duser.language=en"]);
        Assert.True(java.SequenceEqual(port), "java:\n" + string.Join("\n", java) + "\nport:\n" + string.Join("\n", port));
    }
}

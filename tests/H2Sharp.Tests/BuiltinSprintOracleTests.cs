/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Batch A: the same SQL on the port and the pinned H2 jar, line for line.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class BuiltinSprintOracleTests
{
    private const string Setup = "CREATE TABLE T(ID INT, V VARCHAR); INSERT INTO T VALUES(12,'b'),(18,'a'),(NULL,NULL);"
        + "CREATE TABLE GEO(ID INT, G GEOMETRY); INSERT INTO GEO VALUES(1,CAST('POINT(1 2)' AS GEOMETRY)),"
        + "(2,CAST('POINT(3 4)' AS GEOMETRY)),(3,NULL),(4,CAST('GEOMETRYCOLLECTION EMPTY' AS GEOMETRY))";

    [JavaOracleFact]
    public void AggregateRegistryMatchesTheReference()
    {
        List<string> java = OracleRunner.Run([], """
            java.lang.reflect.Field field = org.h2.expression.aggregate.Aggregate.class.getDeclaredField("AGGREGATES");
            field.setAccessible(true);
            java.util.Map<String, org.h2.expression.aggregate.AggregateType> map =
              (java.util.Map<String, org.h2.expression.aggregate.AggregateType>) field.get(null);
            for (String name : new java.util.TreeSet<>(map.keySet())) {
              System.out.println("=" + name + ":" + org.h2.expression.aggregate.Aggregate.getAggregateType(name).ordinal());
            }
            System.out.println("=UNKNOWN:" + (org.h2.expression.aggregate.Aggregate.getAggregateType("NOT_AN_AGGREGATE") == null));
            """);
        FieldInfo field = typeof(Aggregate).GetField("Aggregates", BindingFlags.NonPublic | BindingFlags.Static)!;
        Dictionary<string, AggregateType> registry = (Dictionary<string, AggregateType>)field.GetValue(null)!;
        List<string> port = [.. registry.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => "=" + name + ":" + ((int)Aggregate.GetAggregateType(name)!).ToString(CultureInfo.InvariantCulture))];
        port.Add("=UNKNOWN:" + (Aggregate.GetAggregateType("NOT_AN_AGGREGATE") is null).ToString().ToLowerInvariant());
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void AggregateParsingAndWindowDispatchMatchTheReference()
    {
        string[] sql =
        [
            "SELECT COUNT(*), COUNT(ID), SUM(ID), AVG(ID) FROM T",
            "SELECT ARRAY_AGG(ID ORDER BY ID DESC), LISTAGG(V, ',') WITHIN GROUP (ORDER BY V) FROM T",
            "SELECT JSON_ARRAYAGG(ID ORDER BY ID), JSON_OBJECTAGG(KEY V VALUE ID) FROM T WHERE V IS NOT NULL",
            "SELECT HISTOGRAM(ID) FROM T",
            "SELECT MODE() WITHIN GROUP (ORDER BY ID) FROM T",
            "SELECT MODE(ID ORDER BY ID DESC) FROM T",
            "SELECT MODE(ID ORDER BY ID + 1) FROM T",
            "SELECT HISTOGRAM(DISTINCT ID) FROM T",
            "SELECT COUNT(ID) FILTER (WHERE ID=12), SUM(ID) FILTER (WHERE ID=18) FROM T",
            "SELECT ANY(ID=12), SOME(ID=18), EVERY(ID>0) FROM T",
            "SELECT ID, RANK() OVER (ORDER BY ID), DENSE_RANK() OVER (ORDER BY ID), PERCENT_RANK() OVER (ORDER BY ID), CUME_DIST() OVER (ORDER BY ID) FROM T ORDER BY ID",
            "SELECT NOT_AN_AGGREGATE(ID) FROM T",
        ];
        Assert.Equal(ReadReference(sql), ReadPort(sql));
    }

    [JavaOracleFact]
    public void GcdAndLcmMatchTheReferenceIncludingNullsAndTypes()
    {
        string[] sql =
        [
            "SELECT GCD(12,18), LCM(4,6), GCD(-12,18,24), LCM(-2,3,4), GCD(0,0), LCM(0,5)",
            "SELECT GCD(1,2,NULL), LCM(2,0,NULL), GCD(GCD(12,18),24), LCM(LCM(2,3),4)",
            "SELECT GCD(CAST(123456789012345678901234567890 AS NUMERIC(40,0)), CAST(30 AS NUMERIC(40,0)))",
            "SELECT GCD(1.5,2)",
            "SELECT LCM('4',6)",
            "SELECT GCD(12)",
            "SELECT GCD()",
            "SELECT GCD_AGG(ID), LCM_AGG(ID), GCD_AGG(DISTINCT ID), LCM_AGG(DISTINCT ID) FROM T",
            "SELECT GCD_AGG(ID), LCM_AGG(ID) FROM T WHERE ID IS NULL",
            "SELECT GCD_AGG(ID), LCM_AGG(ID) FROM T WHERE ID<0",
            "SELECT GCD_AGG(12), LCM_AGG(NULL) FROM T",
            "SELECT GCD_AGG(ID) FILTER(WHERE ID=12), LCM_AGG(ID) FILTER(WHERE ID=18) FROM T",
            "SELECT ID, GCD_AGG(ID) OVER(ORDER BY ID ROWS BETWEEN 1 PRECEDING AND CURRENT ROW), LCM_AGG(ID) OVER() FROM T ORDER BY ID",
            "SELECT V, GCD_AGG(ID), LCM_AGG(ID) FROM T GROUP BY V ORDER BY V",
            "SELECT GCD_AGG(ID/2.0) FROM T",
            "SELECT GCD_AGG(V) FROM T",
        ];
        Assert.Equal(ReadReference(sql), ReadPort(sql));
    }

    [JavaOracleFact]
    public void JsonConstructorsAndFormatMatchTheReference()
    {
        string[] sql =
        [
            "SELECT JSON_OBJECT(), JSON_ARRAY(), JSON_OBJECT(KEY 'id' VALUE 1), JSON_OBJECT('id':1), JSON_ARRAY(1,2)",
            "SELECT JSON_OBJECT('a':NULL ABSENT ON NULL), JSON_ARRAY(1,NULL ABSENT ON NULL), JSON_ARRAY(NULL ON NULL)",
            "SELECT JSON_OBJECT('a':1,'a':2 WITHOUT UNIQUE KEYS)",
            "SELECT JSON_OBJECT('a':1,'a':2 WITH UNIQUE KEYS)",
            "SELECT JSON_OBJECT(KEY NULL VALUE 1)",
            "SELECT JSON_OBJECT('a':JSON 'null' ABSENT ON NULL), JSON_ARRAY(JSON 'null',NULL NULL ON NULL)",
            "SELECT JSON_OBJECT('quote': 'a' || CHAR(34) || 'b', 'line':CHAR(10), 'unicode':'ёж')",
            "SELECT '{\"a\":1}' FORMAT JSON, JSON_OBJECT('a':'[1,2]' FORMAT JSON)",
            "SELECT 'invalid-json' FORMAT JSON",
            "SELECT JSON_ARRAY((SELECT JSON_OBJECT('a':1) FROM SYSTEM_RANGE(1,1)) FORMAT JSON)",
            "SELECT JSON_ARRAY((SELECT ID FROM T ORDER BY ID) NULL ON NULL)",
            "SELECT JSON_ARRAY((SELECT ID FROM T WHERE ID<0))",
            "SELECT JSON_ARRAY((SELECT ID,V FROM T ORDER BY ID))",
            "SELECT ID, JSON_OBJECT('id':ID,'v':V ABSENT ON NULL) FROM T ORDER BY ID",
            "SELECT JSON_OBJECT('values':JSON_ARRAY(ID,V)), JSON_ARRAY(V FORMAT JSON) FROM T WHERE ID<0",
        ];
        Assert.Equal(ReadReference(sql), ReadPort(sql));
    }

    [JavaOracleFact]
    public void GeometryEnvelopeMatchesTheReferenceIncludingEmptyGroupsAndWindows()
    {
        string[] sql =
        [
            "SELECT ENVELOPE(G) FROM GEO",
            "SELECT ENVELOPE(G) FILTER(WHERE ID=1), ENVELOPE(G) FILTER(WHERE ID=2) FROM GEO",
            "SELECT ENVELOPE(G) FROM GEO WHERE ID>2",
            "SELECT ENVELOPE(G) FROM GEO WHERE ID<0",
            "SELECT DISTINCT ENVELOPE(G) OVER() FROM GEO",
            "SELECT ID, ENVELOPE(G) OVER(ORDER BY ID ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) FROM GEO ORDER BY ID",
            "SELECT ID, ENVELOPE(G) FROM GEO GROUP BY ID ORDER BY ID",
            "SELECT ENVELOPE(CAST('LINESTRING(-2 -3,5 7)' AS GEOMETRY)) FROM GEO",
            "SELECT ENVELOPE(CAST('SRID=4326;POINT(1 2)' AS GEOMETRY)) FROM GEO",
        ];
        Assert.Equal(ReadReference(sql), ReadPort(sql));
    }

    private static List<string> ReadReference(string[] sql) => OracleRunner.Run(sql, """
        try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:batch_a")) {
          java.sql.Statement stat = c.createStatement();
          stat.execute("CREATE TABLE T(ID INT, V VARCHAR)");
          stat.execute("INSERT INTO T VALUES(12,'b'),(18,'a'),(NULL,NULL)");
          stat.execute("CREATE TABLE GEO(ID INT, G GEOMETRY)");
          stat.execute("INSERT INTO GEO VALUES(1,CAST('POINT(1 2)' AS GEOMETRY)),(2,CAST('POINT(3 4)' AS GEOMETRY)),(3,NULL),(4,CAST('GEOMETRYCOLLECTION EMPTY' AS GEOMETRY))");
          for (int i=0; i<inputs.length; i++) {
            try (java.sql.ResultSet r = stat.executeQuery(inputs[i])) {
              StringBuilder out = new StringBuilder("=" + i);
              while (r.next()) {
                out.append("|row");
                for (int col=1; col<=r.getMetaData().getColumnCount(); col++) {
                  String value = r.getString(col);
                  out.append(':').append(value == null ? "NULL" : hex(value).substring(1));
                }
              }
              System.out.println(out);
            } catch (java.sql.SQLException e) {
              System.out.println("=" + i + ":ERR:" + e.getErrorCode());
            }
          }
        }
        """, extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);

    private static List<string> ReadPort(string[] sql)
    {
        using H2Connection connection = new("Data Source=batch_a_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        foreach (string statement in Setup.Split(';'))
        {
            using DbCommand create = connection.CreateCommand();
            create.CommandText = statement;
            create.ExecuteNonQuery();
        }
        List<string> lines = [];
        for (int i = 0; i < sql.Length; i++)
        {
            try
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql[i];
                using DbDataReader reader = command.ExecuteReader();
                StringBuilder output = new("=" + i.ToString(CultureInfo.InvariantCulture));
                while (reader.Read())
                {
                    output.Append("|row");
                    for (int col = 0; col < reader.FieldCount; col++)
                    {
                        output.Append(':').Append(reader.IsDBNull(col) ? "NULL" : OracleRunner.Hex(reader.GetString(col))[1..]);
                    }
                }
                lines.Add(output.ToString());
            }
            catch (H2SharpException e)
            {
                lines.Add("=" + i.ToString(CultureInfo.InvariantCulture) + ":ERR:" + e.H2ErrorCode.ToString(CultureInfo.InvariantCulture));
            }
        }
        return lines;
    }
}

/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's queries/joins.sql, statement by statement: inner/outer/natural/cross joins,
 * joins without a condition, chains of joins with their own ONs, USING, the column list of a
 * natural join taken from the tables, RIGHT JOIN both spellings, a join of derived tables,
 * joins over SYSTEM_RANGE, the EXCEPT column list, and the refusals. The EXPLAIN forms are
 * left out: the plan text of a join is compared where the plans are the subject.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class JoinsScriptOracleTests
{
    [JavaOracleFact]
    public void JoinsScriptAnswersTheReference()
    {
        string[] statements =
        [
            "create table a(a int) as select 1;",
            "create table b(b int) as select 1;",
            "create table c(c int) as select x from system_range(1, 2);",
            "select * from a inner join b on a=b right outer join c on c=a;",
            "select * from c left outer join (a inner join b on b=a) on c=a;",
            "select * from c left outer join a on c=a inner join b on b=a;",
            "drop table a, b, c;",
            "create table test(a int, b int) as select x, x from system_range(1, 100);",
            "drop table test;",
            "create table test(id identity) as select x from system_range(1, 4);",
            "select a.id from test a inner join test b on a.id > b.id and b.id < 3 group by a.id;",
            "drop table test;",
            "select * from system_range(1, 3) t1 inner join system_range(2, 3) t2 inner join system_range(1, 2) t3 on t3.x=t2.x on t1.x=t2.x;",
            "CREATE TABLE PARENT(ID INT PRIMARY KEY);",
            "CREATE TABLE CHILD(ID INT PRIMARY KEY);",
            "INSERT INTO PARENT VALUES(1);",
            "SELECT * FROM PARENT P LEFT OUTER JOIN CHILD C ON C.PARENTID=P.ID;",
            "DROP TABLE PARENT, CHILD;",
            "create table t1 (i int);",
            "create table t2 (i int);",
            "create table t3 (i int);",
            "select a.i from t1 a inner join (select a.i from t2 a inner join (select i from t3) b on a.i=b.i) b on a.i=b.i;",
            "insert into t1 values (1);",
            "insert into t2 values (1);",
            "insert into t3 values (1);",
            "select a.i from t1 a inner join (select a.i from t2 a inner join (select i from t3) b on a.i=b.i) b on a.i=b.i;",
            "drop table t1, t2, t3;",
            "CREATE TABLE TESTA(ID IDENTITY);",
            "CREATE TABLE TESTB(ID IDENTITY);",
            "DROP TABLE IF EXISTS TESTA, TESTB;",
            "create table one (id int primary key);",
            "create table two (id int primary key, val date);",
            "insert into one values(0);",
            "insert into one values(1);",
            "insert into one values(2);",
            "insert into two values(0, null);",
            "insert into two values(1, DATE'2006-01-01');",
            "insert into two values(2, DATE'2006-07-01');",
            "insert into two values(3, null);",
            "select * from one;",
            "select * from two;",
            "select * from one natural join two left join two three on one.id=three.id left join one four on two.id=four.id where three.val is null;",
            "select * from one natural join two left join two three on one.id=three.id left join one four on two.id=four.id where three.val>=DATE'2006-07-01';",
            "select * from one natural join two left join two three on one.id=three.id left join one four on two.id=four.id where three.val is null or three.val>=DATE'2006-07-01';",
            "select * from one natural join two left join one four on two.id=four.id left join two three on one.id=three.id where three.val is null or three.val>=DATE'2006-07-01';",
            "drop table one;",
            "drop table two;",
            "create table test1 (id int primary key);",
            "create table test2 (id int primary key);",
            "create table test3 (id int primary key);",
            "insert into test1 values(1);",
            "insert into test2 values(1);",
            "insert into test3 values(1);",
            "select * from test1 inner join test2 on test1.id=test2.id left outer join test3 on test2.id=test3.id where test3.id is null;",
            "insert into test1 select x from system_range(2, 1000);",
            "select * from test1 inner join test2 on test1.id=test2.id left outer join test3 on test2.id=test3.id where test3.id is null;",
            "SELECT TEST1.ID, TEST2.ID, TEST3.ID FROM TEST2 LEFT OUTER JOIN TEST3 ON TEST2.ID = TEST3.ID INNER JOIN TEST1 WHERE TEST3.ID IS NULL AND TEST1.ID = TEST2.ID;",
            "drop table test1;",
            "drop table test2;",
            "drop table test3;",
            "create table left_hand (id int primary key);",
            "create table right_hand (id int primary key);",
            "insert into left_hand values(0);",
            "insert into left_hand values(1);",
            "insert into right_hand values(0);",
            "select * from left_hand left outer join right_hand on left_hand.id=right_hand.id;",
            "select * from left_hand left join right_hand on left_hand.id=right_hand.id;",
            "select * from left_hand natural join right_hand;",
            "select * from left_hand left outer join right_hand on left_hand.id=right_hand.id where left_hand.id=1;",
            "select * from left_hand left join right_hand on left_hand.id=right_hand.id where left_hand.id=1;",
            "select * from left_hand natural join right_hand where left_hand.id=1;",
            "select * from left_hand left outer join right_hand on left_hand.id=right_hand.id where left_hand.id=1 having right_hand.id=2;",
            "select * from left_hand left join right_hand on left_hand.id=right_hand.id where left_hand.id=1 having right_hand.id=2;",
            "select * from left_hand natural join right_hand where left_hand.id=1 having right_hand.id=2;",
            "select * from left_hand left outer join right_hand on left_hand.id=right_hand.id where left_hand.id=1 group by left_hand.id having right_hand.id=2;",
            "select * from left_hand left join right_hand on left_hand.id=right_hand.id where left_hand.id=1 group by left_hand.id having right_hand.id=2;",
            "select * from left_hand natural join right_hand where left_hand.id=1 group by left_hand.id having right_hand.id=2;",
            "drop table right_hand;",
            "drop table left_hand;",
            "CREATE TABLE T1(ID INT PRIMARY KEY, NAME VARCHAR(255));",
            "CREATE TABLE T2(ID INT PRIMARY KEY, NAME VARCHAR(255));",
            "CREATE TABLE T3(ID INT PRIMARY KEY, NAME VARCHAR(255));",
            "INSERT INTO T1 VALUES(1, 'Hello');",
            "INSERT INTO T1 VALUES(2, 'World');",
            "INSERT INTO T1 VALUES(3, 'Peace');",
            "INSERT INTO T2 VALUES(1, 'Hello');",
            "INSERT INTO T2 VALUES(2, 'World');",
            "INSERT INTO T3 VALUES(1, 'Hello');",
            "SELECT * FROM t1 left outer join t2 on t1.id=t2.id;",
            "SELECT * FROM t1 left outer join t2 on t1.id=t2.id left outer join t3 on t1.id=t3.id;",
            "SELECT * FROM t1 left outer join t2 on t1.id=t2.id inner join t3 on t1.id=t3.id;",
            "drop table t1;",
            "drop table t2;",
            "drop table t3;",
            "CREATE TABLE TEST(ID INT PRIMARY KEY, parent int, sid int);",
            "create index idx_p on test(sid);",
            "insert into test select x, x, x from system_range(0,20);",
            "select * from test l0 inner join test l1 on l0.sid=l1.sid, test l3 where l0.sid=l3.parent;",
            "select * from test l0 inner join test l1 on l0.sid=l1.sid inner join test l2 on l0.sid=l2.id, test l5 inner join test l3 on l5.sid=l3.sid inner join test l4 on l5.sid=l4.id where l2.id is not null and l0.sid=l5.parent;",
            "DROP TABLE IF EXISTS TEST;",
            "create table t1(id int, name varchar);",
            "insert into t1 values(1, 'hi'), (2, 'world');",
            "create table t2(id int, name varchar);",
            "insert into t2 values(1, 'Hallo'), (3, 'Welt');",
            "select * from t1 join t2 on t1.id=t2.id;",
            "select * from t1 left join t2 on t1.id=t2.id;",
            "select * from t1 right join t2 on t1.id=t2.id;",
            "select * from t1 cross join t2;",
            "select * from t1 natural join t2;",
            "drop table t1;",
            "drop table t2;",
            "create table customer(customerid int, customer_name varchar);",
            "insert into customer values(0, 'Acme');",
            "create table invoice(customerid int, invoiceid int, invoice_text varchar);",
            "insert into invoice values(0, 1, 'Soap'), (0, 2, 'More Soap');",
            "create table INVOICE_LINE(line_id int, invoiceid int, customerid int, line_text varchar);",
            "insert into INVOICE_LINE values(10, 1, 0, 'Super Soap'), (20, 1, 0, 'Regular Soap');",
            "select * from customer c natural join invoice i natural join INVOICE_LINE l;",
            "select c.*, i.*, l.* from customer c natural join invoice i natural join INVOICE_LINE l;",
            "drop table customer;",
            "drop table invoice;",
            "drop table INVOICE_LINE;",
            "CREATE TABLE PARENT(ID INT, NAME VARCHAR(20));",
            "CREATE TABLE CHILD(ID INT, PARENTID INT, NAME VARCHAR(20));",
            "INSERT INTO PARENT VALUES(1, 'Sue');",
            "INSERT INTO PARENT VALUES(2, 'Joe');",
            "INSERT INTO CHILD VALUES(100, 1, 'Simon');",
            "INSERT INTO CHILD VALUES(101, 1, 'Sabine');",
            "SELECT * FROM PARENT P INNER JOIN CHILD C ON P.ID = C.PARENTID;",
            "SELECT * FROM PARENT P LEFT OUTER JOIN CHILD C ON P.ID = C.PARENTID;",
            "SELECT * FROM CHILD C RIGHT OUTER JOIN PARENT P ON P.ID = C.PARENTID;",
            "DROP TABLE PARENT;",
            "DROP TABLE CHILD;",
            "CREATE TABLE A(A1 INT, A2 INT);",
            "INSERT INTO A VALUES (1, 2);",
            "CREATE TABLE B(B1 INT, B2 INT);",
            "INSERT INTO B VALUES (1, 2);",
            "CREATE TABLE C(B1 INT, C1 INT);",
            "INSERT INTO C VALUES (1, 2);",
            "SELECT * FROM A LEFT JOIN B ON TRUE;",
            "SELECT A.A1, A.A2, B.B1, B.B2 FROM A RIGHT JOIN B ON TRUE;",
            "SELECT * FROM A LEFT JOIN B;",
            "SELECT A.A1, A.A2, B.B1, B.B2 FROM A RIGHT JOIN B;",
            "SELECT * FROM A LEFT JOIN B ON TRUE NATURAL JOIN C;",
            "SELECT A.A1, A.A2, B.B1, B.B2, C.C1 FROM A RIGHT JOIN B ON TRUE NATURAL JOIN C;",
            "SELECT * FROM A LEFT JOIN B NATURAL JOIN C;",
            "SELECT A.A1, A.A2, B.B1, B.B2, C.C1 FROM A RIGHT JOIN B NATURAL JOIN C;",
            "DROP TABLE A;",
            "DROP TABLE B;",
            "DROP TABLE C;",
            "CREATE TABLE T1(X1 INT);",
            "CREATE TABLE T2(X2 INT);",
            "CREATE TABLE T3(X3 INT);",
            "CREATE TABLE T4(X4 INT);",
            "CREATE TABLE T5(X5 INT);",
            "INSERT INTO T1 VALUES (1);",
            "INSERT INTO T1 VALUES (NULL);",
            "INSERT INTO T2 VALUES (1);",
            "INSERT INTO T2 VALUES (NULL);",
            "INSERT INTO T3 VALUES (1);",
            "INSERT INTO T3 VALUES (NULL);",
            "INSERT INTO T4 VALUES (1);",
            "INSERT INTO T4 VALUES (NULL);",
            "INSERT INTO T5 VALUES (1);",
            "INSERT INTO T5 VALUES (NULL);",
            "SELECT T1.X1, T2.X2, T3.X3, T4.X4, T5.X5 FROM ( T1 INNER JOIN ( T2 LEFT OUTER JOIN ( T3 INNER JOIN T4 ON T3.X3 = T4.X4 ) ON T2.X2 = T4.X4 ) ON T1.X1 = T2.X2 ) INNER JOIN T5 ON T2.X2 = T5.X5;",
            "DROP TABLE T1, T2, T3, T4, T5;",
            "CREATE TABLE A(X INT);",
            "CREATE TABLE B(Y INT);",
            "CREATE TABLE C(Z INT);",
            "SELECT A.X FROM A JOIN B ON A.X = B.Y AND B.Y >= COALESCE((SELECT Z FROM C FETCH FIRST ROW ONLY), 0);",
            "DROP TABLE A, B, C;",
            "CREATE TABLE TEST(A INT PRIMARY KEY);",
            "SELECT * FROM TEST X LEFT OUTER JOIN TEST Y ON Y.A = X.A || '1';",
            "DROP TABLE TEST;",
            "CREATE TABLE T1(A INT, B INT) AS VALUES (1, 10), (2, 20), (4, 40), (6, 6), (7, 7);",
            "CREATE TABLE T2(A INT, B INT) AS VALUES (1, 100), (2, 200), (5, 500), (6, 6), (8, 7);",
            "SELECT T1.B, T2.B FROM T1 INNER JOIN T2 USING (A);",
            "SELECT * FROM T1 INNER JOIN T2 USING (A);",
            "SELECT * FROM T1 INNER JOIN T2 USING (B);",
            "SELECT T1.B, T2.B FROM T1 INNER JOIN T2 USING (A, B);",
            "SELECT * FROM T1 INNER JOIN T2 USING (B, A);",
            "DROP TABLE T1, T2;",
            "SELECT * FROM (VALUES(1, 'A'), (2, 'B')) T1(A, B) JOIN (VALUES(2, 'C'), (3, 'D')) T2(A, C) USING (A);",
            "SELECT * FROM (VALUES(1, 'A'), (2, 'B')) T1(A, B) LEFT JOIN (VALUES(2, 'C'), (3, 'D')) T2(A, C) USING (A);",
            "SELECT * FROM (VALUES(1, 'A'), (2, 'B')) T1(A, B) RIGHT JOIN (VALUES(2, 'C'), (3, 'D')) T2(A, C) USING (A);",
            "SELECT T1.*, T2.* FROM (VALUES(1, 'A'), (2, 'B')) T1(A, B) RIGHT JOIN (VALUES(2, 'C'), (3, 'D')) T2(A, C) USING (A);",
            "SELECT * FROM (VALUES(1, 'A'), (2, 'B')) T1(A, B) NATURAL JOIN (VALUES(2, 'C'), (3, 'D')) T2(A, C);",
            "CREATE TABLE T1(A VARCHAR_IGNORECASE PRIMARY KEY, B VARCHAR) AS (VALUES ('a', 'A'), ('b', 'B'));",
            "CREATE TABLE T2(A VARCHAR_IGNORECASE PRIMARY KEY, C VARCHAR) AS (VALUES ('B', 'C'), ('C', 'D'));",
            "SELECT * FROM T1 RIGHT JOIN T2 USING (A);",
            "DROP TABLE T1, T2;",
            "CREATE TABLE T1(A INT PRIMARY KEY, B VARCHAR) AS (VALUES (1, 'A'), (2, 'B'));",
            "CREATE TABLE T2(A INT PRIMARY KEY, C VARCHAR) AS (VALUES (2, 'C'), (3, 'D'));",
            "SELECT * FROM T1 RIGHT JOIN T2 USING (A);",
            "SELECT * EXCEPT (T1.A) FROM T1 RIGHT JOIN T2 USING (A);",
            "SELECT * EXCEPT (T2.A) FROM T1 RIGHT JOIN T2 USING (A);",
            "DROP TABLE T1, T2;",
            "CREATE SCHEMA S1;",
            "CREATE SCHEMA S2;",
            "CREATE TABLE S1.T(A VARCHAR_IGNORECASE, B INT) AS (VALUES ('a', 2));",
            "CREATE TABLE S2.T(A VARCHAR_IGNORECASE, B INT) AS (VALUES ('A', 3));",
            "SELECT * FROM S1.T RIGHT JOIN S2.T USING(A);",
            "DROP SCHEMA S1 CASCADE;",
            "DROP SCHEMA S2 CASCADE;",
            "CREATE TABLE T1(C1 INTEGER) AS VALUES 1, 2, 4;",
            "CREATE TABLE T2(C2 INTEGER) AS VALUES 1, 3, 4;",
            "CREATE TABLE T3(C3 INTEGER) AS VALUES 2, 3, 4;",
            "SELECT * FROM T1 JOIN T2 LEFT JOIN T3 ON T2.C2 = T3.C3 ON T1.C1 = T2.C2;",
            "SELECT * FROM T1 RIGHT JOIN T2 LEFT JOIN T3 ON T2.C2 = T3.C3 ON T1.C1 = T2.C2;",
            "DROP TABLE T1, T2, T3;",
            "SELECT X.A, Y.B, Z.C FROM (SELECT 1 A) X JOIN ( (SELECT 1 B) Y JOIN (SELECT 1 C) Z ON Z.C = Y.B ) ON Y.B = X.A;",
            "WITH A(A) AS (VALUES (1)), B(B) AS (VALUES (1)), C(C) AS (VALUES (1)) SELECT A.A, ( SELECT B.B FROM B JOIN C ON B.B = A.A AND C.C = B.B ) FROM A;",
            "WITH A(A) AS (VALUES (1)), B(B) AS (VALUES (1)), C(C) AS (VALUES (1)) SELECT A.A, ( SELECT B.B FROM B LEFT JOIN C ON B.B = A.A AND C.C = B.B ) FROM A;",
            "CREATE TABLE A(ID1 INT PRIMARY KEY);",
            "CREATE TABLE B(ID2 INT PRIMARY KEY);",
            "SELECT * FROM A C JOIN B C;",
            "SELECT * FROM A C, B c;",
            "SELECT * FROM A C LEFT JOIN B C ON TRUE;",
            "SELECT * FROM A C CROSS JOIN (A D JOIN B C ON TRUE);",
            "SELECT * FROM A, B A;",
            "SELECT * FROM A, A A;",
            "SELECT * FROM A C JOIN A D;",
            "SELECT * FROM A WHERE EXISTS (SELECT * FROM B A);",
            "CREATE SCHEMA S1;",
            "CREATE SCHEMA S2;",
            "CREATE TABLE S1.T(ID INT);",
            "CREATE TABLE S2.T(ID INT);",
            "SELECT COUNT(*) FROM S1.T, S2.T;",
            "DROP SCHEMA S1 CASCADE;",
            "DROP SCHEMA S2 CASCADE;",
            "DROP TABLE A, B;"
        ];
        RunBoth(statements);
    }

    /// <summary>
    /// Runs the statements on both sides and asserts the same answers.
    /// </summary>
    /// <param name="statements">the statements</param>
    private static void RunBoth(IReadOnlyList<string> statements)
    {
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the rows, or the code of
    /// the error.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("joins_script_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)
                    || sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                {
                    ResultInterface result = container.Query(-1);
                    int visible = result.GetVisibleColumnCount();
                    while (result.Next())
                    {
                        Value?[] row = result.CurrentRow();
                        List<string> cells = [];
                        for (int i = 0; i < visible; i++)
                        {
                            Value? value = row[i];
                            cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
                        }

                        lines.Add("=" + string.Join(":", cells));
                    }
                }
                else
                {
                    lines.Add("=" + container.ExecuteUpdate(false).GetUpdateCount());
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        session.Close();
        return lines;
    }

    /// <summary>
    /// Runs the statements on the reference and writes what the question answered.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunJava(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:joins_script_oracle", "sa", "");
            java.sql.Statement stat = conn.createStatement();
            for (int i = 0; i < inputs.length; i++) {
              try {
                boolean isQuery = stat.execute(inputs[i]);
                if (isQuery) {
                  java.sql.ResultSet rs = stat.getResultSet();
                  int columns = rs.getMetaData().getColumnCount();
                  while (rs.next()) {
                    StringBuilder line = new StringBuilder("=");
                    for (int c = 1; c <= columns; c++) {
                      if (c > 1) {
                        line.append(':');
                      }
                      String s = rs.getString(c);
                      line.append(rs.wasNull() ? "-" : s);
                    }
                    System.out.println(line);
                  }
                } else {
                  System.out.println("=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

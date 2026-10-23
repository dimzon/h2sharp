/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * FOREIGN KEY, checked against the reference running the same text.
 *
 * A foreign key is a constraint of its own in the schema: the child table refuses a row
 * whose parent is missing (23506), and the parent table refuses a change that would strand
 * a child (23503), unless the action says what happens to the child instead.
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
public sealed class ForeignKeyOracleTests
{
    /// <summary>
    /// Inserts, updates and deletes against foreign keys, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void InsertsUpdatesAndDeletesAgainstForeignKeysAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE PARENT(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID))",
            "INSERT INTO PARENT VALUES (1, 'one'), (2, 'two')",
            "INSERT INTO CHILD VALUES (10, 1), (11, 2), (12, NULL)",
            "SELECT COUNT(*) FROM CHILD",
            "INSERT INTO CHILD VALUES (13, 3)",
            "SELECT COUNT(*) FROM CHILD",
            "DELETE FROM PARENT WHERE ID = 1",
            "UPDATE PARENT SET NAME = 'TWO' WHERE ID = 2",
            "DELETE FROM CHILD WHERE ID = 11",
            "DELETE FROM PARENT WHERE ID = 2",
            "SELECT ID, PID FROM CHILD ORDER BY ID",
            "DELETE FROM PARENT WHERE ID = 1",
            "UPDATE CHILD SET PID = 5 WHERE ID = 10",
            "UPDATE CHILD SET PID = 99 WHERE ID = 10",
            "UPDATE CHILD SET PID = NULL WHERE ID = 10",
            "SELECT PID FROM CHILD WHERE ID = 10",
            "UPDATE CHILD SET PID = 1 WHERE ID = 10",
            "DELETE FROM PARENT WHERE ID = 1",
            "SELECT COUNT(*) FROM PARENT",
            "SELECT COUNT(*) FROM CHILD",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The other spellings of the same constraint, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheOtherSpellingsOfTheConstraintAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE PARENT(ID INT PRIMARY KEY)",
            "INSERT INTO PARENT VALUES (1), (2)",
            "CREATE TABLE C1(ID INT PRIMARY KEY, PID INT REFERENCES PARENT(ID))",
            "CREATE TABLE C2(ID INT PRIMARY KEY, PID INT CONSTRAINT C2_FK REFERENCES PARENT(ID))",
            "CREATE TABLE C3(ID INT, PID INT, CONSTRAINT C3_FK FOREIGN KEY (PID) REFERENCES PARENT(ID))",
            "CREATE TABLE C4(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES (ID))",
            "INSERT INTO C1 VALUES (1, 1)",
            "INSERT INTO C1 VALUES (2, 99)",
            "INSERT INTO C4 VALUES (1, 1)",
            "INSERT INTO C4 VALUES (2, 99)",
            "CREATE TABLE EMP(ID INT PRIMARY KEY, MGR INT, FOREIGN KEY (MGR) REFERENCES EMP(ID))",
            "INSERT INTO EMP VALUES (1, NULL), (2, 1)",
            "INSERT INTO EMP VALUES (3, 99)",
            "DELETE FROM EMP WHERE ID = 1",
            "DELETE FROM EMP WHERE ID = 2",
            "DELETE FROM EMP WHERE ID = 1",
            "SELECT ID FROM EMP ORDER BY ID",
            "CREATE TABLE PA(A INT, B INT, PRIMARY KEY(A, B))",
            "CREATE TABLE CA(ID INT PRIMARY KEY, PA INT, PB INT, FOREIGN KEY (PA, PB) REFERENCES PA(A, B))",
            "INSERT INTO PA VALUES (1, 1), (1, 2)",
            "INSERT INTO CA VALUES (1, 1, 2)",
            "INSERT INTO CA VALUES (2, 1, 3)",
            "INSERT INTO CA VALUES (3, 1, NULL)",
            "INSERT INTO CA VALUES (4, NULL, NULL)",
            "DELETE FROM PA WHERE A = 1 AND B = 1",
            "DELETE FROM PA WHERE A = 1 AND B = 2",
            "CREATE TABLE CD(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID) DEFERRABLE)",
            "CREATE TABLE CE(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID) NOT DEFERRABLE)",
            "INSERT INTO CD VALUES (1, 1)",
            "INSERT INTO CE VALUES (1, 2)",
            "SELECT COUNT(*) FROM CD",
            "SELECT COUNT(*) FROM CE",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The actions on the parent, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheActionsOnTheParentAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE D1(ID INT PRIMARY KEY)",
            "INSERT INTO D1 VALUES (1), (2)",
            "CREATE TABLE DC(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES D1(ID) ON DELETE CASCADE)",
            "INSERT INTO DC VALUES (10, 1), (11, 1), (12, 2), (13, NULL)",
            "DELETE FROM D1 WHERE ID = 1",
            "SELECT ID, DID FROM DC ORDER BY ID",
            "CREATE TABLE E1(ID INT PRIMARY KEY)",
            "INSERT INTO E1 VALUES (1)",
            "CREATE TABLE EU(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES E1(ID) ON UPDATE CASCADE)",
            "INSERT INTO EU VALUES (10, 1)",
            "SELECT DID FROM EU",
            "CREATE TABLE F1(ID INT PRIMARY KEY)",
            "INSERT INTO F1 VALUES (5)",
            "CREATE TABLE SN(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES F1(ID) ON DELETE SET NULL)",
            "INSERT INTO SN VALUES (10, 5)",
            "DELETE FROM F1 WHERE ID = 5",
            "SELECT DID FROM SN",
            "CREATE TABLE G1(ID INT PRIMARY KEY)",
            "INSERT INTO G1 VALUES (6)",
            "CREATE TABLE SD(ID INT PRIMARY KEY, DID INT DEFAULT 7, FOREIGN KEY (DID) REFERENCES G1(ID) ON DELETE SET DEFAULT)",
            "INSERT INTO SD VALUES (10, 6)",
            "DELETE FROM G1 WHERE ID = 6",
            "SELECT DID FROM SD",
            "CREATE TABLE H1(ID INT PRIMARY KEY)",
            "INSERT INTO H1 VALUES (9)",
            "CREATE TABLE ND(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES H1(ID) ON DELETE SET DEFAULT)",
            "INSERT INTO ND VALUES (10, 9)",
            "DELETE FROM H1 WHERE ID = 9",
            "CREATE TABLE R1(ID INT PRIMARY KEY)",
            "INSERT INTO R1 VALUES (8)",
            "CREATE TABLE UA(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES R1(ID) ON UPDATE CASCADE ON DELETE CASCADE)",
            "INSERT INTO UA VALUES (10, 8)",
            "SELECT DID FROM UA",
            "DELETE FROM R1 WHERE ID = 8",
            "SELECT COUNT(*) FROM UA",
            "INSERT INTO R1 VALUES (81)",
            "CREATE TABLE RA(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES R1(ID) ON DELETE RESTRICT)",
            "CREATE TABLE NA(ID INT PRIMARY KEY, DID INT, FOREIGN KEY (DID) REFERENCES R1(ID) ON DELETE NO ACTION)",
            "INSERT INTO RA VALUES (10, 81)",
            "INSERT INTO NA VALUES (10, 81)",
            "DELETE FROM R1 WHERE ID = 81",
            "SELECT COUNT(*) FROM RA",
            "SELECT COUNT(*) FROM NA",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The catalog answers about the keys, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void TheCatalogAnswersAboutTheKeysAsTheReferenceDoes()
    {
        string[] statements =
        [
            "CREATE TABLE PARENT(ID INT PRIMARY KEY)",
            "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, CONSTRAINT FK_C FOREIGN KEY (PID) REFERENCES PARENT(ID) ON DELETE CASCADE)",
            "SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'CHILD' ORDER BY CONSTRAINT_NAME",
            "SELECT CONSTRAINT_NAME, COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = 'CHILD' ORDER BY ORDINAL_POSITION",
            "SELECT CONSTRAINT_NAME, UNIQUE_CONSTRAINT_NAME, MATCH_OPTION, UPDATE_RULE, DELETE_RULE FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Updates of the referenced key, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void UpdatesOfTheReferencedKeyAreTheSameAsTheReference()
    {
        string[] statements =
        [
            "CREATE TABLE PARENT(ID INT PRIMARY KEY)",
            "INSERT INTO PARENT VALUES (1), (2)",
            "CREATE TABLE CHILD(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID))",
            "INSERT INTO CHILD VALUES (10, 2)",
            "UPDATE PARENT SET ID = 5 WHERE ID = 2",
            "CREATE TABLE CU(ID INT PRIMARY KEY, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID) ON UPDATE CASCADE)",
            "INSERT INTO CU VALUES (10, 1)",
            "UPDATE PARENT SET ID = 6 WHERE ID = 1",
            "SELECT PID FROM CU",
            "SELECT ID, PID FROM CHILD ORDER BY ID",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// The answers to the keys that cannot be made, the same on both sides.
    /// </summary>
    [JavaOracleFact]
    public void KeysThatCannotBeMadeFailAsTheReferenceFails()
    {
        string[] statements =
        [
            "CREATE TABLE PARENT(ID INT PRIMARY KEY, NAME VARCHAR(20))",
            "CREATE TABLE NOPK(ID INT)",
            "CREATE TABLE CHILD(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES NOSUCH(ID))",
            "CREATE TABLE CHILD(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES NOPK(ID))",
            "CREATE TABLE CHILD(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(NOSUCH))",
            "CREATE TABLE CHILD(ID INT, NOSUCH INT, FOREIGN KEY (NOSUCH2) REFERENCES PARENT(ID))",
            "CREATE TABLE CHILD(ID INT, PID INT, P2 INT, FOREIGN KEY (PID, P2) REFERENCES PARENT(ID))",
            "CREATE TABLE CHILD(ID INT PRIMARY KEY, NAME VARCHAR(20), FOREIGN KEY (NAME) REFERENCES PARENT(ID))",
            "CREATE TABLE CHILD(ID INT, PID VARCHAR(20), FOREIGN KEY (PID) REFERENCES PARENT(ID))",
            "CREATE TABLE CHILD(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES PARENT)",
            "CREATE TABLE CHILD2(ID INT, PID INT, FOREIGN KEY (PID) REFERENCES PARENT(ID) NOCHECK)",
            "ALTER TABLE CHILD2 ADD CONSTRAINT FK2 FOREIGN KEY (PID) REFERENCES PARENT(ID)",
            "CREATE TABLE CHILD3(ID INT, PID INT)",
            "INSERT INTO CHILD3 VALUES (1, 99)",
            "ALTER TABLE CHILD3 ADD CONSTRAINT FK3 FOREIGN KEY (PID) REFERENCES PARENT(ID)",
            "ALTER TABLE CHILD3 ADD CONSTRAINT FK3N FOREIGN KEY (PID) REFERENCES PARENT(ID) NOCHECK",
            "INSERT INTO CHILD3 VALUES (2, 1)",
            "SELECT COUNT(*) FROM CHILD3",
            "CREATE TABLE GEN(ID INT PRIMARY KEY, V INT GENERATED ALWAYS AS (ID + 1))",
            "CREATE TABLE GENCHILD(ID INT PRIMARY KEY, V INT, FOREIGN KEY (V) REFERENCES GEN(V) ON DELETE SET NULL)",
            "CREATE TABLE GENCHILD2(ID INT PRIMARY KEY, V INT, FOREIGN KEY (V) REFERENCES GEN(ID) ON UPDATE CASCADE)",
            "CREATE TABLE MISSING(ID INT, FOREIGN KEY (ID) REFERENCES MISSING(ID))",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJava(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    /// <summary>
    /// Runs the statements here and writes what the question answered: the error, or the rows.
    /// </summary>
    /// <param name="statements">the statements</param>
    /// <returns>the lines</returns>
    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(new ConnectionInfo("jdbc:h2:mem:foreign_key_oracle")), "sa")
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
                if (sql.StartsWith("SELECT", StringComparison.Ordinal))
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
                    _ = container.ExecuteUpdate(false);
                }
            }
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name);
            }
        }

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
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:foreign_key_oracle");
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
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en"]);
}

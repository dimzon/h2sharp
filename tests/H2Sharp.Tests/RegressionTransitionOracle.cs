/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0. */
using System.Collections.Generic;
using H2Sharp.Tests.Oracle;

namespace H2Sharp.Tests;

/// <summary>Preparation and execution of the unchanged regression SQL on the pinned jar.</summary>
internal static class RegressionTransitionOracle
{
    internal static List<string> Read(string sql) => OracleRunner.Run([sql], """
        try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:regression_transition")) {
          java.sql.Statement s = c.createStatement();
          s.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
          s.execute("INSERT INTO T VALUES(3,'c'),(1,'b'),(2,'b'),(4,NULL)");
          org.h2.engine.SessionLocal session =
            (org.h2.engine.SessionLocal)((org.h2.jdbc.JdbcConnection)c).getSession();
          org.h2.command.CommandInterface command;
          try {
            command = session.prepareCommand(inputs[0]);
            System.out.println("PREPARE:OK");
          } catch (org.h2.message.DbException e) {
            System.out.println("PREPARE:ERR:" + e.getErrorCode());
            return;
          }
          try (org.h2.result.ResultInterface r = command.executeQuery(0,0,false)) {
            while (r.next()) {
              StringBuilder row = new StringBuilder("ROW:");
              for (int col=0; col<r.getVisibleColumnCount(); col++) {
                if (col>0) row.append('|');
                String value = r.currentRow()[col].getString();
                row.append(value == null ? "NULL" : value);
              }
              System.out.println(row);
            }
            System.out.println("EXECUTE:OK");
          } catch (org.h2.message.DbException e) {
            System.out.println("EXECUTE:ERR:" + e.getErrorCode());
          } finally { command.close(); }
        }
        """, extraJavaArgs: ["-Duser.language=en"]);
}

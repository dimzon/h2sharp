/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A throwaway in-memory database for one benchmark. The name is unique per benchmark so
 * that two benchmarks never share a database, and every connection is disposed by the
 * benchmark's own cleanup.
 *
 * Statements that create objects go through ExecuteNonQuery here, and the SELECT benches
 * drain readers fully so that the local result is materialized and measured.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;

namespace H2Sharp.Benchmarks;

internal static class PerfDb
{
    /// <summary>
    /// Opens a named in-memory database.
    /// </summary>
    /// <param name="name">the database name</param>
    /// <returns>the open connection</returns>
    internal static H2Connection Open(string name)
    {
        H2Connection connection = new("Data Source=" + name + ";Storage=Memory");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Executes a statement that returns nothing.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    internal static void ExecuteNonQuery(H2Connection connection, string sql)
    {
        using H2Command command = new(sql, connection);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates the TEST table: two integers and a string column.
    /// </summary>
    /// <param name="connection">the connection</param>
    internal static void CreateTestTable(H2Connection connection)
    {
        ExecuteNonQuery(connection, "CREATE TABLE TEST(A INT, B INT, C VARCHAR(50))");
    }

    /// <summary>
    /// Fills TEST with N rows of the given width.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="count">how many rows</param>
    internal static void FillTestTable(H2Connection connection, int count)
    {
        for (int batch = 0; batch < count; batch += 500)
        {
            System.Text.StringBuilder sb = new("INSERT INTO TEST(A, B, C) VALUES ");
            int end = Math.Min(batch + 500, count);
            for (int i = batch; i < end; i++)
            {
                if (i > batch)
                {
                    sb.Append(',');
                }

                sb.Append('(').Append(i).Append(',').Append(i % 100).Append(",'name").Append(i).Append("')");
            }

            ExecuteNonQuery(connection, sb.ToString());
        }
    }
}

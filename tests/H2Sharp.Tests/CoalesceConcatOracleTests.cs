/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The GREATEST/LEAST comparison with NULLs allowed, including composite values whose own
 * comparison meets a NULL element, and the concatenation of binary, array and NULL operands
 * taken from columns and from bound parameters.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Data;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CoalesceConcatOracleTests
{
    [JavaOracleFact]
    public void GreatestAndLeastWithIgnoredNullsMatchJdbc()
    {
        // The mode where GREATEST and LEAST skip NULL arguments and remember a NULL inside a
        // composite comparison; the regular mode returns NULL as soon as one argument is.
        DatabaseSettingsOracleTests.Compare("MODE=Legacy", [
            "SELECT GREATEST(1, 2, NULL), LEAST(1, 2, NULL)",
            "SELECT GREATEST(1, NULL, 2, NULL), LEAST(1, NULL, 2, NULL)",
            "SELECT GREATEST(ARRAY[1, NULL], ARRAY[1, 3]), LEAST(ARRAY[1, NULL], ARRAY[1, 3])",
            "SELECT GREATEST(ARRAY[1, NULL], ARRAY[1, NULL]), LEAST(ARRAY[1, NULL], ARRAY[1, NULL])",
            "SELECT GREATEST(ROW(1, NULL), ROW(1, 3)), LEAST(ROW(1, NULL), ROW(1, 3))",
            "SELECT GREATEST(ROW(1, 2), ROW(1, NULL)), LEAST(ROW(1, 2), ROW(1, NULL))",
            "SELECT GREATEST(NULL, NULL), LEAST(NULL, NULL)",
            "SELECT GREATEST('a', 'b', NULL), LEAST('a', 'b', NULL)",
            "SELECT GREATEST(DATE '2020-01-01', DATE '2020-01-03', NULL)",
            "SELECT GREATEST(ARRAY[1, NULL], ARRAY[1, NULL], ARRAY[1, 3])",
        ]);
    }

    [JavaOracleFact]
    public void ConcatenationOfColumnsAndNullsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE B(X VARBINARY, Y VARBINARY)",
            "INSERT INTO B VALUES (X'01', X'02'), (NULL, X'03'), (X'04', NULL), (X'', X'05')",
            "SELECT X || Y FROM B",
            "SELECT CAST(NULL AS VARBINARY) || X FROM B",
            "SELECT X || CAST(NULL AS VARBINARY) FROM B",
            "SELECT X'01' || X FROM B",
            "SELECT X || Y || X FROM B",
            "CREATE TABLE A(P INT ARRAY, Q INT ARRAY)",
            "INSERT INTO A VALUES (ARRAY[1], ARRAY[2]), (NULL, ARRAY[3]), (ARRAY[4], NULL), "
                + "(ARRAY[], ARRAY[5])",
            "SELECT P || Q FROM A",
            "SELECT CAST(NULL AS INT ARRAY) || P FROM A",
            "SELECT ARRAY[9] || P FROM A",
            "SELECT P || 9 FROM A",
            "SELECT P || Q || ARRAY[7] FROM A",
        ]);
    }

    [Fact]
    public void BoundOperandsConcatenateWithTheirType()
    {
        using H2Connection connection = new(
            "Data Source=concat_params_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ? || ?";
        DbParameter first = command.CreateParameter();
        first.Value = new byte[] { 1 };
        command.Parameters.Add(first);
        DbParameter second = command.CreateParameter();
        second.Value = new byte[] { 2, 3 };
        command.Parameters.Add(second);
        command.Prepare();
        using (DbDataReader reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            // The parameters carry no type when the statement is prepared, so the concatenation
            // is the character one: the bytes become the characters 0x01, 0x02, 0x03, which is
            // what the reference's JDBC statement answers for the same call.
            Assert.Equal("",
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture));
        }

        command.CommandText = "SELECT ? || ?";
        using DbCommand text = connection.CreateCommand();
        text.CommandText = "SELECT ? || ?";
        DbParameter left = text.CreateParameter();
        left.Value = "ab";
        text.Parameters.Add(left);
        DbParameter right = text.CreateParameter();
        right.Value = "cd";
        text.Parameters.Add(right);
        text.Prepare();
        using (DbDataReader reader = text.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal("abcd", Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture));
        }
    }
}

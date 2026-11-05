/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * DUAL and DB2/Derby SYSIBM.SYSDUMMY1: the pseudo-table has no columns and one row, a real
 * table with the same name wins when it exists, and the name does not appear in
 * INFORMATION_SCHEMA.TABLES. Compared with H2 2.5 on the JVM.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DualOracleTests
{
    [JavaOracleFact]
    public void DualIsAnEmptyPseudoTableAndARealTableWins()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT * FROM DUAL",
            "SELECT 1 FROM DUAL",
            "SELECT 1 FROM SYS.DUAL",
            "SELECT * FROM DUAL JOIN (SELECT * FROM DUAL) ON 1 = 1",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DUAL'",
            // A real table of the same name is resolved first, as in the reference's
            // readTableOrView: the schema lookup answers before the DUAL fallback.
            "CREATE TABLE DUAL(A INT)",
            "INSERT INTO DUAL VALUES (2)",
            "SELECT A FROM DUAL",
            "SELECT * FROM DUAL",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DUAL'",
            // The explicit SYS name is still the pseudo-table: no table SYS.DUAL exists.
            "SELECT 1 FROM SYS.DUAL",
            "DROP TABLE DUAL",
            "SELECT 1 FROM DUAL",
            // SYSIBM.SYSDUMMY1 is only available in DB2 and Derby modes.
            "SELECT * FROM SYSIBM.SYSDUMMY1",
        ]);
    }

    [JavaOracleFact]
    public void Db2AndDerbyExposeSysDummy1()
    {
        DatabaseSettingsOracleTests.Compare("MODE=DB2", [
            "SELECT * FROM SYSIBM.SYSDUMMY1",
            "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            "SELECT 1 FROM SYSDUMMY1",
            "CREATE TABLE SYSDUMMY1(A INT)",
            "INSERT INTO SYSDUMMY1 VALUES (2)",
            "SELECT A FROM SYSDUMMY1",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SYSDUMMY1'",
            "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            "DROP TABLE SYSDUMMY1",
            "SELECT * FROM SYSIBM.SYSDUMMY1",
        ]);
    }

    [JavaOracleFact]
    public void DerbyAlsoExposesSysDummy1()
    {
        DatabaseSettingsOracleTests.Compare("MODE=Derby", [
            "SELECT * FROM SYSIBM.SYSDUMMY1",
            "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            "SELECT 1 FROM SYSDUMMY1",
        ]);
    }
}

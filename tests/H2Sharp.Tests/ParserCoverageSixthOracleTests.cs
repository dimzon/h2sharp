/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (sixth wave): statement-level commands, more type/precision
 * spellings, ALTER actions, INSERT variants, operators, and additional functions —
 * run through the ADO.NET layer and compared with the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageSixthOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void StatementCommands()
    {
        AssertSameAsReference("parser_sixth1",
            "CREATE TABLE T(ID INT)",
            "BEGIN",
            "INSERT INTO T VALUES(1)",
            "SAVEPOINT S1",
            "INSERT INTO T VALUES(2)",
            "ROLLBACK TO SAVEPOINT S1",
            "SELECT COUNT(*) FROM T",
            "COMMIT",
            "SET OPTIMIZE_REUSE_RESULTS TRUE",
            "SET OPTIMIZE_DISTINCT TRUE",
            "SET QUERY_CACHE_SIZE 16",
            "SET DB_CLOSE_DELAY 0",
            "SET RETENTION_TIME 5",
            "SET THROTTLE 1",
            "SET MAX_MEMORY_UNDO 2",
            "SET MAX_OPERATION_MEMORY 4",
            "SET CACHE_SIZE 256",
            "CHECKPOINT",
            "ANALYZE TABLE T",
            "VALUES (1), (2)",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void TypesAndPrecisionMore()
    {
        AssertSameAsReference("parser_sixth2",
            "CREATE TABLE T1(A CHAR VARYING(10), B CHARACTER LARGE OBJECT, C NATIONAL CHARACTER(5),"
            + " D NCHAR VARYING(7), E NVARCHAR(9), F DOUBLE PRECISION, G FLOAT(20),"
            + " H DECIMAL(20), I NUMERIC(12,0), J TIME(6), K TIMESTAMP(9),"
            + " L INTERVAL DAY(2) TO SECOND(6), M INTERVAL YEAR(3) TO MONTH)",
            "SELECT COUNT(*) FROM T1",
            "DROP TABLE T1",
            "CREATE TABLE T2(A INT ARRAY[5], B VARCHAR(10) ARRAY)",
            "DROP TABLE T2");
    }

    [JavaOracleFact]
    public void AlterActions()
    {
        AssertSameAsReference("parser_sixth3",
            "CREATE TABLE T(ID INT, A INT, B VARCHAR(10), C INT)",
            "ALTER TABLE T ADD COLUMN IF NOT EXISTS A INT",
            "ALTER TABLE T ADD COLUMN D INT DEFAULT 5",
            "INSERT INTO T(ID) VALUES(1)",
            "SELECT D FROM T WHERE ID=1",
            "ALTER TABLE T ALTER COLUMN A SET DEFAULT 3",
            "ALTER TABLE T ALTER COLUMN A DROP DEFAULT",
            "ALTER TABLE T ALTER COLUMN B SET DATA TYPE VARCHAR(30)",
            "ALTER TABLE T ALTER COLUMN C RENAME TO K",
            "ALTER TABLE T ADD CONSTRAINT CK CHECK (ID > 0)",
            "ALTER TABLE T DROP CONSTRAINT CK",
            "ALTER TABLE T DROP COLUMN A CASCADE",
            "ALTER TABLE T DROP COLUMN IF EXISTS MISSING",
            "ALTER TABLE T SET REFERENTIAL_INTEGRITY FALSE",
            "ALTER TABLE T SET REFERENTIAL_INTEGRITY TRUE",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void InsertVariants()
    {
        AssertSameAsReference("parser_sixth4",
            "CREATE TABLE S(ID INT, V VARCHAR(10))",
            "INSERT INTO S VALUES(1,'a'),(2,'b')",
            "SELECT ID, V FROM S ORDER BY ID",
            "INSERT INTO S SELECT ID + 10, V FROM S WHERE ID < 2",
            "SELECT COUNT(*) FROM S",
            "INSERT INTO S(ID) SELECT ID + 100 FROM S WHERE ID = 2",
            "SELECT COUNT(*) FROM S",
            "CREATE TABLE G(ID BIGINT GENERATED ALWAYS AS IDENTITY, V VARCHAR(10))",
            "INSERT INTO G(ID, V) OVERRIDING USER VALUE VALUES(5, 'x')",
            "SELECT ID, V FROM G",
            "DROP TABLE G, S");
    }

    [JavaOracleFact]
    public void OperatorsAndGlobs()
    {
        AssertSameAsReference("parser_sixth5",
            "SELECT 1 & 3, 1 | 2, 1 ^ 3, 1 << 3, 8 >> 2",
            "SELECT CASE WHEN 1 = 1 AND 2 = 2 THEN 1 ELSE 0 END",
            "SELECT CASE WHEN 1 = 1 OR 2 = 3 THEN 1 ELSE 0 END",
            "SELECT ID FROM (SELECT 2 AS ID UNION SELECT 1) T ORDER BY ID NULLS LAST",
            "SELECT ID FROM (SELECT 2 AS ID UNION SELECT 1) T ORDER BY ID NULLS FIRST");
    }

    [JavaOracleFact]
    public void MoreFunctions()
    {
        AssertSameAsReference("parser_sixth6",
            "SELECT BITAND(6, 3), BITOR(4, 1), BITXOR(6, 3)",
            "SELECT LSHIFT(1, 4), RSHIFT(16, 2)",
            "SELECT STRINGDECODE('abc'), STRINGENCODE('abc')",
            "SELECT HEXTORAW('00FF'), RAWTOHEX(X'00FF')",
            "SELECT DATABASE(), CURRENT_SCHEMA()",
            "SELECT REGEXP_REPLACE('abc123', '[0-9]+', 'X')",
            "SELECT CASE WHEN REGEXP_LIKE('abc', '^a') THEN 1 ELSE 0 END",
            "SELECT TRUNCATE_VALUE(123.456, 2)",
            "SELECT MOD(10, 3), SIGN(-5), ABS(-9)",
            "SELECT YEAR(DATE '2020-05-06'), MONTH(DATE '2020-05-06'), DAY_OF_MONTH(DATE '2020-05-06')");
    }
}

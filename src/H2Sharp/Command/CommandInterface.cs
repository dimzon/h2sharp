/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.CommandInterface.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The kind of a statement, and the contract every command keeps to.
 *
 * The numbers are a contract in both directions: a prepared statement stores its kind and
 * INFORMATION_SCHEMA reads it back, so a row written by the reference and read here must agree.
 * They are transcribed from the reference rather than renumbered, including the one place where
 * it repeats a value on purpose — REPLACE and NO_OPERATION are both 63, because a REPLACE that
 * affected no rows *is* a no-operation and the two answers are meant to be the same one.
 *
 * This is an interface, as in the reference, and not a class of numbers: the contract above the
 * numbers is what a caller programs against — it is what a prepared statement is, and what the
 * DDL that collects commands walks. In C# the constants of an interface cannot be read through
 * the interface's own name, so the code below reads them through Command, which is the type the
 * reference reads them through.
 *
 * It is also the answer to "where do CREATE and INSERT live": they live *here*, as kinds of
 * statements, not as keywords. The tokenizer does not know them and the parser compares their
 * text, so adding them to ParserUtil would renumber every token after them.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Command;

/// <summary>
/// The contract every command keeps to, and the kinds of statement as the reference numbers
/// them.
/// </summary>
public interface CommandInterface : IDisposable
{
    /// <summary>
    /// Get the command type.
    /// </summary>
    /// <returns>the command type</returns>
    int GetCommandType();

    /// <summary>
    /// Check if this command is a query.
    /// </summary>
    /// <returns>true if it is</returns>
    bool IsQuery();

    /// <summary>
    /// Execute a query statement, if this is possible.
    /// </summary>
    /// <param name="maxRows">the maximum number of rows returned</param>
    /// <param name="fetchSize">ignored by local commands</param>
    /// <param name="scrollable">if the result set must be scrollable (ignored)</param>
    /// <returns>the result set</returns>
    ResultInterface ExecuteQuery(long maxRows, int fetchSize, bool scrollable);

    /// <summary>
    /// Execute an updating statement, if this is possible.
    /// </summary>
    /// <param name="generatedKeysRequest">whether generated keys are needed</param>
    /// <returns>the update count and generated keys, if any</returns>
    ResultWithGeneratedKeys ExecuteUpdate(object generatedKeysRequest);

    /// <summary>
    /// Execute a batch of updates.
    /// </summary>
    /// <param name="batchParameters">the parameters of each statement</param>
    /// <param name="generatedKeysRequest">whether generated keys are needed</param>
    /// <returns>the result</returns>
    BatchResult ExecuteBatchUpdate(List<Value?[]> batchParameters, object generatedKeysRequest);

    /// <summary>
    /// Close the command and mark it as reusable.
    /// </summary>
    /// <param name="commitIfAutoCommit">whether to commit on auto commit</param>
    void Stop(bool commitIfAutoCommit);

    /// <summary>
    /// Close the command.
    /// </summary>
    void Close();

    /// <summary>
    /// Cancel the command.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Get an empty result set containing the metadata.
    /// </summary>
    /// <returns>the metadata</returns>
    ResultInterface? GetMetaData();

    /// <summary>Unknown.</summary>
    public const int Unknown = 0;

    /// <summary>ALTER INDEX RENAME.</summary>
    public const int AlterIndexRename = 1;

    /// <summary>ALTER SCHEMA RENAME.</summary>
    public const int AlterSchemaRename = 2;

    /// <summary>ALTER TABLE ADD CONSTRAINT CHECK.</summary>
    public const int AlterTableAddConstraintCheck = 3;

    /// <summary>ALTER TABLE ADD CONSTRAINT UNIQUE.</summary>
    public const int AlterTableAddConstraintUnique = 4;

    /// <summary>ALTER TABLE ADD CONSTRAINT REFERENTIAL.</summary>
    public const int AlterTableAddConstraintReferential = 5;

    /// <summary>ALTER TABLE ADD CONSTRAINT PRIMARY KEY.</summary>
    public const int AlterTableAddConstraintPrimaryKey = 6;

    /// <summary>ALTER TABLE ADD COLUMN.</summary>
    public const int AlterTableAddColumn = 7;

    /// <summary>ALTER TABLE ALTER COLUMN NOT NULL.</summary>
    public const int AlterTableAlterColumnNotNull = 8;

    /// <summary>ALTER TABLE ALTER COLUMN DROP NOT NULL.</summary>
    public const int AlterTableAlterColumnDropNotNull = 9;

    /// <summary>ALTER TABLE ALTER COLUMN DEFAULT.</summary>
    public const int AlterTableAlterColumnDefault = 10;

    /// <summary>ALTER TABLE ALTER COLUMN CHANGE TYPE.</summary>
    public const int AlterTableAlterColumnChangeType = 11;

    /// <summary>ALTER TABLE DROP COLUMN.</summary>
    public const int AlterTableDropColumn = 12;

    /// <summary>ALTER TABLE ALTER COLUMN SELECTIVITY.</summary>
    public const int AlterTableAlterColumnSelectivity = 13;

    /// <summary>ALTER TABLE DROP CONSTRAINT.</summary>
    public const int AlterTableDropConstraint = 14;

    /// <summary>ALTER TABLE RENAME.</summary>
    public const int AlterTableRename = 15;

    /// <summary>ALTER TABLE ALTER COLUMN RENAME.</summary>
    public const int AlterTableAlterColumnRename = 16;

    /// <summary>ALTER USER ADMIN.</summary>
    public const int AlterUserAdmin = 17;

    /// <summary>ALTER USER RENAME.</summary>
    public const int AlterUserRename = 18;

    /// <summary>ALTER USER SET PASSWORD.</summary>
    public const int AlterUserSetPassword = 19;

    /// <summary>ALTER VIEW.</summary>
    public const int AlterView = 20;

    /// <summary>ANALYZE.</summary>
    public const int Analyze = 21;

    /// <summary>CREATE AGGREGATE.</summary>
    public const int CreateAggregate = 22;

    /// <summary>CREATE CONSTANT.</summary>
    public const int CreateConstant = 23;

    /// <summary>CREATE ALIAS.</summary>
    public const int CreateAlias = 24;

    /// <summary>CREATE INDEX.</summary>
    public const int CreateIndex = 25;

    /// <summary>CREATE LINKED TABLE.</summary>
    public const int CreateLinkedTable = 26;

    /// <summary>CREATE ROLE.</summary>
    public const int CreateRole = 27;

    /// <summary>CREATE SCHEMA.</summary>
    public const int CreateSchema = 28;

    /// <summary>CREATE SEQUENCE.</summary>
    public const int CreateSequence = 29;

    /// <summary>CREATE TABLE.</summary>
    public const int CreateTable = 30;

    /// <summary>CREATE TRIGGER.</summary>
    public const int CreateTrigger = 31;

    /// <summary>CREATE USER.</summary>
    public const int CreateUser = 32;

    /// <summary>CREATE DOMAIN.</summary>
    public const int CreateDomain = 33;

    /// <summary>CREATE VIEW.</summary>
    public const int CreateView = 34;

    /// <summary>DEALLOCATE.</summary>
    public const int Deallocate = 35;

    /// <summary>DROP AGGREGATE.</summary>
    public const int DropAggregate = 36;

    /// <summary>DROP CONSTANT.</summary>
    public const int DropConstant = 37;

    /// <summary>DROP ALL OBJECTS.</summary>
    public const int DropAllObjects = 38;

    /// <summary>DROP ALIAS.</summary>
    public const int DropAlias = 39;

    /// <summary>DROP INDEX.</summary>
    public const int DropIndex = 40;

    /// <summary>DROP ROLE.</summary>
    public const int DropRole = 41;

    /// <summary>DROP SCHEMA.</summary>
    public const int DropSchema = 42;

    /// <summary>DROP SEQUENCE.</summary>
    public const int DropSequence = 43;

    /// <summary>DROP TABLE.</summary>
    public const int DropTable = 44;

    /// <summary>DROP TRIGGER.</summary>
    public const int DropTrigger = 45;

    /// <summary>DROP USER.</summary>
    public const int DropUser = 46;

    /// <summary>DROP DOMAIN.</summary>
    public const int DropDomain = 47;

    /// <summary>DROP VIEW.</summary>
    public const int DropView = 48;

    /// <summary>GRANT.</summary>
    public const int Grant = 49;

    /// <summary>REVOKE.</summary>
    public const int Revoke = 50;

    /// <summary>PREPARE.</summary>
    public const int Prepare = 51;

    /// <summary>COMMENT.</summary>
    public const int Comment = 52;

    /// <summary>TRUNCATE TABLE.</summary>
    public const int TruncateTable = 53;

    /// <summary>ALTER SEQUENCE.</summary>
    public const int AlterSequence = 54;

    /// <summary>ALTER TABLE SET REFERENTIAL INTEGRITY.</summary>
    public const int AlterTableSetReferentialIntegrity = 55;

    /// <summary>BACKUP.</summary>
    public const int Backup = 56;

    /// <summary>CALL.</summary>
    public const int Call = 57;

    /// <summary>DELETE.</summary>
    public const int Delete = 58;

    /// <summary>EXECUTE.</summary>
    public const int Execute = 59;

    /// <summary>EXPLAIN.</summary>
    public const int Explain = 60;

    /// <summary>INSERT.</summary>
    public const int Insert = 61;

    /// <summary>MERGE.</summary>
    public const int Merge = 62;

    /// <summary>REPLACE, and also NO_OPERATION: a REPLACE that changed no rows is one.</summary>
    public const int Replace = 63;

    /// <summary>A statement that did nothing.</summary>
    public const int NoOperation = 63;

    /// <summary>RUNSCRIPT.</summary>
    public const int RunScript = 64;

    /// <summary>SCRIPT.</summary>
    public const int Script = 65;

    /// <summary>SELECT.</summary>
    public const int Select = 66;

    /// <summary>SET.</summary>
    public const int Set = 67;

    /// <summary>UPDATE.</summary>
    public const int Update = 68;

    /// <summary>SET AUTOCOMMIT TRUE.</summary>
    public const int SetAutocommitTrue = 69;

    /// <summary>SET AUTOCOMMIT FALSE.</summary>
    public const int SetAutocommitFalse = 70;

    /// <summary>COMMIT.</summary>
    public const int Commit = 71;

    /// <summary>ROLLBACK.</summary>
    public const int Rollback = 72;

    /// <summary>CHECKPOINT.</summary>
    public const int Checkpoint = 73;

    /// <summary>SAVEPOINT.</summary>
    public const int Savepoint = 74;

    /// <summary>ROLLBACK TO SAVEPOINT.</summary>
    public const int RollbackToSavepoint = 75;

    /// <summary>CHECKPOINT SYNC.</summary>
    public const int CheckpointSync = 76;

    /// <summary>PREPARE COMMIT.</summary>
    public const int PrepareCommit = 77;

    /// <summary>COMMIT TRANSACTION.</summary>
    public const int CommitTransaction = 78;

    /// <summary>ROLLBACK TRANSACTION.</summary>
    public const int RollbackTransaction = 79;

    /// <summary>SHUTDOWN.</summary>
    public const int Shutdown = 80;

    /// <summary>SHUTDOWN IMMEDIATELY.</summary>
    public const int ShutdownImmediately = 81;

    /// <summary>SHUTDOWN COMPACT.</summary>
    public const int ShutdownCompact = 82;

    /// <summary>BEGIN.</summary>
    public const int Begin = 83;

    /// <summary>SHUTDOWN DEFRAG.</summary>
    public const int ShutdownDefrag = 84;

    /// <summary>ALTER TABLE RENAME CONSTRAINT.</summary>
    public const int AlterTableRenameConstraint = 85;

    /// <summary>EXPLAIN ANALYZE.</summary>
    public const int ExplainAnalyze = 86;

    /// <summary>ALTER TABLE ALTER COLUMN VISIBILITY.</summary>
    public const int AlterTableAlterColumnVisibility = 87;

    /// <summary>CREATE SYNONYM.</summary>
    public const int CreateSynonym = 88;

    /// <summary>DROP SYNONYM.</summary>
    public const int DropSynonym = 89;

    /// <summary>ALTER TABLE ALTER COLUMN ON UPDATE.</summary>
    public const int AlterTableAlterColumnOnUpdate = 90;

    /// <summary>EXECUTE IMMEDIATELY.</summary>
    public const int ExecuteImmediately = 91;

    /// <summary>ALTER DOMAIN ADD CONSTRAINT.</summary>
    public const int AlterDomainAddConstraint = 92;

    /// <summary>ALTER DOMAIN DROP CONSTRAINT.</summary>
    public const int AlterDomainDropConstraint = 93;

    /// <summary>ALTER DOMAIN DEFAULT.</summary>
    public const int AlterDomainDefault = 94;

    /// <summary>ALTER DOMAIN ON UPDATE.</summary>
    public const int AlterDomainOnUpdate = 95;

    /// <summary>ALTER DOMAIN RENAME.</summary>
    public const int AlterDomainRename = 96;

    /// <summary>HELP.</summary>
    public const int Help = 97;

    /// <summary>ALTER TABLE ALTER COLUMN DROP EXPRESSION.</summary>
    public const int AlterTableAlterColumnDropExpression = 98;

    /// <summary>ALTER TABLE ALTER COLUMN DROP IDENTITY.</summary>
    public const int AlterTableAlterColumnDropIdentity = 99;

    /// <summary>ALTER TABLE ALTER COLUMN DEFAULT ON NULL.</summary>
    public const int AlterTableAlterColumnDefaultOnNull = 100;

    /// <summary>ALTER DOMAIN RENAME CONSTRAINT.</summary>
    public const int AlterDomainRenameConstraint = 101;

    /// <summary>CREATE MATERIALIZED VIEW.</summary>
    public const int CreateMaterializedView = 102;

    /// <summary>REFRESH MATERIALIZED VIEW.</summary>
    public const int RefreshMaterializedView = 103;

    /// <summary>DROP MATERIALIZED VIEW.</summary>
    public const int DropMaterializedView = 104;

    /// <summary>ALTER TYPE.</summary>
    public const int AlterType = 105;
}

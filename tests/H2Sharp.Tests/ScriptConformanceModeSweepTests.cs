/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 262: adapted reference scripts also run in every compatibility mode in which their
 * remaining expectations match the reference. The mode is chosen before the database opens,
 * as D-029 requires; only statements the port cannot execute are omitted.
 */
using System.Collections.Generic;
using Xunit;

namespace H2Sharp.Tests;

[Collection(ScriptCatalogCollection.Name)]
public sealed class ScriptConformanceModeSweepTests
{
    public static TheoryData<string, string> Scripts()
    {
        TheoryData<string, string> data = new();
        data.Add("datatypes/varchar-ignorecase.sql", "DB2");
        data.Add("datatypes/varchar-ignorecase.sql", "LEGACY");
        data.Add("datatypes/varchar-ignorecase.sql", "HSQLDB");
        data.Add("ddl/createAlias.sql", "ORACLE");
        data.Add("ddl/createAlias.sql", "MSSQLSERVER");
        data.Add("ddl/createAlias.sql", "MYSQL");
        data.Add("ddl/createAlias.sql", "MARIADB");
        data.Add("ddl/createAlias.sql", "POSTGRESQL");
        data.Add("ddl/createAlias.sql", "DB2");
        data.Add("ddl/createAlias.sql", "DERBY");
        data.Add("ddl/createAlias.sql", "LEGACY");
        data.Add("ddl/createAlias.sql", "HSQLDB");
        data.Add("ddl/createDomain.sql", "ORACLE");
        data.Add("ddl/createDomain.sql", "MSSQLSERVER");
        data.Add("ddl/createDomain.sql", "MYSQL");
        data.Add("ddl/createDomain.sql", "MARIADB");
        data.Add("ddl/createDomain.sql", "POSTGRESQL");
        data.Add("ddl/createDomain.sql", "DB2");
        data.Add("ddl/createDomain.sql", "DERBY");
        data.Add("ddl/createDomain.sql", "LEGACY");
        data.Add("ddl/createDomain.sql", "HSQLDB");
        data.Add("ddl/createView.sql", "ORACLE");
        data.Add("ddl/createView.sql", "MSSQLSERVER");
        data.Add("ddl/createView.sql", "MYSQL");
        data.Add("ddl/createView.sql", "MARIADB");
        data.Add("ddl/createView.sql", "POSTGRESQL");
        data.Add("ddl/createView.sql", "DB2");
        data.Add("ddl/createView.sql", "DERBY");
        data.Add("ddl/createView.sql", "LEGACY");
        data.Add("ddl/createView.sql", "HSQLDB");
        data.Add("ddl/dropAllObjects.sql", "ORACLE");
        data.Add("ddl/dropAllObjects.sql", "MSSQLSERVER");
        data.Add("ddl/dropAllObjects.sql", "MYSQL");
        data.Add("ddl/dropAllObjects.sql", "MARIADB");
        data.Add("ddl/dropAllObjects.sql", "DB2");
        data.Add("ddl/dropAllObjects.sql", "DERBY");
        data.Add("ddl/dropAllObjects.sql", "LEGACY");
        data.Add("ddl/dropAllObjects.sql", "HSQLDB");
        data.Add("ddl/dropSchema.sql", "MYSQL");
        data.Add("ddl/dropSchema.sql", "MARIADB");
        data.Add("ddl/dropSchema.sql", "LEGACY");
        data.Add("ddl/dropSchema.sql", "HSQLDB");
        data.Add("dml/script.sql", "ORACLE");
        data.Add("dml/script.sql", "MSSQLSERVER");
        data.Add("dml/script.sql", "MYSQL");
        data.Add("dml/script.sql", "MARIADB");
        data.Add("dml/script.sql", "DB2");
        data.Add("dml/script.sql", "DERBY");
        data.Add("dml/script.sql", "LEGACY");
        data.Add("dml/script.sql", "HSQLDB");
        data.Add("other/help.sql", "ORACLE");
        data.Add("other/help.sql", "MSSQLSERVER");
        data.Add("other/help.sql", "MYSQL");
        data.Add("other/help.sql", "MARIADB");
        data.Add("other/help.sql", "POSTGRESQL");
        data.Add("other/help.sql", "DB2");
        data.Add("other/help.sql", "DERBY");
        data.Add("other/help.sql", "LEGACY");
        data.Add("other/help.sql", "HSQLDB");
        data.Add("other/set.sql", "MSSQLSERVER");
        data.Add("other/set.sql", "MYSQL");
        data.Add("other/set.sql", "MARIADB");
        data.Add("other/set.sql", "POSTGRESQL");
        data.Add("other/set.sql", "DB2");
        data.Add("other/set.sql", "DERBY");
        data.Add("other/set.sql", "LEGACY");
        data.Add("other/set.sql", "HSQLDB");
        data.Add("other/two_phase_commit.sql", "ORACLE");
        data.Add("other/two_phase_commit.sql", "MSSQLSERVER");
        data.Add("other/two_phase_commit.sql", "MYSQL");
        data.Add("other/two_phase_commit.sql", "MARIADB");
        data.Add("other/two_phase_commit.sql", "DB2");
        data.Add("other/two_phase_commit.sql", "DERBY");
        data.Add("other/two_phase_commit.sql", "LEGACY");
        data.Add("other/two_phase_commit.sql", "HSQLDB");
        data.Add("functions/numeric/cosh.sql", "ORACLE");
        data.Add("functions/numeric/cosh.sql", "MSSQLSERVER");
        data.Add("functions/numeric/cosh.sql", "MYSQL");
        data.Add("functions/numeric/cosh.sql", "MARIADB");
        data.Add("functions/numeric/cosh.sql", "POSTGRESQL");
        data.Add("functions/numeric/cosh.sql", "DB2");
        data.Add("functions/numeric/cosh.sql", "DERBY");
        data.Add("functions/numeric/cosh.sql", "LEGACY");
        data.Add("functions/numeric/cosh.sql", "HSQLDB");
        data.Add("functions/numeric/hash.sql", "ORACLE");
        data.Add("functions/numeric/hash.sql", "MSSQLSERVER");
        data.Add("functions/numeric/hash.sql", "MYSQL");
        data.Add("functions/numeric/hash.sql", "MARIADB");
        data.Add("functions/numeric/hash.sql", "POSTGRESQL");
        data.Add("functions/numeric/hash.sql", "DB2");
        data.Add("functions/numeric/hash.sql", "DERBY");
        data.Add("functions/numeric/hash.sql", "LEGACY");
        data.Add("functions/numeric/hash.sql", "HSQLDB");
        data.Add("functions/string/regexp-substr.sql", "ORACLE");
        data.Add("functions/string/regexp-substr.sql", "MSSQLSERVER");
        data.Add("functions/string/regexp-substr.sql", "MYSQL");
        data.Add("functions/string/regexp-substr.sql", "MARIADB");
        data.Add("functions/string/regexp-substr.sql", "POSTGRESQL");
        data.Add("functions/string/regexp-substr.sql", "DB2");
        data.Add("functions/string/regexp-substr.sql", "DERBY");
        data.Add("functions/string/regexp-substr.sql", "LEGACY");
        data.Add("functions/string/regexp-substr.sql", "HSQLDB");
        data.Add("functions/string/to-char.sql", "ORACLE");
        data.Add("functions/string/to-char.sql", "MSSQLSERVER");
        data.Add("functions/string/to-char.sql", "MYSQL");
        data.Add("functions/string/to-char.sql", "MARIADB");
        data.Add("functions/string/to-char.sql", "POSTGRESQL");
        data.Add("functions/string/to-char.sql", "DB2");
        data.Add("functions/string/to-char.sql", "DERBY");
        data.Add("functions/string/to-char.sql", "LEGACY");
        data.Add("functions/string/to-char.sql", "HSQLDB");
        data.Add("functions/string/xmlcdata.sql", "ORACLE");
        data.Add("functions/string/xmlcdata.sql", "MSSQLSERVER");
        data.Add("functions/string/xmlcdata.sql", "MYSQL");
        data.Add("functions/string/xmlcdata.sql", "MARIADB");
        data.Add("functions/string/xmlcdata.sql", "POSTGRESQL");
        data.Add("functions/string/xmlcdata.sql", "DB2");
        data.Add("functions/string/xmlcdata.sql", "DERBY");
        data.Add("functions/string/xmlcdata.sql", "LEGACY");
        data.Add("functions/string/xmlcdata.sql", "HSQLDB");
        data.Add("functions/string/xmlcomment.sql", "ORACLE");
        data.Add("functions/string/xmlcomment.sql", "MSSQLSERVER");
        data.Add("functions/string/xmlcomment.sql", "MYSQL");
        data.Add("functions/string/xmlcomment.sql", "MARIADB");
        data.Add("functions/string/xmlcomment.sql", "POSTGRESQL");
        data.Add("functions/string/xmlcomment.sql", "DB2");
        data.Add("functions/string/xmlcomment.sql", "DERBY");
        data.Add("functions/string/xmlcomment.sql", "LEGACY");
        data.Add("functions/string/xmlcomment.sql", "HSQLDB");
        data.Add("functions/string/xmlnode.sql", "ORACLE");
        data.Add("functions/string/xmlnode.sql", "MSSQLSERVER");
        data.Add("functions/string/xmlnode.sql", "MYSQL");
        data.Add("functions/string/xmlnode.sql", "MARIADB");
        data.Add("functions/string/xmlnode.sql", "POSTGRESQL");
        data.Add("functions/string/xmlnode.sql", "DB2");
        data.Add("functions/string/xmlnode.sql", "DERBY");
        data.Add("functions/string/xmlnode.sql", "LEGACY");
        data.Add("functions/string/xmlnode.sql", "HSQLDB");
        data.Add("functions/string/xmltext.sql", "ORACLE");
        data.Add("functions/string/xmltext.sql", "MSSQLSERVER");
        data.Add("functions/string/xmltext.sql", "MYSQL");
        data.Add("functions/string/xmltext.sql", "MARIADB");
        data.Add("functions/string/xmltext.sql", "POSTGRESQL");
        data.Add("functions/string/xmltext.sql", "DB2");
        data.Add("functions/string/xmltext.sql", "DERBY");
        data.Add("functions/string/xmltext.sql", "LEGACY");
        data.Add("functions/string/xmltext.sql", "HSQLDB");
        data.Add("functions/system/current_user.sql", "ORACLE");
        data.Add("functions/system/current_user.sql", "MSSQLSERVER");
        data.Add("functions/system/current_user.sql", "MYSQL");
        data.Add("functions/system/current_user.sql", "MARIADB");
        data.Add("functions/system/current_user.sql", "POSTGRESQL");
        data.Add("functions/system/current_user.sql", "DB2");
        data.Add("functions/system/current_user.sql", "DERBY");
        data.Add("functions/system/current_user.sql", "LEGACY");
        data.Add("functions/system/current_user.sql", "HSQLDB");
        data.Add("functions/system/data_type_sql.sql", "ORACLE");
        data.Add("functions/system/data_type_sql.sql", "MSSQLSERVER");
        data.Add("functions/system/data_type_sql.sql", "MYSQL");
        data.Add("functions/system/data_type_sql.sql", "MARIADB");
        data.Add("functions/system/data_type_sql.sql", "POSTGRESQL");
        data.Add("functions/system/data_type_sql.sql", "DB2");
        data.Add("functions/system/data_type_sql.sql", "DERBY");
        data.Add("functions/system/data_type_sql.sql", "LEGACY");
        data.Add("functions/system/data_type_sql.sql", "HSQLDB");
        data.Add("functions/timeanddate/formatdatetime.sql", "ORACLE");
        data.Add("functions/timeanddate/formatdatetime.sql", "MSSQLSERVER");
        data.Add("functions/timeanddate/formatdatetime.sql", "MYSQL");
        data.Add("functions/timeanddate/formatdatetime.sql", "MARIADB");
        data.Add("functions/timeanddate/formatdatetime.sql", "POSTGRESQL");
        data.Add("functions/timeanddate/formatdatetime.sql", "DB2");
        data.Add("functions/timeanddate/formatdatetime.sql", "DERBY");
        data.Add("functions/timeanddate/formatdatetime.sql", "LEGACY");
        data.Add("functions/timeanddate/formatdatetime.sql", "HSQLDB");
        data.Add("functions/timeanddate/parsedatetime.sql", "ORACLE");
        data.Add("functions/timeanddate/parsedatetime.sql", "MSSQLSERVER");
        data.Add("functions/timeanddate/parsedatetime.sql", "MYSQL");
        data.Add("functions/timeanddate/parsedatetime.sql", "MARIADB");
        data.Add("functions/timeanddate/parsedatetime.sql", "POSTGRESQL");
        data.Add("functions/timeanddate/parsedatetime.sql", "DB2");
        data.Add("functions/timeanddate/parsedatetime.sql", "DERBY");
        data.Add("functions/timeanddate/parsedatetime.sql", "LEGACY");
        data.Add("functions/timeanddate/parsedatetime.sql", "HSQLDB");
        return data;
    }

    [ReferenceScriptsTheory]
    [MemberData(nameof(Scripts))]
    public void AdaptedScriptMatchesInMode(string relativePath, string mode)
    {
        List<string> failures = ScriptRunner.RunAdaptedInMode(
            relativePath, mode, ScriptConformanceAdaptedTests.Skips[relativePath]);
        Assert.True(failures.Count == 0, mode + ": " + string.Join(" | ", failures));
    }
}

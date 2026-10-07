/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ConstantsTests
{
    [Fact]
    public void Version_IsComposedLikeJava()
    {
        // org.h2.engine.Constants: VERSION_MAJOR + "." + VERSION_MINOR + '.' + BUILD_ID
        // plus "-SNAPSHOT" when BUILD_SNAPSHOT, and FULL_VERSION appends " (BUILD_DATE)".
        string expected = Constants.VersionMajor + "." + Constants.VersionMinor + "."
            + Constants.BuildId;
        if (Constants.BuildVendorAndVersion is not null)
        {
            expected += '_' + Constants.BuildVendorAndVersion;
        }

        if (Constants.BuildSnapshot)
        {
            expected += "-SNAPSHOT";
        }

        Assert.Equal(expected, Constants.Version);
        Assert.Equal(expected + " (" + Constants.BuildDate + ')', Constants.FullVersion);
    }

    [Fact]
    public void FileSuffixes_MatchTheDocumentedOnDiskLayout()
    {
        // These strings end up in real file names and in INFORMATION_SCHEMA, so they
        // are part of H2Sharp's observable behaviour.
        Assert.Equal(".mv.db", Constants.SuffixMvFile);
        Assert.Equal(".lock.db", Constants.SuffixLockFile);
        Assert.Equal(".trace.db", Constants.SuffixTraceFile);
        Assert.Equal(".temp.db", Constants.SuffixTempFile);
        Assert.Equal(".newFile", Constants.SuffixMvStoreNewFile);
        Assert.Equal(".tempFile", Constants.SuffixMvStoreTempFile);
    }

    [Fact]
    public void DerivedConstants_UseTheSameValuesAsJava()
    {
        Assert.Equal(Constants.LockModeReadCommitted, Constants.DefaultLockMode);
        Assert.Equal(Constants.TcpProtocolVersion17, Constants.TcpProtocolVersionMinSupported);
        Assert.Equal(Constants.TcpProtocolVersion21, Constants.TcpProtocolVersionMaxSupported);
        Assert.Equal(16 * 1024, Constants.DefaultPageSize);
        Assert.Equal(4 * 1024, Constants.IoBufferSize);
        Assert.Equal(128 * 1024, Constants.IoBufferSizeCompress);
    }

    [Fact]
    public void SchemaIdentityConstants_MatchJava()
    {
        Assert.Equal(-1, Constants.InformationSchemaId);
        Assert.Equal(0, Constants.MainSchemaId);
        Assert.Equal(-1_000, Constants.PgCatalogSchemaId);
        Assert.Equal("PUBLIC", Constants.SchemaMain);
        Assert.Equal("PG_CATALOG", Constants.SchemaPgCatalog);
    }

    [JavaOracleFact]
    public void VersionConstants_MatchReferenceJvm()
    {
        string javaOutput = RunConstantsOnReferenceJvm();
        string actual = Constants.VersionMajor + " " + Constants.VersionMinor + " "
            + Constants.BuildId + " " + Constants.BuildDate + " " + Constants.Version
            + " " + Constants.FullVersion;

        Assert.Equal(JavaOracle.Normalize(javaOutput), JavaOracle.Normalize(actual));
    }

    private static string RunConstantsOnReferenceJvm()
    {
        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        string source = "public class Oracle {\n"
            + "  public static void main(String[] a) throws Exception {\n"
            + "    Class<?> c = org.h2.engine.Constants.class;\n"
            + "    System.out.println(c.getField(\"VERSION_MAJOR\").getInt(null)\n"
            + "      + \" \" + c.getField(\"VERSION_MINOR\").getInt(null)\n"
            + "      + \" \" + c.getField(\"BUILD_ID\").getInt(null));\n"
            + "    System.out.println(c.getField(\"BUILD_DATE\").get(null));\n"
            + "    System.out.println(c.getField(\"VERSION\").get(null));\n"
            + "    System.out.println(c.getField(\"FULL_VERSION\").get(null));\n"
            + "  }\n"
            + "}\n";

        string output = JavaOracle.Run(source, "Oracle", JavaOracle.H2Jar);
        string[] lines = JavaOracle.Normalize(output).Split('\n');
        return string.Join(' ',
            lines[0].Trim(), lines[1].Trim(), lines[2].Trim(), lines[3].Trim());
    }
}
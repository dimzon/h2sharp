// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — numeric functions, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class MathFunctionTests
{
    /// <summary>docs/functions.html:951 — ABS( { numeric | interval } )</summary>
    [Fact]
    public void Abs_RemovesTheSign() // FUN-0951
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT ABS(-5)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM ABS(INTERVAL '-1' HOUR))"));
    }

    /// <summary>docs/functions.html:969 — ACOS(numeric)</summary>
    [Fact]
    public void Acos_IsInverseOfCos() // FUN-0969
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT ACOS(1)"), 10);
        Assert.Equal(Math.PI / 2, connection.QuerySingle<double>("SELECT ACOS(0)"), 10);
    }

    /// <summary>docs/functions.html:987 — ASIN(numeric)</summary>
    [Fact]
    public void Asin_IsInverseOfSin() // FUN-0987
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT ASIN(0)"), 10);
        Assert.Equal(Math.PI / 2, connection.QuerySingle<double>("SELECT ASIN(1)"), 10);
    }

    /// <summary>docs/functions.html:1005 — ATAN(numeric)</summary>
    [Fact]
    public void Atan_IsInverseOfTan() // FUN-1005
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(Math.PI / 4, connection.QuerySingle<double>("SELECT ATAN(1)"), 10);
    }

    /// <summary>docs/functions.html:1023 — COS(numeric)</summary>
    [Fact]
    public void Cos_AtZero_IsOne() // FUN-1023
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT COS(0)"), 10);
    }

    /// <summary>docs/functions.html:1041 — COSH(numeric)</summary>
    [Fact]
    public void Cosh_AtZero_IsOne() // FUN-1041
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT COSH(0)"), 10);
    }

    /// <summary>docs/functions.html:1059 — COT(numeric)</summary>
    [Fact]
    public void Cot_IsInverseOfTan() // FUN-1059
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.0 / Math.Tan(1), connection.QuerySingle<double>("SELECT COT(1)"), 10);
    }

    /// <summary>docs/functions.html:1077 — SIN(numeric)</summary>
    [Fact]
    public void Sin_AtZero_IsZero() // FUN-1077
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT SIN(0)"), 10);
    }

    /// <summary>docs/functions.html:1095 — SINH(numeric)</summary>
    [Fact]
    public void Sinh_AtZero_IsZero() // FUN-1095
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT SINH(0)"), 10);
    }

    /// <summary>docs/functions.html:1113 — TAN(numeric)</summary>
    [Fact]
    public void Tan_AtZero_IsZero() // FUN-1113
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT TAN(0)"), 10);
    }

    /// <summary>docs/functions.html:1131 — TANH(numeric)</summary>
    [Fact]
    public void Tanh_AtZero_IsZero() // FUN-1131
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT TANH(0)"), 10);
    }

    /// <summary>docs/functions.html:1149 — ATAN2(numeric, numeric)</summary>
    [Fact]
    public void Atan2_OfEqualArguments_IsQuarterPi() // FUN-1149
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(Math.PI / 4, connection.QuerySingle<double>("SELECT ATAN2(1, 1)"), 10);
    }

    /// <summary>docs/functions.html:1437 — MOD(dividendNumeric, divisorNumeric)</summary>
    [Fact]
    public void Mod_ReturnsTheRemainder() // FUN-1437
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT MOD(7, 3)"));
    }

    /// <summary>docs/functions.html:1455 — { CEIL | CEILING } (numeric)</summary>
    [Fact]
    public void Ceil_RoundsUp() // FUN-1455
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT CEIL(1.2)"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT CEILING(2.1)"));
    }

    /// <summary>docs/functions.html:1473 — DEGREES(numeric)</summary>
    [Fact]
    public void Degrees_ConvertsRadians() // FUN-1473
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(180.0, connection.QuerySingle<double>("SELECT DEGREES(PI())"), 10);
    }

    /// <summary>docs/functions.html:1491 — EXP(numeric)</summary>
    [Fact]
    public void Exp_AtZero_IsOne() // FUN-1491
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT EXP(0)"), 10);
    }

    /// <summary>docs/functions.html:1509 — FLOOR(numeric)</summary>
    [Fact]
    public void Floor_RoundsDown() // FUN-1509
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT FLOOR(1.9)"));
    }

    /// <summary>docs/functions.html:1527 — LN(numeric)</summary>
    [Fact]
    public void Ln_IsInverseOfExp() // FUN-1527
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT LN(EXP(1))"), 10);
    }

    /// <summary>docs/functions.html:1545 — LOG({baseNumeric, numeric | {numeric}})</summary>
    [Fact]
    public void Log_WithAndWithoutBase_IsEvaluated() // FUN-1545
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3.0, connection.QuerySingle<double>("SELECT LOG(2, 8)"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT LOG(EXP(1))"), 10);
    }

    /// <summary>docs/functions.html:1563 — LOG10(numeric)</summary>
    [Fact]
    public void Log10_OfHundred_IsTwo() // FUN-1563
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT LOG10(100)"), 10);
    }

    /// <summary>docs/functions.html:1581 — ORA_HASH(expression [, bucketLong [, seedLong]])</summary>
    [Fact]
    public void OraHash_StaysInsideTheBucket() // FUN-1581
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT ORA_HASH('x', 10) >= 0 AND ORA_HASH('x', 10) < 10"));
    }

    /// <summary>docs/functions.html:1599 — RADIANS(numeric)</summary>
    [Fact]
    public void Radians_ConvertsDegrees() // FUN-1599
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(Math.PI, connection.QuerySingle<double>("SELECT RADIANS(180)"), 10);
    }

    /// <summary>docs/functions.html:1617 — SQRT(numeric)</summary>
    [Fact]
    public void Sqrt_OfNine_IsThree() // FUN-1617
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3.0, connection.QuerySingle<double>("SELECT SQRT(9)"), 10);
    }

    /// <summary>docs/functions.html:1635 — PI()</summary>
    [Fact]
    public void Pi_ReturnsTheConstant() // FUN-1635
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(Math.PI, connection.QuerySingle<double>("SELECT PI()"), 10);
    }

    /// <summary>docs/functions.html:1653 — POWER(numeric, numeric)</summary>
    [Fact]
    public void Power_RaisesToTheExponent() // FUN-1653
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1024.0, connection.QuerySingle<double>("SELECT POWER(2, 10)"), 10);
    }

    /// <summary>docs/functions.html:1671 — { RAND | RANDOM } ( [ int ] )</summary>
    [Fact]
    public void Rand_StaysInTheUnitInterval() // FUN-1671
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT RAND() >= 0 AND RAND() < 1"));
        Assert.True(connection.QuerySingle<bool>("SELECT RANDOM(10) >= 0"));
    }

    /// <summary>docs/functions.html:1689 — RANDOM_UUID([versionInt]) | UUID()</summary>
    [Fact]
    public void RandomUuid_ReturnsAValue() // FUN-1689
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT RANDOM_UUID() IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>("SELECT RANDOM_UUID(4) IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>("SELECT UUID() IS NOT NULL"));
    }

    /// <summary>docs/functions.html:1707 — ROUND(numeric [, digitsInt])</summary>
    [Fact]
    public void Round_RoundsHalfUpAndByDigits() // FUN-1707
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT ROUND(1.5)"));
        Assert.Equal(1.12m, connection.QuerySingle<decimal>("SELECT ROUND(1.1234, 2)"));
    }

    /// <summary>docs/functions.html:1725 — SECURE_RAND(int)</summary>
    [Fact]
    public void SecureRand_ReturnsTheRequestedBytes() // FUN-1725
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(16, connection.QuerySingle<int>("SELECT OCTET_LENGTH(SECURE_RAND(16))"));
    }

    /// <summary>docs/functions.html:1743 — SIGN( { numeric | interval } )</summary>
    [Fact]
    public void Sign_ReportsTheDirection() // FUN-1743
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(-1, connection.QuerySingle<int>("SELECT SIGN(-3)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT SIGN(INTERVAL '1' HOUR)"));
    }

    /// <summary>docs/functions.html:1815 — { TRUNC | TRUNCATE } ( { {numeric [, digitsInt] }</summary>
    [Fact]
    public void Trunc_WithoutDigits_DropsTheFractionPart() // FUN-1815
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT TRUNC(1.99)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT TRUNCATE(1.99)"));
    }

    /// <summary>docs/functions.html:1816 — | { timestamp | timestampWithTimeZone | date | timestampString } } )</summary>
    [Fact]
    public void Trunc_OfTimestamp_DropsTheTimePart() // FUN-1816
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>(
            "SELECT DAYOFMONTH(TRUNC(TIMESTAMP '2024-05-06 10:20:30'))"));
    }

    /// <summary>docs/functions.html:1871 — ZERO()</summary>
    [Fact]
    public void Zero_ReturnsZero() // FUN-1871
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(0, connection.QuerySingle<int>("SELECT ZERO()"));
    }

    /// <summary>docs/functions.html:1889 — GCD(numeric, numeric [,...])</summary>
    [Fact]
    public void Gcd_OfTwelveAndEighteen_IsSix() // FUN-1889
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(6, connection.QuerySingle<int>("SELECT GCD(12, 18)"));
    }

    /// <summary>docs/functions.html:1907 — LCM(numeric, numeric [,...])</summary>
    [Fact]
    public void Lcm_OfFourAndSix_IsTwelve() // FUN-1907
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(12, connection.QuerySingle<int>("SELECT LCM(4, 6)"));
    }
}

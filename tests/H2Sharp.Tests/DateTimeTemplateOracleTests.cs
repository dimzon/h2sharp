using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The date and time template engine against the reference: the same template writing values out and
/// reading them back.
/// </summary>
/// <remarks>
/// Templates are strict. A letter is read one at a time and anything else is refused, and a template
/// that does not make sense as a whole — a second year, a day of the year next to a month and a day,
/// a time zone second without a time zone minute — is refused when it is parsed rather than when it is
/// used. Both are answers, and they are the answers that keep a template from parsing a different
/// format than the one that was declared.
/// <para>
/// The round trip is the point worth checking: a template that formats correctly but parses back to a
/// different value is worse than one that does not work at all, because it looks right.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class DateTimeTemplateOracleTests
{
    private static readonly string[] Templates =
    [
        "YYYY-MM-DD", "YY/M/D", "YYYY-MM-DD HH:MI:SS", "HH12:MI:SS A.M.",
        "DDD", "YYYY-DDD", "HH24:MI", "YYYY-MM-DDTHH:MI:SS", "HH:MI:SS",
        "YYYY-MM-DD HH24:MI:SS.SSSSS", "F1", "F9", "YYYYMMDD",
        "R RR", "RRRR", "YYYY-MM-DD HH12:MI:SS A.M.",
        "TZH:TZM", "TZS", "HH24:MI:TZM", "YYYY-MM-DD'T'HH:MI:SS",
    ];

    private static readonly string[] BadTemplates =
    [
        "", "Y", "YYYY-YYYY", "DDD-MM-DD", "HH12:MI:SS A.M. P.M.",
        "HH24:HH12", "SSSSS:HH12:MI:SS", "TZS:TZH", "TZM:TZS", "Q", "YYYY-MM-DD Q",
        "F0", "YYYY-MM-DD HH24:MI:SS.TZS",
    ];

    private static readonly string[] Samples =
    [
        "2024-02-29", "1999-12-31", "0001-01-01", "9999-12-31", "1970-01-01",
    ];

    private static readonly string[] TimeSamples =
    [
        "00:00:00", "12:00:00", "23:59:59", "09:30:15", "12:34:56",
    ];

    /// <summary>
    /// A provider with a fixed "now", so that the parts a template leaves out are the same on both
    /// sides.
    /// </summary>
    private sealed class FixedProvider(ValueTimestampTimeZone now)
        : CastDataProvider
    {
        public ValueTimestampTimeZone CurrentTimestamp() => now;

        public TimeZoneProvider CurrentTimeZone() => TimeZoneProvider.Utc;

        public bool ZeroBasedEnums() => true;

        public Mode GetMode() => Mode.GetRegular();
    }

    [JavaOracleFact]
    public void TemplatesAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"])
            .ToList();

        List<string> csharp = [.. Lines()];

        if (java.Count != csharp.Count)
        {
            int common = Math.Min(java.Count, csharp.Count);
            int first = -1;
            for (int i = 0; i < common; i++)
            {
                if (java[i] != csharp[i])
                {
                    first = i;
                    break;
                }
            }

            Assert.Fail(
                "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
                + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
                + "\n  первое различие: " + first.ToString(CultureInfo.InvariantCulture)
                + "\n    java:   " + (first >= 0 ? java[first] : "-")
                + "\n    csharp: " + (first >= 0 ? csharp[first] : "-"));
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    private static string Program() =>
        """
        {
        class P implements org.h2.engine.CastDataProvider {
          public org.h2.value.ValueTimestampTimeZone currentTimestamp() {
            return org.h2.value.ValueTimestampTimeZone.parse(
                "2024-06-15 12:00:00+00",
                new org.h2.engine.CastDataProvider() {
                  public org.h2.value.ValueTimestampTimeZone currentTimestamp() {
                    return null;
                  }

                  public org.h2.util.TimeZoneProvider currentTimeZone() {
                    return org.h2.util.TimeZoneProvider.ofId("UTC");
                  }

                  public boolean zeroBasedEnums() {
                    return true;
                  }

                  public org.h2.api.JavaObjectSerializer getJavaObjectSerializer() {
                    return null;
                  }

                  public org.h2.engine.Mode getMode() {
                    return org.h2.engine.Mode.getRegular();
                  }
                });
          }

          public boolean zeroBasedEnums() {
            return true;
          }

          public org.h2.api.JavaObjectSerializer getJavaObjectSerializer() {
            return null;
          }

          public org.h2.engine.Mode getMode() {
            return org.h2.engine.Mode.getRegular();
          }

          public org.h2.util.TimeZoneProvider currentTimeZone() {
            return org.h2.util.TimeZoneProvider.ofId("UTC");
          }
        }

        String[] dates = { "2024-02-29", "1999-12-31", "0001-01-01", "9999-12-31", "1970-01-01" };
        String[] times = { "00:00:00", "12:00:00", "23:59:59", "09:30:15", "12:34:56" };
        String[] templates = { "YYYY-MM-DD", "YY/M/D", "YYYY-MM-DD HH:MI:SS", "HH12:MI:SS A.M.",
            "DDD", "YYYY-DDD", "HH24:MI", "YYYY-MM-DDTHH:MI:SS", "HH:MI:SS",
            "YYYY-MM-DD HH24:MI:SS.SSSSS", "F1", "F9", "YYYYMMDD",
            "R RR", "RRRR", "YYYY-MM-DD HH12:MI:SS A.M.",
            "TZH:TZM", "TZS", "HH24:MI:TZM", "YYYY-MM-DD'T'HH:MI:SS" };

        String[] bad = { "", "Y", "YYYY-YYYY", "DDD-MM-DD", "HH12:MI:SS A.M. P.M.",
            "HH24:HH12", "SSSSS:HH12:MI:SS", "TZS:TZH", "TZM:TZS", "Q", "YYYY-MM-DD Q",
            "F0", "YYYY-MM-DD HH24:MI:SS.TZS" };

        for (int i = 0; i < templates.length; i++) {
          String name = templates[i];
          org.h2.util.DateTimeTemplate t;
          try {
            t = org.h2.util.DateTimeTemplate.of(name);
          } catch (RuntimeException e) {
            System.out.println("parse|" + name + "|REFUSED");
            continue;
          }
          System.out.println("parse|" + name + "|ok");
          for (int j = 0; j < dates.length; j++) {
            try {
              System.out.println("fmt|" + name + "|" + dates[j] + "|"
                  + t.format(org.h2.value.ValueDate.fromDateValue(
                      org.h2.util.DateTimeUtils.parseDateValue(dates[j], 0, dates[j].length()))));
            } catch (RuntimeException e) {
              System.out.println("fmt|" + name + "|" + dates[j] + "|REFUSED");
            }
          }
          for (int j = 0; j < times.length; j++) {
            try {
              System.out.println("fmtt|" + name + "|" + times[j] + "|"
                  + t.format(org.h2.value.ValueTime.fromNanos(
                      org.h2.util.DateTimeUtils.parseTimeNanos(times[j], 0, times[j].length()))));
            } catch (RuntimeException e) {
              System.out.println("fmtt|" + name + "|" + times[j] + "|REFUSED");
            }
          }
        }

        for (int i = 0; i < bad.length; i++) {
          try {
            org.h2.util.DateTimeTemplate.of(bad[i]);
            System.out.println("bad|" + bad[i] + "|ok");
          } catch (RuntimeException e) {
            System.out.println("bad|" + bad[i] + "|REFUSED");
          }
        }

        org.h2.util.DateTimeTemplate round = org.h2.util.DateTimeTemplate.of("YYYY-MM-DD HH24:MI:SS");
        for (int j = 0; j < dates.length; j++) {
          try {
            org.h2.value.Value v = round.parse(dates[j] + " 13:45:59",
                org.h2.value.TypeInfo.TYPE_TIMESTAMP, new P());
            System.out.println("round|" + dates[j] + "|" + v.getString());
          } catch (RuntimeException e) {
            System.out.println("round|" + dates[j] + "|REFUSED");
          }
        }
        }
        """;

    private static IEnumerable<string> Lines()
    {
        FixedProvider provider = new(
            ValueTimestampTimeZone.Parse("2024-06-15 12:00:00+00", null));

        foreach (string name in Templates)
        {
            DateTimeTemplate? template = null;
            try
            {
                template = DateTimeTemplate.Of(name);
            }
            catch (Exception)
            {
                // Refused at parse time, which is a result and not a failure.
            }

            if (template is null)
            {
                yield return "parse|" + name + "|REFUSED";
                continue;
            }

            yield return "parse|" + name + "|ok";

            foreach (string date in Samples)
            {
                yield return "fmt|" + name + "|" + date + "|" + Try(() => template.Format(
                    ValueDate.FromDateValue(DateTimeUtils.ParseDateValue(date, 0, date.Length))) ?? "null");
            }

            foreach (string time in TimeSamples)
            {
                yield return "fmtt|" + name + "|" + time + "|" + Try(() => template.Format(
                    ValueTime.FromNanos(DateTimeUtils.ParseTimeNanos(time, 0, time.Length))) ?? "null");
            }
        }

        foreach (string name in BadTemplates)
        {
            yield return "bad|" + name + "|" + Try(() => DateTimeTemplate.Of(name) is null ? "null" : "ok");
        }

        DateTimeTemplate round = DateTimeTemplate.Of("YYYY-MM-DD HH24:MI:SS");
        foreach (string date in Samples)
        {
            yield return "round|" + date + "|" + Try(() => round.Parse(
                date + " 13:45:59", TypeInfo.TypeTimestamp, provider).GetString());
        }
    }

    private static string Try(Func<string?> body)
    {
        try
        {
            return body() ?? "null";
        }
        catch (Exception)
        {
            return "REFUSED";
        }
    }

    /// <summary>
    /// The same templates through SQL: CAST ... FORMAT writes each date/time type out and reads
    /// text back into each of them, including the parts a template leaves out and every refusal.
    /// </summary>
    [JavaOracleFact]
    public void CastWithFormatAgreesWithTheReference()
    {
        string[] statements =
        [
            // Formatting each type through its own fields.
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'YYYY-MM-DD')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'DDD')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'YYYY-DDD')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'YY-M-D')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'RRRR/MM/DD')",
            "SELECT CAST(TIME '12:34:56' AS VARCHAR FORMAT 'HH24:MI:SS')",
            "SELECT CAST(TIME '12:34:56.789' AS VARCHAR FORMAT 'HH24:MI:SS.FF3')",
            "SELECT CAST(TIME '00:34:56' AS VARCHAR FORMAT 'HH:MI:SS A.M.')",
            "SELECT CAST(TIME '15:04:05' AS VARCHAR FORMAT 'HH:MI:SS P.M.')",
            "SELECT CAST(TIME '12:34:56' AS VARCHAR FORMAT 'SSSSS')",
            "SELECT CAST(TIME WITH TIME ZONE '12:34:56+05:30' AS VARCHAR FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST(TIME WITH TIME ZONE '12:34:56-05:30' AS VARCHAR FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST(TIMESTAMP '2024-03-05 12:34:56.789' AS VARCHAR FORMAT 'YYYY-MM-DD HH24:MI:SS.FF3')",
            "SELECT CAST(TIMESTAMP '2024-03-05 12:34:56' AS VARCHAR FORMAT 'YYYY-MM-DD HH24:MI:SS')",
            "SELECT CAST(TIMESTAMP WITH TIME ZONE '2024-03-05 12:34:56.789+02:00' AS VARCHAR FORMAT 'YYYY-MM-DD HH24:MI:SS.FF3 TZH:TZM')",
            "SELECT CAST(TIMESTAMP WITH TIME ZONE '2024-03-05 12:34:56.789-02:00' AS VARCHAR FORMAT 'YYYY-MM-DD HH24:MI:SS.FF3 TZH:TZM')",
            // A NULL goes through untouched.
            "SELECT CAST(CAST(NULL AS DATE) AS VARCHAR FORMAT 'YYYY-MM-DD')",
            // Reading text back into each type.
            "SELECT CAST('2024-03-05' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('05.03.2024' AS DATE FORMAT 'DD.MM.YYYY')",
            "SELECT CAST('065' AS DATE FORMAT 'DDD')",
            "SELECT CAST('03-05' AS DATE FORMAT 'MM-DD')",
            "SELECT CAST('05' AS DATE FORMAT 'DD')",
            "SELECT CAST('2024' AS DATE FORMAT 'YYYY')",
            "SELECT CAST('24-03-05' AS DATE FORMAT 'RR-MM-DD')",
            "SELECT CAST('12:34:56' AS TIME FORMAT 'HH24:MI:SS')",
            "SELECT CAST('12:34:56.789' AS TIME FORMAT 'HH24:MI:SS.FF3')",
            "SELECT CAST('12:34:56 A.M.' AS TIME FORMAT 'HH:MI:SS A.M.')",
            "SELECT CAST('12:34:56 P.M.' AS TIME FORMAT 'HH:MI:SS A.M.')",
            "SELECT CAST('03:04:05 P.M.' AS TIME FORMAT 'HH:MI:SS A.M.')",
            "SELECT CAST('43210' AS TIME FORMAT 'SSSSS')",
            // The template's own space is the delimiter before the sign, so the text carries one.
            "SELECT CAST('12:34:56 +05:30' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST('12:34:56 -05:30' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST('12:34:56 +05:30:15' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM:TZS')",
            "SELECT CAST('2024-03-05 12:34:56.789' AS TIMESTAMP FORMAT 'YYYY-MM-DD HH24:MI:SS.FF3')",
            "SELECT CAST('2024-03-05' AS TIMESTAMP FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('2024-03-05 12:34:56.789 +02:00' AS TIMESTAMP WITH TIME ZONE FORMAT 'YYYY-MM-DD HH24:MI:SS.FF3 TZH:TZM')",
            "SELECT CAST('2024-03-05 12:34:56.789 -02:00' AS TIMESTAMP WITH TIME ZONE FORMAT 'YYYY-MM-DD HH24:MI:SS.FF3 TZH:TZM')",
            // Refusals: a field the target type cannot carry, and values out of range.
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'HH24')",
            "SELECT CAST(TIME '12:00:00' AS VARCHAR FORMAT 'YYYY')",
            "SELECT CAST(TIMESTAMP '2024-03-05 12:00:00' AS VARCHAR FORMAT 'TZH')",
            "SELECT CAST('12:00:00' AS DATE FORMAT 'HH24:MI:SS')",
            "SELECT CAST('2024-03-05' AS TIME FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('12:00:00' AS TIME WITH TIME ZONE FORMAT 'YYYY HH24:MI:SS TZH')",
            "SELECT CAST('2024-03-05 12:00:00+01' AS TIMESTAMP FORMAT 'YYYY-MM-DD HH24:MI:SS TZH')",
            "SELECT CAST('367' AS DATE FORMAT 'DDD')",
            "SELECT CAST('2024-02-30' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('2024-13-01' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('24:00:00' AS TIME FORMAT 'HH24:MI:SS')",
            "SELECT CAST('12:99:00' AS TIME FORMAT 'HH24:MI:SS')",
            "SELECT CAST('12:00:99' AS TIME FORMAT 'HH24:MI:SS')",
            "SELECT CAST('13:00:00 P.M.' AS TIME FORMAT 'HH:MI:SS A.M.')",
            "SELECT CAST('86400' AS TIME FORMAT 'SSSSS')",
            "SELECT CAST('12:00:00 +19:00' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST('12:00:00 +05:60' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST('12:00:00 +05:30:60' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM:TZS')",
            // A template that repeats a field or mixes the alternatives is refused.
            "SELECT CAST('2024' AS DATE FORMAT 'YYYY YYYY')",
            "SELECT CAST('12' AS TIME FORMAT 'HH24 HH')",
            "SELECT CAST('1' AS TIME FORMAT 'HH MI')",
            "SELECT CAST('1' AS TIME FORMAT 'SSSSS HH24')",
            "SELECT CAST('1' AS TIME FORMAT 'TZM')",
            "SELECT CAST('1' AS DATE FORMAT 'Q')",
            // Year forms: negative, rounded, truncated, and the digits a two- or three-letter
            // year cannot hold.
            "SELECT CAST('-2024-03-05' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('2024-03-05' AS DATE FORMAT 'RRRR-MM-DD')",
            "SELECT CAST('90-03-05' AS DATE FORMAT 'RR-MM-DD')",
            "SELECT CAST('60-03-05' AS DATE FORMAT 'RR-MM-DD')",
            "SELECT CAST('5-03-05' AS DATE FORMAT 'Y-MM-DD')",
            "SELECT CAST('12-03-05' AS DATE FORMAT 'Y-MM-DD')",
            "SELECT CAST('1234-03-05' AS DATE FORMAT 'YY-MM-DD')",
            "SELECT CAST('1234-03-05' AS DATE FORMAT 'YYY-MM-DD')",
            "SELECT CAST('-24-03-05' AS DATE FORMAT 'YY-MM-DD')",
            "SELECT CAST('12345678901-03-05' AS DATE FORMAT 'YYYY-MM-DD')",
            // Delimiters must match, and a short text runs out inside a field.
            "SELECT CAST('2024/03/05' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('05/03/2024' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('2024-03' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('2024-03-' AS DATE FORMAT 'YYYY-MM-DD')",
            "SELECT CAST('2024,03,05' AS DATE FORMAT 'YYYY,MM,DD')",
            "SELECT CAST('2024;03;05' AS DATE FORMAT 'YYYY;MM;DD')",
            "SELECT CAST('2024:03:05' AS DATE FORMAT 'YYYY:MM:DD')",
            // Fractions: no digits after the point, and a letter where digits belong.
            "SELECT CAST('12:00:00.' AS TIME FORMAT 'HH24:MI:SS.FF3')",
            "SELECT CAST('12:00:00abc' AS TIME FORMAT 'HH24:MI:SSFF3')",
            // A.M./P.M. must be written out.
            "SELECT CAST('12:00 X.M.' AS TIME FORMAT 'HH24:MI A.M.')",
            // Time zone defaults, a negative zero offset, and an offset too large once the
            // minutes or seconds are added.
            "SELECT CAST('12:34:56 +05' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH')",
            "SELECT CAST('12:34:56 -00:30' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST('12:34:56' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS')",
            "SELECT CAST('12:00:00 +18:01' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM')",
            "SELECT CAST('12:00:00 +18:00:01' AS TIME WITH TIME ZONE FORMAT 'HH24:MI:SS TZH:TZM:TZS')",
            "SELECT CAST(TIME WITH TIME ZONE '12:00:00+01:00' AS VARCHAR FORMAT 'YYYY')",
            // Two-digit years and fractions format through the two-digit path; a negative year
            // keeps its sign when it is written out again.
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'YY-MM-DD')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'RR-MM-DD')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'Y-MM-DD')",
            "SELECT CAST(DATE '2024-03-05' AS VARCHAR FORMAT 'YYY-MM-DD')",
            "SELECT CAST(TIME '12:34:56.789' AS VARCHAR FORMAT 'HH24:MI:SS.FF2')",
            "SELECT CAST(CAST('-2024-03-05' AS DATE FORMAT 'YYYY-MM-DD') AS VARCHAR FORMAT 'YYYY-MM-DD')",
            "SELECT CAST(TIME WITH TIME ZONE '12:34:56+05:30:15' AS VARCHAR FORMAT 'HH24:MI:SS TZH:TZM:TZS')",
            // Template letters that need a digit or a longer year, and adjacent fields.
            "SELECT CAST('1' AS TIME FORMAT 'F0')",
            "SELECT CAST('1' AS TIME FORMAT 'HH24MI')",
            "SELECT CAST('1234' AS TIME FORMAT 'HH24MI')",
            "SELECT CAST('12:00:00' AS TIME FORMAT 'HH24:MI:SSFF3')",
            "SELECT CAST('12:00:00012' AS TIME FORMAT 'HH24:MI:SSFF3')",
            "SELECT CAST('555-03-05' AS DATE FORMAT 'YYY-MM-DD')",
            "SELECT CAST('1' AS TIME FORMAT 'FF0')",
            "SELECT CAST('43210 05' AS TIME WITH TIME ZONE FORMAT 'SSSSSTZH')",
        ];
        List<string> port = RunPort(statements);
        List<string> java = RunJavaCast(statements);
        Assert.True(
            java.Count == port.Count && java.SequenceEqual(port),
            "java:\n  " + string.Join("\n  ", java) + "\nport:\n  " + string.Join("\n  ", port));
    }

    private static List<string> RunPort(IReadOnlyList<string> statements)
    {
        SessionLocal session = new(
            new Database(ConnectionInfo.Memory("date_time_template_cast")), "sa")
        {
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> lines = [];
        foreach (string sql in statements)
        {
            try
            {
                CommandContainer container =
                    Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
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
            catch (Exception e)
            {
                lines.Add(e is H2SharpException h2
                    ? "=" + h2.H2ErrorCode.ToString(CultureInfo.InvariantCulture)
                    : "=" + e.GetType().Name + ":" + e.Message);
            }
        }

        return lines;
    }

    private static List<string> RunJavaCast(IReadOnlyList<string> statements)
        => OracleRunner.Run(
            statements,
            """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:date_time_template_cast");
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
                } else {
                  System.out.println("=" + stat.getUpdateCount());
                }
              } catch (java.sql.SQLException e) {
                System.out.println("=" + e.getErrorCode());
              }
            }
            conn.close();
            """,
            extraJavaArgs: ["-Duser.language=en", "-Duser.timezone=UTC"]);
}

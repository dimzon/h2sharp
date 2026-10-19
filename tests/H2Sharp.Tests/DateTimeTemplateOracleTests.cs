using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Engine;
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
}

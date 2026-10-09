using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Index;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Cursors against the reference: how a cursor walks, and what it is left holding past the end.
/// </summary>
/// <remarks>
/// These three cursors need nothing but a row, so they can be walked with no table, no index and no
/// session. That is exactly why they are worth checking on their own: a cursor is a state machine,
/// and the state after the last row is the part nobody looks at.
/// <para>
/// It is looked at here. <c>RangeCursor</c> builds the row <em>before</em> it decides whether the
/// position is still inside the range, so a cursor that has answered "no" is left holding a row from
/// beyond the end. <c>SingleRowCursor</c> does the opposite and clears its row. Those are different
/// behaviours, and a port that made them the same would pass every happy-path test.
/// </para>
/// <para>
/// Every cursor refuses <c>previous</c>, and the refusal is compared as a refusal rather than by the
/// name of the exception.
/// </para>
/// <para>
/// The walk is capped rather than driven to exhaustion. A cursor whose step does not move never ends,
/// and the reference would run forever on it too, so both sides stop at the same place.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class CursorOracleTests
{
    /// <summary>
    /// How many times a walk asks for another row before it gives up.
    /// </summary>
    private const int Limit = 12;

    private static readonly long[][] Ranges =
    [
        [1, 5, 1],
        [1, 5, 2],
        [1, 5, 10],
        [5, 1, -1],
        [5, 1, -2],
        [1, 1, 1],
        [0, 0, 1],
        [-3, 3, 1],
        [3, 3, -1],
    ];

    [Fact]
    public void CursorsAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.index")
            .ToList();

        List<string> csharp = [];
        csharp.Add(Walk(SingleRowCursor.Empty));
        csharp.Add(Walk(new SingleRowCursor(RowOf(7))));
        csharp.Add(Walk(new SingleRowCursor(Row.Get([], SearchRow.MemoryCalculate))));
        foreach (long[] r in Ranges)
        {
            csharp.Add(Walk(new RangeCursor(r[0], r[1], r[2])));
        }

        csharp.Add(Walk(new DualCursor()));
        csharp.Add(Refuse("single", () => SingleRowCursor.Empty.Previous()));
        csharp.Add(Refuse("range", () => new RangeCursor(1, 5, 1).Previous()));
        csharp.Add(Refuse("dual", () => new DualCursor().Previous()));

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// The reference program, generated into its own package because two of the three cursors are
    /// package-private there and are internal here.
    /// </summary>
    private static string Program() =>
        """
        {
        class R {
        static String show(org.h2.result.Row r) {
          try {
            return shape(r == null, r == null ? null : r.getValue(0));
          } catch (RuntimeException e) {
            return "THROWS";
          }
        }

        static String show(org.h2.result.SearchRow r) {
          try {
            return shape(r == null, r == null ? null : r.getValue(0));
          } catch (RuntimeException e) {
            return "THROWS";
          }
        }

        static String shape(boolean missingRow, org.h2.value.Value v) {
          if (missingRow) {
            return "null";
          }
          return v == null ? "[]" : String.valueOf(v.getString());
        }

        static String walk(org.h2.index.Cursor c) {
          StringBuilder b = new StringBuilder();
          int i = 0;
          while (i++ < 12) {
            boolean more = c.next();
            b.append(more ? '+' : '-').append(show(c.get())).append('|');
            if (!more) {
              break;
            }
          }
          b.append("after=").append(show(c.get())).append('/').append(show(c.getSearchRow()));
          return b.toString();
        }

        static String refuse(String name, Runnable body) {
          try {
            body.run();
            return name + ":no";
          } catch (RuntimeException e) {
            return name + ":THROWS";
          }
        }
        }

        org.h2.result.Row seven = org.h2.result.Row.get(
            new org.h2.value.Value[] { org.h2.value.ValueInteger.get(7) },
            org.h2.result.SearchRow.MEMORY_CALCULATE);
        org.h2.result.Row none = org.h2.result.Row.get(
            new org.h2.value.Value[0], org.h2.result.SearchRow.MEMORY_CALCULATE);

        System.out.println(R.walk(org.h2.index.SingleRowCursor.EMPTY));
        System.out.println(R.walk(new SingleRowCursor(seven)));
        System.out.println(R.walk(new SingleRowCursor(none)));

        long[][] ranges = {
          {1, 5, 1}, {1, 5, 2}, {1, 5, 10}, {5, 1, -1}, {5, 1, -2},
          {1, 1, 1}, {0, 0, 1}, {-3, 3, 1}, {3, 3, -1},
        };
        for (int i = 0; i < ranges.length; i++) {
          System.out.println(R.walk(new RangeCursor(ranges[i][0], ranges[i][1], ranges[i][2])));
        }

        System.out.println(R.walk(new DualCursor()));
        System.out.println(R.refuse("single", () -> SingleRowCursor.EMPTY.previous()));
        System.out.println(R.refuse("range", () -> new RangeCursor(1, 5, 1).previous()));
        System.out.println(R.refuse("dual", () -> new DualCursor().previous()));
        }
        """;

    private static string Walk(Cursor c)
    {
        StringBuilder b = new();
        int i = 0;
        while (i++ < Limit)
        {
            bool more = c.Next();
            _ = b.Append(more ? '+' : '-').Append(Show(c.Get())).Append('|');
            if (!more)
            {
                break;
            }
        }

        return _ = b.Append("after=").Append(Show(c.Get())).Append('/').Append(Show(c.GetSearchRow())).ToString();
    }

    private static Row RowOf(long value) => Row.Get([ValueBigint.Get(value)], SearchRow.MemoryCalculate);

    /// <summary>What a row's first column reads as, with the shape the reference prints it in.</summary>
    private static string Show(Row? row)
    {
        try
        {
            return row is null ? "null" : Show(row.GetValue(0));
        }
        catch (IndexOutOfRangeException)
        {
            // A row with no columns has no first column, and the reference says the same thing by
            // throwing. Reading past the end is a refusal, not an answer.
            return "THROWS";
        }
    }

    private static string Show(SearchRow? row)
    {
        try
        {
            return row is null ? "null" : Show(row.GetValue(0));
        }
        catch (IndexOutOfRangeException)
        {
            return "THROWS";
        }
    }

    private static string Show(Value? v) => v is null ? "[]" : v.GetString() ?? string.Empty;

    private static string Refuse(string name, Action body)
    {
        try
        {
            body();
            return name + ":no";
        }
        catch (Exception)
        {
            // The reference catches RuntimeException and this catches everything, which is close
            // enough: what is being compared is that the call is refused, not which type refused it.
            return name + ":THROWS";
        }
    }
}

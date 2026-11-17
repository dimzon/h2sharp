/*
 * Sprint 245: row comparison of the MVStore value type — a shorter key, NULLs, explicit sort
 * directions, and the final tie-break on the row key.
 */
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueDataTypeRowComparisonTests
{
    private static Row MakeRow(long key, params Value?[] values)
        => Row.Get(values, 1, key);

    [Fact]
    public void ComparesRowsByValuesThenByKey()
    {
        Database database = new(ConnectionInfo.Memory("value_data_type_cmp"));
        SessionLocal session = new(database, "sa");
        try
        {
            ValueDataType type = new(session, session.GetDatabase().GetCompareMode(), null,
                [SortOrder.Ascending, SortOrder.Ascending]);
            Value?[] one = [ValueInteger.Get(1), ValueVarchar.Get("a")];
            Value?[] two = [ValueInteger.Get(1), ValueVarchar.Get("b")];
            Assert.True(type.Compare(MakeRow(1, one), MakeRow(1, two)) < 0);
            Assert.Equal(0, type.Compare(MakeRow(1, one), MakeRow(2, one)));
            // Without a row factory there is no index and no key tie-break.
            Assert.Equal(0, type.Compare(MakeRow(3, one), MakeRow(2, one)));

            ValueDataType sorted = new(session, session.GetDatabase().GetCompareMode(), null,
                [SortOrder.Ascending, SortOrder.Descending]);
            Assert.True(sorted.Compare(MakeRow(1, one), MakeRow(1, two)) > 0);
            Assert.Equal(0, sorted.Compare(MakeRow(1, one), MakeRow(2, one)));
            database.Close();
        }
        finally
        {
            database.Close();
        }
    }
}

/*
 * Sprint 229: the typed ADO.NET adapter properties have to keep the base command objects in
 * step with the H2-specific ones; the constructor is the reference's select-command overload.
 */
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataAdapterSurfaceTests
{
    [Fact]
    public void CommandsAreSharedWithTheBaseAdapter()
    {
        using H2Connection connection = new("Data Source=adapter_surface;Storage=Memory");
        connection.Open();
        using H2Command select = (H2Command)connection.CreateCommand();
        using H2Command insert = (H2Command)connection.CreateCommand();
        using H2Command update = (H2Command)connection.CreateCommand();
        using H2Command delete = (H2Command)connection.CreateCommand();

        H2DataAdapter adapter = new(select);
        Assert.Same(select, adapter.SelectCommand);
        Assert.Same(select, ((System.Data.Common.DbDataAdapter)adapter).SelectCommand);

        adapter.InsertCommand = insert;
        adapter.UpdateCommand = update;
        adapter.DeleteCommand = delete;
        Assert.Same(insert, adapter.InsertCommand);
        Assert.Same(insert, ((System.Data.Common.DbDataAdapter)adapter).InsertCommand);
        Assert.Same(update, adapter.UpdateCommand);
        Assert.Same(update, ((System.Data.Common.DbDataAdapter)adapter).UpdateCommand);
        Assert.Same(delete, adapter.DeleteCommand);
        Assert.Same(delete, ((System.Data.Common.DbDataAdapter)adapter).DeleteCommand);
    }
}

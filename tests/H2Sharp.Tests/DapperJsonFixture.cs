/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Test-only fixture: Dapper's static type maps belong to a separate collectible assembly
 * load context per fixture. No mapping is registered on the default Dapper assembly.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Dapper;

namespace H2Sharp.TestInfrastructure;

/// <summary>
/// A real Dapper instance with JSON typed getters configured only for this fixture.
/// The provider and BCL ADO.NET types remain shared; only Dapper's static state is isolated.
/// </summary>
internal sealed class DapperJsonFixture : IDisposable
{
    private IDbConnection? connection;
    private AssemblyLoadContext? context;
    private Type? mapper;
    private MethodInfo? querySingle;
    private MethodInfo? executeScalar;
    private MethodInfo? executeReader;

    internal DapperJsonFixture(IDbConnection connection)
    {
        this.connection = connection;
        context = new DapperLoadContext();
        try
        {
            Assembly isolated = context.LoadFromAssemblyPath(typeof(SqlMapper).Assembly.Location);
            if (ReferenceEquals(isolated, typeof(SqlMapper).Assembly))
            {
                throw new InvalidOperationException("Dapper must be isolated from the default load context");
            }
            mapper = isolated.GetType("Dapper.SqlMapper", throwOnError: true)!;
            MethodInfo map = mapper.GetMethod("AddTypeMap", [typeof(Type), typeof(DbType), typeof(bool)])!;
            Invoke(map, [typeof(string), DbType.String, true]);
            Invoke(map, [typeof(byte[]), DbType.Binary, true]);
            querySingle = FindSqlMethod("QuerySingle", generic: true);
            executeScalar = FindSqlMethod("ExecuteScalar", generic: true);
            executeReader = FindSqlMethod("ExecuteReader", generic: false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IDbConnection Connection => connection ?? throw new ObjectDisposedException(nameof(DapperJsonFixture));
    internal Assembly MapperAssembly => mapper?.Assembly ?? throw new ObjectDisposedException(nameof(DapperJsonFixture));

    /// <summary>Invokes the actual QuerySingle&lt;T&gt; in this fixture's Dapper assembly.</summary>
    internal T QuerySingle<T>(string sql)
        => (T)Invoke(querySingle!.MakeGenericMethod(typeof(T)), Arguments(sql))!;

    /// <summary>Invokes the actual ExecuteScalar&lt;T&gt; in this fixture's Dapper assembly.</summary>
    internal T ExecuteScalar<T>(string sql)
        => (T)Invoke(executeScalar!.MakeGenericMethod(typeof(T)), Arguments(sql))!;

    /// <summary>Gets Dapper's reader; its getters delegate to the real provider reader.</summary>
    internal DbDataReader ExecuteReader(string sql)
        => (DbDataReader)Invoke(executeReader!, Arguments(sql))!;

    private object?[] Arguments(string sql) => [Connection, sql, null, null, null, null];

    private MethodInfo FindSqlMethod(string name, bool generic)
        => mapper!.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == name && method.IsGenericMethodDefinition == generic
                && parameters.Length == 6 && parameters[0].ParameterType == typeof(IDbConnection)
                && parameters[1].ParameterType == typeof(string);
        });

    private static object? Invoke(MethodInfo method, object?[] arguments)
    {
        try { return method.Invoke(null, arguments); }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    public void Dispose()
    {
        IDbConnection? owned = connection;
        AssemblyLoadContext? isolated = context;
        connection = null;
        context = null;
        mapper = null;
        querySingle = executeScalar = executeReader = null;
        try { owned?.Dispose(); }
        finally { isolated?.Unload(); }
    }

    private sealed class DapperLoadContext : AssemblyLoadContext
    {
        internal DapperLoadContext() : base("Dapper JSON test " + Guid.NewGuid().ToString("N"), isCollectible: true) { }
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Data types as stored in the meta map: a type is written as its class name and read back by
/// resolving that name.
/// </summary>
/// <remarks>
/// The class name is the whole identity, and the names written are the reference's (Java) names,
/// mapped to the port's types by <see cref="JavaNames"/>: a store written by this port is read by
/// the reference and the other way round. An adapter type (<c>ObjectMapDataType</c>) is unwrapped
/// before it is written, so the file never names it.
/// <para>
/// Singletons are looked for by a static field and anything else is constructed with a parameterless
/// constructor, exactly as the reference does, and the result is cached either way when it is a
/// singleton. A failure goes to the handler if there is one and then is rethrown, so a store with a
/// type it cannot load says so rather than carrying on without it.
/// </para>
/// </remarks>
/// <typeparam name="D">what a stateful data type's state belongs to</typeparam>
public sealed class MetaType<D> : BasicDataType<object>
{
    /// <summary>
    /// The reference class name of every data type this port can put into a store.
    /// </summary>
    /// <remarks>
    /// The reference resolves the name with <c>Class.forName</c>; the port has no such thing, so the
    /// name is looked up here. A type that is not in the table refuses by name rather than writing a
    /// name the reference cannot load.
    /// </remarks>
    private static readonly Dictionary<System.Type, string> JavaNames = new()
    {
        [typeof(LongDataType)] = "org.h2.mvstore.type.LongDataType",
        [typeof(IntegerDataType)] = "org.h2.mvstore.type.IntegerDataType",
        [typeof(StringDataType)] = "org.h2.mvstore.type.StringDataType",
        [typeof(ByteArrayDataType)] = "org.h2.mvstore.type.ByteArrayDataType",
        [typeof(ObjectDataType)] = "org.h2.mvstore.type.ObjectDataType",
        // The port's generic object type is the reference's non-generic one: the reference
        // erases the element type, and its ObjectDataType reads any value.
        [typeof(ObjectDataType<object>)] = "org.h2.mvstore.type.ObjectDataType",
        [typeof(Db.NullValueDataType)] = "org.h2.mvstore.db.NullValueDataType",
        [typeof(Db.ValueDataType)] = "org.h2.mvstore.db.ValueDataType",
        [typeof(Db.RowDataType.Factory)] = "org.h2.mvstore.db.RowDataType$Factory",
        [typeof(Tx.VersionedValueType<object, Database>.Factory)] =
            "org.h2.mvstore.tx.VersionedValueType$Factory",
        [typeof(Db.LobStorageMap.BlobReference.Type)] =
            "org.h2.mvstore.db.LobStorageMap$BlobReference$Type",
        [typeof(Db.LobStorageMap.BlobMeta.Type)] =
            "org.h2.mvstore.db.LobStorageMap$BlobMeta$Type",
    };

    private static readonly Dictionary<string, System.Type> ClrTypes = BuildClrTypes();

    private static Dictionary<string, System.Type> BuildClrTypes()
    {
        Dictionary<string, System.Type> map = new(StringComparer.Ordinal);
        foreach (KeyValuePair<System.Type, string> entry in JavaNames)
        {
            _ = map.TryAdd(entry.Value, entry.Key);
        }

        return map;
    }

    private readonly D database;

    private readonly Action<Exception>? exceptionHandler;

    private readonly Dictionary<string, object?> cache = new(StringComparer.Ordinal);

    /// <summary>
    /// A meta type over some database.
    /// </summary>
    /// <param name="database">what a stateful type's state belongs to</param>
    /// <param name="exceptionHandler">told about a type that could not be loaded, if anywhere</param>
    public MetaType(D database, Action<Exception>? exceptionHandler)
    {
        this.database = database;
        this.exceptionHandler = exceptionHandler;
    }

    /// <inheritdoc/>
    /// <remarks>Meta types are not ordered; comparing them is a mistake.</remarks>
    public override int Compare(object? a, object? b) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int GetMemory(object obj) => Constants.MemoryObject;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, object obj)
    {
        if (obj is null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (obj is Tx.IObjectMapDataType adapter)
        {
            // The adapter is the port's bridge to the object-keyed transaction maps; the file
            // names the reference's type, never the bridge.
            obj = adapter.UnderlyingDataType;
        }

        System.Type clazz = obj.GetType();
        StatefulDataType<D>? statefulDataType = null;
        if (obj is StatefulDataType<D> stateful)
        {
            statefulDataType = stateful;
            StatefulDataType<D>.Factory? factory = stateful.GetFactory();
            if (factory is not null)
            {
                clazz = factory.GetType();
            }
        }

        if (!JavaNames.TryGetValue(clazz, out string? className))
        {
            throw new TODOException(
                "MetaType.write: the reference data type " + clazz.FullName + " is not ported");
        }

        int len = className.Length;
        _ = buff.PutVarInt(len);
        _ = buff.PutStringData(className, len);
        statefulDataType?.Save(buff, this);
    }

    /// <inheritdoc/>
    public override object Read(Span<byte> buff, ref int position)
    {
        int len = DataUtils.ReadVarInt(buff, ref position);
        string className = DataUtils.ReadString(buff, ref position, len);
        try
        {
            if (cache.TryGetValue(className, out object? cached) && cached is not null)
            {
                if (cached is StatefulDataType<D>.Factory known)
                {
                    return known.Create(buff, ref position, this, database);
                }

                return ToObjectDataType(cached);
            }

            if (!ClrTypes.TryGetValue(className, out System.Type? clazz))
            {
                throw new TODOException(
                    "MetaType.read: the reference data type " + className + " is not ported");
            }

            bool singleton = false;
            object? obj = clazz.GetField("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            if (obj is null)
            {
                try
                {
                    obj = Activator.CreateInstance(clazz)!;
                }
                catch (Exception e) when (e is MissingMethodException
                    or MemberAccessException
                    or System.Reflection.TargetInvocationException)
                {
                    exceptionHandler?.Invoke(e);
                    throw new InvalidOperationException(e.Message, e);
                }
            }
            else
            {
                singleton = true;
            }

            if (obj is StatefulDataType<D>.Factory factory)
            {
                cache[className] = factory;
                return factory.Create(buff, ref position, this, database);
            }

            if (singleton)
            {
                cache[className] = obj;
            }

            return ToObjectDataType(obj);
        }
        catch (Exception e) when (e is System.Reflection.TargetInvocationException
            or System.Reflection.TargetException
            or MissingMemberException
            or TypeLoadException
            or ArgumentException
            or InvalidCastException
            or System.Security.SecurityException)
        {
            exceptionHandler?.Invoke(e);
            throw new InvalidOperationException(e.Message, e);
        }
    }

    /// <summary>
    /// Presents a typed data type as the object data type the transaction maps are declared
    /// with, as <see cref="Tx.ObjectMapDataType{T}"/> does for the builders.
    /// </summary>
    /// <param name="dataType">the type read back from the registry</param>
    /// <returns>the same type, or an adapter over it</returns>
    internal static DataType<object> ToObjectDataType(object dataType)
    {
        if (dataType is DataType<object> already)
        {
            return already;
        }

        System.Type? element = null;
        foreach (System.Type contract in dataType.GetType().GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(DataType<>))
            {
                element = contract.GetGenericArguments()[0];
                break;
            }
        }

        if (element is null)
        {
            throw DbException.GetInternalError("not a data type: " + dataType.GetType().FullName);
        }

        System.Type adapterType = typeof(Tx.ObjectMapDataType<>).MakeGenericType(element);
        return (DataType<object>)Activator.CreateInstance(
            adapterType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public,
            binder: null,
            args: [dataType],
            culture: null)!;
    }

    /// <inheritdoc/>
    public override object[] CreateStorage(int size) => new object[size];
}

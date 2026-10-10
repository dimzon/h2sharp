using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Data types as stored in the meta map: a type is written as its class name and read back by
/// resolving that name.
/// </summary>
/// <remarks>
/// The class name is the whole identity, so the names written here are .NET type names rather than
/// Java ones. That is a deliberate departure from the on-disk format and it means a store written by
/// this port is readable by this port and not by the reference. The alternative, writing Java names
/// and resolving them, would produce a file that looks compatible and fails at the first read, which
/// is worse than a file that is honestly its own format.
/// <para>
/// Singletons are looked for by a static field and anything else is constructed with a parameterless
/// constructor, exactly as the reference does, and the result is cached either way when it is a
/// singleton. A failure goes to the handler if there is one and then is rethrown, so a store with a
/// type it cannot load says so rather than carrying on without it.
/// </para>
/// </remarks>
/// <typeparam name="D">what a stateful data type's state belongs to</typeparam>
public sealed class MetaType<D> : BasicDataType<DataType<object>>
{
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
    public override int Compare(DataType<object>? a, DataType<object>? b) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int GetMemory(DataType<object> obj) => Constants.MemoryObject;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, DataType<object> obj)
    {
        if (obj is null)
        {
            throw new ArgumentNullException(nameof(obj));
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

        string className = clazz.FullName ?? clazz.Name;
        int len = className.Length;
        _ = buff.PutVarInt(len);
        _ = buff.PutStringData(className, len);
        statefulDataType?.Save(buff, this);
    }

    /// <inheritdoc/>
    public override DataType<object> Read(Span<byte> buff, ref int position)
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

                return (DataType<object>)cached;
            }

            // Type.GetType only resolves assembly-qualified names and system types; the names
            // written here are plain .NET full names of this assembly's types.
            System.Type? clazz = System.Type.GetType(className, throwOnError: false)
                ?? System.Reflection.Assembly.GetExecutingAssembly().GetType(className);
            if (clazz is null)
            {
                throw new TypeLoadException("Can not load data type " + className);
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

            return (DataType<object>)obj;
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

    /// <inheritdoc/>
    public override DataType<object>[] CreateStorage(int size) => new DataType<object>[size];
}

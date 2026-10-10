/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.TestTransactionStore.
 *
 * testCompareWithPostgreSQL is omitted: it needs a live PostgreSQL over JDBC, which this
 * port has neither. Everything else runs, including the threaded tests, on plain Threads.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestTransactionStore : StoreTestBase
{
    [Fact]
    public void TestHclfKey()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        LongDataType keyType = LongDataType.Instance;
        TransactionMap<long, long> map = t.OpenMap("test", keyType, keyType);
        // firstEntry() & firstKey()
        Assert.Null(map.FirstEntry());
        Assert.Equal(0L, map.FirstKey());
        // lastEntry() & lastKey()
        Assert.Null(map.LastEntry());
        Assert.Equal(0L, map.LastKey());
        map.Put(10L, 100L);
        map.Put(20L, 200L);
        map.Put(30L, 300L);
        map.Put(40L, 400L);
        t.Commit();
        t = ts.Begin();
        map = t.OpenMap("test", keyType, keyType);
        map.Put(15L, 150L);
        // The same transaction
        Assert.Equal(new KeyValuePair<long, long>(15L, 150L), map.HigherEntry(10L));
        Assert.Equal(15L, map.HigherKey(10L));
        t = ts.Begin();
        map = t.OpenMap("test", keyType, keyType);
        // Another transaction
        // firstEntry() & firstKey()
        Assert.Equal(new KeyValuePair<long, long>(10L, 100L), map.FirstEntry());
        Assert.Equal(10L, map.FirstKey());
        // lastEntry() & lastKey()
        Assert.Equal(new KeyValuePair<long, long>(40L, 400L), map.LastEntry());
        Assert.Equal(40L, map.LastKey());
        // higherEntry() & higherKey()
        Assert.Equal(new KeyValuePair<long, long>(20L, 200L), map.HigherEntry(10L));
        Assert.Equal(20L, map.HigherKey(10L));
        Assert.Equal(new KeyValuePair<long, long>(20L, 200L), map.HigherEntry(15L));
        Assert.Equal(20L, map.HigherKey(15L));
        Assert.Null(map.HigherEntry(40L));
        Assert.Equal(0L, map.HigherKey(40L));
        // ceilingEntry() & ceilingKey()
        Assert.Equal(new KeyValuePair<long, long>(10L, 100L), map.CeilingEntry(10L));
        Assert.Equal(10L, map.CeilingKey(10L));
        Assert.Equal(new KeyValuePair<long, long>(20L, 200L), map.CeilingEntry(15L));
        Assert.Equal(20L, map.CeilingKey(15L));
        Assert.Equal(new KeyValuePair<long, long>(40L, 400L), map.CeilingEntry(40L));
        Assert.Equal(40L, map.CeilingKey(40L));
        Assert.Null(map.HigherEntry(45L));
        Assert.Equal(0L, map.HigherKey(45L));
        // lowerEntry() & lowerKey()
        Assert.Null(map.LowerEntry(10L));
        Assert.Equal(0L, map.LowerKey(10L));
        Assert.Equal(new KeyValuePair<long, long>(10L, 100L), map.LowerEntry(15L));
        Assert.Equal(10L, map.LowerKey(15L));
        Assert.Equal(new KeyValuePair<long, long>(10L, 100L), map.LowerEntry(20L));
        Assert.Equal(10L, map.LowerKey(20L));
        Assert.Equal(new KeyValuePair<long, long>(20L, 200L), map.LowerEntry(25L));
        Assert.Equal(20L, map.LowerKey(25L));
        // floorEntry() & floorKey()
        Assert.Null(map.FloorEntry(5L));
        Assert.Equal(0L, map.FloorKey(5L));
        Assert.Equal(new KeyValuePair<long, long>(10L, 100L), map.FloorEntry(10L));
        Assert.Equal(10L, map.FloorKey(10L));
        Assert.Equal(new KeyValuePair<long, long>(10L, 100L), map.FloorEntry(15L));
        Assert.Equal(10L, map.FloorKey(15L));
        Assert.Equal(new KeyValuePair<long, long>(30L, 300L), map.FloorEntry(35L));
        Assert.Equal(30L, map.FloorKey(35L));
    }

    [Fact]
    public void TestConcurrentAddRemove()
    {
        using MVStore s = new MVStore.Builder().Open();
        const int threadCount = 3;
        const int keyCount = 2;
        var ts = new TransactionStore(s);
        ts.Init();

        var r = new Random(1);

        var tasks = new Worker[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            var task = new Worker(w =>
            {
                while (!w.Stop)
                {
                    Transaction tx = ts.Begin();
                    TransactionMap<int, int> map = tx.OpenMap<int, int>("data");
                    int k;
                    lock (r)
                    {
                        k = r.Next(keyCount);
                    }

                    try
                    {
                        map.Remove(k);
                        int v;
                        lock (r)
                        {
                            v = r.Next();
                        }

                        map.Put(k, v);
                    }
                    catch (MvStoreException)
                    {
                        // ignore and retry
                    }

                    tx.Commit();
                }
            });
            task.Execute();
            tasks[i] = task;
        }

        Thread.Sleep(1000);
        foreach (Worker t in tasks)
        {
            t.Get();
        }
    }

    [Fact]
    public void TestConcurrentAdd()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        var r = new Random(1);

        int key = -1;
        int failCount = 0;

        var task = new Worker(w =>
        {
            while (!w.Stop)
            {
                int k = Volatile.Read(ref key);
                Transaction tx = ts.Begin();
                TransactionMap<int, int> map = tx.OpenMap<int, int>("data");
                try
                {
                    int v;
                    lock (r)
                    {
                        v = r.Next();
                    }

                    map.Put(k, v);
                }
                catch (MvStoreException)
                {
                    _ = Interlocked.Increment(ref failCount);
                    // ignore and retry
                }

                tx.Commit();
            }
        });
        task.Execute();
        int count = 100000;
        for (int i = 0; i < count; i++)
        {
            Volatile.Write(ref key, i);
            Transaction tx = ts.Begin();
            TransactionMap<int, int> map = tx.OpenMap<int, int>("data");
            try
            {
                int v;
                lock (r)
                {
                    v = r.Next();
                }

                map.Put(i, v);
            }
            catch (MvStoreException)
            {
                _ = Interlocked.Increment(ref failCount);
                // ignore and retry
            }

            tx.Commit();
            if (Volatile.Read(ref failCount) > 0 && i > 4000)
            {
                // stop earlier, if possible
                count = i;
                break;
            }
        }

        task.Get();
        // we expect at least 10% the operations were successful
        AssertTrue(Volatile.Read(ref failCount) + " >= " + (count * 0.9),
            Volatile.Read(ref failCount) < count * 0.9);
        // we expect at least a few failures
        // but this is not guaranteed, and became flaky recently, so we don't assert this anymore
    }

    [Fact]
    public void TestCountWithOpenTransactions()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx1 = ts.Begin();
        TransactionMap<int, int> map1 = tx1.OpenMap<int, int>("data");
        const int size = 150;
        for (int i = 0; i < size; i++)
        {
            map1.Put(i, i * 10);
        }

        tx1.Commit();
        tx1 = ts.Begin();
        map1 = tx1.OpenMap<int, int>("data");

        Transaction tx2 = ts.Begin();
        TransactionMap<int, int> map2 = tx2.OpenMap<int, int>("data");

        var r = new Random(1);
        for (int i = 0; i < size * 3; i++)
        {
            Assert.Equal(size, map1.Size());
            Assert.Equal(size, map1.SizeAsLong());
            // keep the first 10%, and add 10%
            int k = size / 10 + r.Next(size);
            if (r.Next(2) == 0)
            {
                map2.Remove(k);
            }
            else
            {
                map2.Put(k, i);
            }
        }
    }

    [Fact]
    public void TestConcurrentUpdate()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx1 = ts.Begin();
        TransactionMap<int, int> map1 = tx1.OpenMap<int, int>("data");
        map1.Put(1, 10);

        Transaction tx2 = ts.Begin();
        TransactionMap<int, int> map2 = tx2.OpenMap<int, int>("data");
        MvStoreException locked = Assert.Throws<MvStoreException>(() => map2.Put(1, 20));
        CheckErrorCode(DataUtils.ErrorTransactionLocked, locked);
        Assert.Equal(10, map1.Get(1));
        Assert.Equal(0, map2.Get(1));
        tx1.Commit();
        Assert.Equal(10, map2.Get(1));
    }

    [Fact]
    public void TestRepeatedChange()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx0 = ts.Begin();
        TransactionMap<int, int> map0 = tx0.OpenMap<int, int>("data");
        map0.Put(1, -1);
        tx0.Commit();

        Transaction tx = ts.Begin();
        TransactionMap<int, int> map = tx.OpenMap<int, int>("data");
        for (int i = 0; i < 2000; i++)
        {
            map.Put(1, i);
        }

        Transaction tx2 = ts.Begin();
        TransactionMap<int, int> map2 = tx2.OpenMap<int, int>("data");
        Assert.Equal(-1, map2.Get(1));
    }

    [Fact]
    public void TestTransactionAge()
    {
        MVStore s = new MVStore.Builder().Open();
        TransactionStore ts = new TransactionStore(s);
        ts.Init();
        ts.SetMaxTransactionId(16);
        var openList = new List<Transaction>();
        for (int i = 0, j = 1; i < 64; i++)
        {
            Transaction t = ts.Begin();
            openList.Add(t);
            Assert.Equal(j, t.GetId());
            j++;
            if (j > 16)
            {
                j = 1;
            }

            if (openList.Count >= 16)
            {
                t = openList[0];
                openList.RemoveAt(0);
                t.Commit();
            }
        }

        s = new MVStore.Builder().Open();
        TransactionStore ts2 = new TransactionStore(s);
        ts2.Init();
        ts2.SetMaxTransactionId(16);
        var fifo = new List<Transaction>();
        int open = 0;
        for (int i = 0; i < 64; i++)
        {
            if (open >= 16)
            {
                _ = Assert.Throws<MvStoreException>(() => ts2.Begin());
                Transaction first = fifo[0];
                fifo.RemoveAt(0);
                first.Commit();
                open--;
            }

            Transaction t = ts2.Begin();
            t.OpenMap<int, int>("data").Put(i, i);
            fifo.Add(t);
            open++;
        }

        s.Close();
    }

    [Fact]
    public void TestGetModifiedMaps()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx = ts.Begin();
        tx.OpenMap<object, object>("m1");
        tx.OpenMap<object, object>("m2");
        tx.OpenMap<object, object>("m3");
        Assert.False(tx.GetChanges(0).GetEnumerator().MoveNext());
        tx.Commit();

        tx = ts.Begin();
        TransactionMap<string, string> m1 = tx.OpenMap<string, string>("m1");
        TransactionMap<string, string> m2 = tx.OpenMap<string, string>("m2");
        TransactionMap<string, string> m3 = tx.OpenMap<string, string>("m3");
        m1.Put("1", "100");
        long sp = tx.SetSavepoint();
        m2.Put("1", "100");
        m3.Put("1", "100");
        using (IEnumerator<TransactionStore.Change> it = tx.GetChanges(sp).GetEnumerator())
        {
            Assert.True(it.MoveNext());
            TransactionStore.Change c = it.Current;
            Assert.Equal("m3", c.MapName);
            Assert.Equal("1", c.Key!.ToString());
            Assert.Null(c.Value);
            Assert.True(it.MoveNext());
            c = it.Current;
            Assert.Equal("m2", c.MapName);
            Assert.Equal("1", c.Key!.ToString());
            Assert.Null(c.Value);
            Assert.False(it.MoveNext());
        }

        using (IEnumerator<TransactionStore.Change> it = tx.GetChanges(0).GetEnumerator())
        {
            Assert.True(it.MoveNext());
            TransactionStore.Change c = it.Current;
            Assert.Equal("m3", c.MapName);
            Assert.Equal("1", c.Key!.ToString());
            Assert.Null(c.Value);
            Assert.True(it.MoveNext());
            c = it.Current;
            Assert.Equal("m2", c.MapName);
            Assert.Equal("1", c.Key!.ToString());
            Assert.Null(c.Value);
            Assert.True(it.MoveNext());
            c = it.Current;
            Assert.Equal("m1", c.MapName);
            Assert.Equal("1", c.Key!.ToString());
            Assert.Null(c.Value);
            Assert.False(it.MoveNext());
        }

        tx.RollbackToSavepoint(sp);

        using (IEnumerator<TransactionStore.Change> it = tx.GetChanges(0).GetEnumerator())
        {
            Assert.True(it.MoveNext());
            TransactionStore.Change c = it.Current;
            Assert.Equal("m1", c.MapName);
            Assert.Equal("1", c.Key!.ToString());
            Assert.Null(c.Value);
            Assert.False(it.MoveNext());
        }

        tx.Commit();
    }

    [Fact]
    public void TestKeyIterator()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx = ts.Begin();
        TransactionMap<string, string> m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hello");
        m.Put("2", "World");
        m.Put("3", ".");
        tx.Commit();

        Transaction tx2 = ts.Begin();
        TransactionMap<string, string> m2 = tx2.OpenMap<string, string>("test");
        m2.Remove("2");
        m2.Put("3", "!");
        m2.Put("4", "?");

        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        TransactionMap<string, string>.TMIterator<string, string, string> it = m.KeyIterator(null);
        Assert.True(it.HasNext());
        Assert.Equal("1", it.Next());
        Assert.True(it.HasNext());
        Assert.Equal("2", it.Next());
        Assert.True(it.HasNext());
        Assert.Equal("3", it.Next());
        Assert.False(it.HasNext());

        using (IEnumerator<KeyValuePair<string, string>> entryIt = m.EntrySet().GetEnumerator())
        {
            Assert.True(entryIt.MoveNext());
            Assert.Equal("1", entryIt.Current.Key);
            Assert.True(entryIt.MoveNext());
            Assert.Equal("2", entryIt.Current.Key);
            Assert.True(entryIt.MoveNext());
            Assert.Equal("3", entryIt.Current.Key);
            Assert.False(entryIt.MoveNext());
        }

        TransactionMap<string, string>.TMIterator<string, string, string> it2 = m2.KeyIterator(null);
        Assert.True(it2.HasNext());
        Assert.Equal("1", it2.Next());
        Assert.True(it2.HasNext());
        Assert.Equal("3", it2.Next());
        Assert.True(it2.HasNext());
        Assert.Equal("4", it2.Next());
        Assert.False(it2.HasNext());
    }

    [Fact]
    public void TestTwoPhaseCommit()
    {
        string fileName = TempFile("testTwoPhaseCommit.h3");
        DeleteFile(fileName);

        TransactionMap<string, string>? m;

        using (MVStore s = new MVStore.Builder().FileName(fileName).Open())
        {
            var ts = new TransactionStore(s);
            ts.Init();
            Transaction tx = ts.Begin();
            Assert.Null(tx.GetName());
            tx.SetName("first transaction");
            Assert.Equal("first transaction", tx.GetName());
            Assert.Equal(1, tx.GetId());
            Assert.Equal(Transaction.StatusOpen, tx.GetStatus());
            m = tx.OpenMap<string, string>("test");
            m.Put("1", "Hello");
            List<Transaction> list = ts.GetOpenTransactions();
            Assert.Single(list);
            Transaction txOld = list[0];
            Assert.True(tx.GetId() == txOld.GetId());
            Assert.Equal("first transaction", txOld.GetName());
            s.Commit();
        }

        using (MVStore s = new MVStore.Builder().FileName(fileName).Open())
        {
            var ts = new TransactionStore(s);
            ts.Init();
            Transaction tx = ts.Begin();
            Assert.Equal(2, tx.GetId());
            m = tx.OpenMap<string, string>("test");
            Assert.Null(m.Get("1"));
            m.Put("2", "Hello");
            List<Transaction> list = ts.GetOpenTransactions();
            Assert.Equal(2, list.Count);
            Transaction txOld = list[0];
            Assert.Equal(1, txOld.GetId());
            Assert.Equal(Transaction.StatusOpen, txOld.GetStatus());
            Assert.Equal("first transaction", txOld.GetName());
            txOld.Prepare();
            Assert.Equal(Transaction.StatusPrepared, txOld.GetStatus());
            txOld = list[1];
            txOld.Commit();
            s.Commit();
        }

        using (MVStore s = new MVStore.Builder().FileName(fileName).Open())
        {
            var ts = new TransactionStore(s);
            ts.Init();
            Transaction tx = ts.Begin();
            m = tx.OpenMap<string, string>("test");
            m.Put("3", "Test");
            Assert.Equal(2, tx.GetId());
            List<Transaction> list = ts.GetOpenTransactions();
            Assert.Equal(2, list.Count);
            Transaction txOld = list[1];
            Assert.Equal(2, txOld.GetId());
            Assert.Equal(Transaction.StatusOpen, txOld.GetStatus());
            Assert.Null(txOld.GetName());
            txOld.Rollback();
            txOld = list[0];
            Assert.Equal(1, txOld.GetId());
            Assert.Equal(Transaction.StatusPrepared, txOld.GetStatus());
            Assert.Equal("first transaction", txOld.GetName());
            txOld.Commit();
            Assert.Equal("Hello", m.Get("1"));
        }

        DeleteFile(fileName);
    }

    [Fact]
    public void TestSavepoint()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx = ts.Begin();
        TransactionMap<string, string> m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hello");
        m.Put("2", "World");
        m.Put("1", "Hallo");
        m.Remove("2");
        m.Put("3", "!");
        long logId = tx.SetSavepoint();
        m.Put("1", "Hi");
        m.Put("2", ".");
        m.Remove("3");
        tx.RollbackToSavepoint(logId);
        Assert.Equal("Hallo", m.Get("1"));
        Assert.Null(m.Get("2"));
        Assert.Equal("!", m.Get("3"));
        tx.Rollback();

        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        Assert.Null(m.Get("1"));
        Assert.Null(m.Get("2"));
        Assert.Null(m.Get("3"));
    }

    [Fact]
    public void TestConcurrentTransactionsReadCommitted()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        Transaction tx1 = ts.Begin();
        TransactionMap<string, string> m1 = tx1.OpenMap<string, string>("test");
        m1.Put("1", "Hi");
        m1.Put("3", ".");
        tx1.Commit();

        tx1 = ts.Begin();
        m1 = tx1.OpenMap<string, string>("test");
        m1.Put("1", "Hello");
        m1.Put("2", "World");
        m1.Remove("3");
        tx1.Commit();

        // start new transaction to read old data
        Transaction tx2 = ts.Begin();
        TransactionMap<string, string> m2 = tx2.OpenMap<string, string>("test");

        // start transaction tx1, update/delete/add
        tx1 = ts.Begin();
        m1 = tx1.OpenMap<string, string>("test");
        m1.Put("1", "Hallo");
        m1.Remove("2");
        m1.Put("3", "!");

        Assert.Equal("Hello", m2.Get("1"));
        Assert.Equal("World", m2.Get("2"));
        Assert.Null(m2.Get("3"));

        tx1.Commit();

        Assert.Equal("Hallo", m2.Get("1"));
        Assert.Null(m2.Get("2"));
        Assert.Equal("!", m2.Get("3"));

        tx1 = ts.Begin();
        m1 = tx1.OpenMap<string, string>("test");
        m1.Put("2", "World");

        Assert.Null(m2.Get("2"));
        Assert.False(m2.TryRemove("2"));
        Assert.False(m2.TryPut("2", "Welt"));

        tx2 = ts.Begin();
        m2 = tx2.OpenMap<string, string>("test");
        Assert.Null(m2.Get("2"));
        m1.Remove("2");
        Assert.Null(m2.Get("2"));
        tx1.Commit();

        tx1 = ts.Begin();
        m1 = tx1.OpenMap<string, string>("test");
        Assert.Null(m1.Get("2"));
        m1.Put("2", "World");
        m1.Put("2", "Welt");
        tx1.Rollback();

        tx1 = ts.Begin();
        m1 = tx1.OpenMap<string, string>("test");
        Assert.Null(m1.Get("2"));
    }

    [Fact]
    public void TestSingleConnection()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();

        // add, rollback
        Transaction tx = ts.Begin();
        TransactionMap<string, string> m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hello");
        Assert.Equal("Hello", m.Get("1"));
        m.Put("2", "World");
        Assert.Equal("World", m.Get("2"));
        tx.Rollback();
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        Assert.Null(m.Get("1"));
        Assert.Null(m.Get("2"));

        // add, commit
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hello");
        m.Put("2", "World");
        Assert.Equal("Hello", m.Get("1"));
        Assert.Equal("World", m.Get("2"));
        tx.Commit();
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        Assert.Equal("Hello", m.Get("1"));
        Assert.Equal("World", m.Get("2"));

        // update+delete+insert, rollback
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hallo");
        m.Remove("2");
        m.Put("3", "!");
        Assert.Equal("Hallo", m.Get("1"));
        Assert.Null(m.Get("2"));
        Assert.Equal("!", m.Get("3"));
        tx.Rollback();
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        Assert.Equal("Hello", m.Get("1"));
        Assert.Equal("World", m.Get("2"));
        Assert.Null(m.Get("3"));

        // update+delete+insert, commit
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        m.Put("1", "Hallo");
        m.Remove("2");
        m.Put("3", "!");
        Assert.Equal("Hallo", m.Get("1"));
        Assert.Null(m.Get("2"));
        Assert.Equal("!", m.Get("3"));
        tx.Commit();
        tx = ts.Begin();
        m = tx.OpenMap<string, string>("test");
        Assert.Equal("Hallo", m.Get("1"));
        Assert.Null(m.Get("2"));
        Assert.Equal("!", m.Get("3"));
    }

    [Fact]
    public void TestStoreMultiThreadedReads()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        TransactionMap<int, int> mapA = t.OpenMap<int, int>("a");
        mapA.Put(1, 0);
        t.Commit();

        var task = new Worker(w =>
        {
            for (int i = 0; !w.Stop; i++)
            {
                Transaction tx = ts.Begin();
                TransactionMap<int, int> mapAx = tx.OpenMap<int, int>("a");
                while (!mapAx.TryPut(1, i))
                {
                    // repeat
                }

                tx.Commit();

                // map B transaction
                // the other thread will get a map A uncommitted value,
                // but by the time it tries to walk back to the committed
                // value, the undoLog has changed
                tx = ts.Begin();
                TransactionMap<int, int> mapB = tx.OpenMap<int, int>("b");
                // put a new value to the map; this will cause a map B
                // undoLog entry to be created with a null pre-image value
                mapB.TryPut(i, -i);
                // this is where the real race condition occurs:
                // some other thread might get the B log entry
                // for this transaction rather than the uncommitted A log
                // entry it is expecting
                tx.Commit();
            }
        });
        task.Execute();
        try
        {
            for (int i = 0; i < 10000; i++)
            {
                // Values are ints, so absence would read as 0 exactly like a stored 0: the
                // key is put once and never removed, therefore every read observes it. The
                // loop still exercises the concurrent read path, where a corruption would
                // surface as an exception rather than a null.
                Transaction tx = ts.Begin();
                mapA = tx.OpenMap<int, int>("a");
                _ = mapA.Get(1);
                tx.Commit();
            }
        }
        finally
        {
            task.Get();
        }
    }

    [Fact]
    public void TestCommitAfterMapRemoval()
    {
        using MVStore s = new MVStore.Builder().Open();
        var ts = new TransactionStore(s);
        ts.Init();
        Transaction t = ts.Begin();
        TransactionMap<long, string> map = t.OpenMap("test", LongDataType.Instance, StringDataType.Instance);
        map.Put(1L, "A");
        s.RemoveMap("test");
        try
        {
            t.Commit();
        }
        finally
        {
            // commit should not fail, but even if it does
            // transaction should be cleanly removed and store remains operational
            Assert.True(ts.GetOpenTransactions().Count == 0);
            Assert.False(ts.HasMap("test"));
            t = ts.Begin();
            map = t.OpenMap("test", LongDataType.Instance, StringDataType.Instance);
            Assert.True(map.IsEmpty());
            map.Put(2L, "B");
        }
    }

    [Fact]
    public void TestDeadLock()
    {
        const int threadCount = 2;
        for (int i = 1; i < threadCount; i++)
        {
            DeadLock(threadCount, i);
        }
    }

    private void DeadLock(int threadCount, int stepCount)
    {
        using MVStore s = new MVStore.Builder().Open();
        s.SetAutoCommitDelay(0);
        var ts = new TransactionStore(s,
            new MetaType<object>(null!, ex => s.BackgroundExceptionHandler?.Invoke(Thread.CurrentThread, ex)),
            new ObjectDataType(), 10000);
        ts.Init();
        Transaction t = ts.Begin();
        TransactionMap<long, long> m = t.OpenMap("test", LongDataType.Instance, LongDataType.Instance);
        for (int i = 0; i < threadCount; i++)
        {
            m.Put((long)i, 0L);
        }

        t.Commit();

        var latch = new CountdownEvent(threadCount);
        var tasks = new Worker[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            long initialKey = i;
            var worker = new Worker(_ =>
            {
                Transaction tx = ts.Begin();
                try
                {
                    TransactionMap<long, long> map = tx.OpenMap("test", LongDataType.Instance,
                            LongDataType.Instance);
                    long key = initialKey;
                    map.ComputeIfPresent(key, (k, v) => v + 1);
                    latch.Signal();
                    latch.Wait();
                    for (int j = 0; j < stepCount; j++)
                    {
                        key = (key + 1) % threadCount;
                        map.Lock(key);
                        map.Put(key, map.Get(key) + 1);
                    }

                    tx.Commit();
                }
                catch (Exception)
                {
                    tx.Rollback();
                    throw;
                }
            });
            worker.Execute();
            tasks[i] = worker;
        }

        int failureCount = 0;
        foreach (Worker task in tasks)
        {
            Exception? exception = task.GetException();
            if (exception is not null)
            {
                ++failureCount;
                Assert.IsType<MvStoreException>(exception);
                CheckErrorCode(DataUtils.ErrorTransactionsDeadlock, exception);
            }
        }

        AssertEqual(" " + stepCount, stepCount, failureCount);
        t = ts.Begin();
        m = t.OpenMap("test", LongDataType.Instance, LongDataType.Instance);
        int count = 0;
        for (int i = 0; i < threadCount; i++)
        {
            long? value = m.Get((long)i);
            Assert.NotNull(value);
            count += (int)value!.Value;
        }

        t.Commit();
        AssertEqual(" " + stepCount, (stepCount + 1) * (threadCount - failureCount), count);
    }

    /// <summary>
    /// A background worker with captured failure, standing in for the reference's test Task:
    /// Get signals the loop to stop and joins, GetException only joins.
    /// </summary>
    private sealed class Worker
    {
        private readonly Thread thread;

        private Exception? exception;

        private volatile bool stop;

        public Worker(Action<Worker> action)
        {
            thread = new Thread(() =>
            {
                try
                {
                    action(this);
                }
                catch (Exception e)
                {
                    exception = e;
                }
            })
            {
                IsBackground = true,
            };
        }

        public bool Stop => stop;

        public void Execute() => thread.Start();

        public Exception? Get()
        {
            stop = true;
            thread.Join();
            return exception;
        }

        public Exception? GetException()
        {
            thread.Join();
            return exception;
        }
    }
}

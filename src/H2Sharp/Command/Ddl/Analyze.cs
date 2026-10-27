/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.Analyze.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The row-count statistics are collected through the table's scan index. User and
 * trigger checks are omitted because users (D-004) and triggers are not ported.
 * DbSettings.ANALYZE_SAMPLE is not ported either; its reference default is used.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// The statements ANALYZE and ANALYZE TABLE.
/// </summary>
public sealed class Analyze : DefineCommand
{
    private sealed class SelectivityData
    {
        private long distinctCount;

        /// <summary>The number of occupied slots, excluding the zero element.</summary>
        private int size;

        private int[] elements = new int[8];

        private bool zeroElement;

        private int maxSize = 7;

        public void Add(Value v)
        {
            int currentSize = CurrentSize;
            if (currentSize >= Constants.SelectivityDistinctCount)
            {
                size = 0;
                Array.Fill(elements, 0);
                zeroElement = false;
                distinctCount += currentSize;
            }

            int hash = v.GetHashCode();
            if (hash == 0)
            {
                zeroElement = true;
            }
            else
            {
                if (size >= maxSize)
                {
                    Rehash();
                }

                Add(hash);
            }
        }

        public int GetSelectivity(long count)
        {
            int s;
            if (count == 0)
            {
                s = 0;
            }
            else
            {
                s = (int)(100 * (distinctCount + CurrentSize) / count);
                if (s <= 0)
                {
                    s = 1;
                }
            }

            return s;
        }

        private int CurrentSize
        {
            get
            {
                int currentSize = size;
                if (zeroElement)
                {
                    currentSize++;
                }

                return currentSize;
            }
        }

        private void Add(int element)
        {
            int len = elements.Length;
            int mask = len - 1;
            int index = element & mask;
            int plus = 1;
            do
            {
                int k = elements[index];
                if (k == 0)
                {
                    size++;
                    elements[index] = element;
                    return;
                }

                if (k == element)
                {
                    return;
                }

                index = (index + plus++) & mask;
            }
            while (plus <= len);
        }

        private void Rehash()
        {
            size = 0;
            int[] oldElements = elements;
            int len = oldElements.Length << 1;
            elements = new int[len];
            maxSize = (int)(len * 90L / 100);
            foreach (int k in oldElements)
            {
                if (k != 0)
                {
                    Add(k);
                }
            }
        }
    }

    private int sampleRows = 10_000;

    private Table? table;

    /// <summary>Creates the statement.</summary>
    public Analyze(SessionLocal session) : base(session)
    {
    }

    /// <summary>Sets the table.</summary>
    public void SetTable(Table table) => this.table = table;

    /// <inheritdoc/>
    public override long Update()
    {
        // D-004: checkAdmin has no user object to check.
        SessionLocal session = GetSession();
        if (table is not null)
        {
            AnalyzeTable(session, table, sampleRows, true);
        }
        else
        {
            foreach (Schema schema in GetSession().GetDatabase().GetSchemas())
            {
                foreach (Table schemaTable in schema.GetAllTablesAndViews(null!))
                {
                    AnalyzeTable(session, schemaTable, sampleRows, true);
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Analyzes a table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    /// <param name="sample">the number of sample rows</param>
    /// <param name="manual">whether the command was called by the user</param>
    public static void AnalyzeTable(SessionLocal session, Table table, int sample, bool manual)
    {
        // Table validity is not exposed by the port; removed tables are dropped from the schema.
        if (table.GetTableType() != Table.TABLE
            || table.IsTemporary() && !table.IsGlobalTemporary()
            || table.IsLockedExclusivelyBy(session))
        {
            return;
        }

        _ = table.Lock(session, Table.ReadLock);
        Column[] columns = table.GetColumns();
        int columnCount = columns.Length;
        if (columnCount == 0)
        {
            return;
        }

        Cursor? cursor = table.GetScanIndex(session)?.GetCursor(session, null, null, false);
        if (cursor is not null && cursor.Next())
        {
            SelectivityData?[] array = new SelectivityData?[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                if (!DataType.IsLargeObject(columns[i].GetTypeInfo().GetValueType()))
                {
                    array[i] = new SelectivityData();
                }
            }

            long rowNumber = 0;
            do
            {
                Result.Row row = cursor.Get()!;
                for (int i = 0; i < columnCount; i++)
                {
                    array[i]?.Add(row.GetValue(i)!);
                }

                rowNumber++;
            }
            while ((sample <= 0 || rowNumber < sample) && cursor.Next());

            for (int i = 0; i < columnCount; i++)
            {
                SelectivityData? selectivity = array[i];
                if (selectivity is not null)
                {
                    columns[i].SetSelectivity(selectivity.GetSelectivity(rowNumber));
                }
            }
        }
        else
        {
            for (int i = 0; i < columnCount; i++)
            {
                columns[i].SetSelectivity(0);
            }
        }

        session.GetDatabase().UpdateMeta(session, table);
    }

    /// <summary>Sets the sample size.</summary>
    public void SetTop(int top) => sampleRows = top;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Analyze;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;
}

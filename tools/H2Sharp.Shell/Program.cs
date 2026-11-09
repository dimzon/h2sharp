/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.tools.Shell (H2 2.5.x), adapted to the H2Sharp ADO.NET API.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using H2Sharp.Data;

namespace H2Sharp.Shell;

internal static class Program
{
    private const int MaxRowBuffer = 5000;
    private const int HistoryCount = 20;

    private static bool listMode;
    private static int maxColumnSize = 100;
    private static readonly List<string> history = new();
    private static H2Connection? connection;

    private static int Main(string[] args)
    {
        string? connectionString = null;
        string? dataSource = null;
        string? storage = null;
        string? sql = null;
        bool help = false;
        List<string> positionals = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-cs":
                case "--connection-string":
                    connectionString = args[++i];
                    break;
                case "-d":
                case "--data-source":
                    dataSource = args[++i];
                    break;
                case "--storage":
                    storage = args[++i];
                    break;
                case "-sql":
                case "--sql":
                    sql = args[++i];
                    break;
                case "-list":
                case "--list":
                    listMode = true;
                    break;
                case "-h":
                case "--help":
                case "-?":
                    help = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        Console.WriteLine("Unknown option: " + arg);
                        ShowUsage();
                        return 1;
                    }
                    positionals.Add(arg);
                    break;
            }
        }

        if (help)
        {
            ShowUsage();
            return 0;
        }

        if (connectionString is null)
        {
            if (positionals.Count > 0)
            {
                dataSource = positionals[0];
            }

            dataSource ??= "test";
            if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                dataSource = "shell";
                storage ??= "Memory";
            }

            storage ??= "File";
            connectionString = "Data Source=" + dataSource + ";Storage=" + storage;
        }

        try
        {
            connection = new H2Connection(connectionString);
            connection.Open();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
            return 1;
        }

        if (sql is not null)
        {
            foreach (string statement in SplitStatements(sql))
            {
                Execute(statement);
            }

            CloseConnection();
            return 0;
        }

        PromptLoop();
        return 0;
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage: H2Sharp.Shell [options] [dataSource]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -cs, --connection-string <value>   H2Sharp connection string");
        Console.WriteLine("  -d, --data-source <value>          database file path or memory key");
        Console.WriteLine("  --storage Memory|File              storage kind (default File)");
        Console.WriteLine("  -sql, --sql <statements>           execute SQL statements and exit");
        Console.WriteLine("  -list, --list                      enable result list mode");
        Console.WriteLine("  -h, --help, -?                     show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  H2Sharp.Shell");
        Console.WriteLine("  H2Sharp.Shell test.db");
        Console.WriteLine("  H2Sharp.Shell -sql \"SELECT 1\"");
        Console.WriteLine("  H2Sharp.Shell -cs \"Data Source=demo;Storage=Memory\" -sql \"SELECT 1\"");
    }

    private static void PromptLoop()
    {
        Console.WriteLine();
        Console.WriteLine("Welcome to H2 Shell " + (typeof(H2Connection).Assembly.GetName().Version?.ToString() ?? "2.5.259"));
        Console.WriteLine("Exit with Ctrl+C");
        ShowHelp();

        string? statement = null;
        while (true)
        {
            try
            {
                Console.Write(statement is null ? "sql> " : "...> ");
                string? line = Console.ReadLine();
                if (line is null)
                {
                    break;
                }

                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                bool end = trimmed.EndsWith(";", StringComparison.Ordinal);
                if (end)
                {
                    line = line.Substring(0, line.LastIndexOf(';'));
                    trimmed = trimmed.Substring(0, trimmed.Length - 1);
                }

                string lower = trimmed.ToLowerInvariant();
                if (lower == "exit" || lower == "quit")
                {
                    break;
                }
                else if (lower == "help" || lower == "?")
                {
                    ShowHelp();
                }
                else if (lower == "list")
                {
                    listMode = !listMode;
                    Console.WriteLine("Result list mode is now " + (listMode ? "on" : "off"));
                }
                else if (lower == "history")
                {
                    if (history.Count == 0)
                    {
                        Console.WriteLine("No history");
                    }
                    else
                    {
                        for (int i = 0; i < history.Count; i++)
                        {
                            string s = history[i].Replace('\n', ' ').Replace('\r', ' ');
                            Console.WriteLine("#" + (1 + i) + ": " + s);
                        }

                        Console.WriteLine("To re-run a statement, type the number and press and enter");
                    }
                }
                else if (lower.StartsWith("maxwidth", StringComparison.Ordinal))
                {
                    string rest = trimmed.Substring("maxwidth".Length).Trim();
                    if (int.TryParse(rest, out int width))
                    {
                        maxColumnSize = width;
                    }
                    else
                    {
                        Console.WriteLine("Usage: maxwidth <integer value>");
                    }

                    Console.WriteLine("Maximum column width is now " + maxColumnSize);
                }
                else
                {
                    bool addToHistory = true;
                    if (statement is null)
                    {
                        if (int.TryParse(trimmed, out int position) && position >= 1 && position <= history.Count)
                        {
                            statement = history[position - 1];
                            addToHistory = false;
                            Console.WriteLine(statement);
                            end = true;
                        }
                        else
                        {
                            statement = line;
                        }
                    }
                    else
                    {
                        statement += "\n" + line;
                    }

                    if (end)
                    {
                        if (addToHistory && statement is not null)
                        {
                            history.Insert(0, statement);
                            if (history.Count > HistoryCount)
                            {
                                history.RemoveAt(history.Count - 1);
                            }
                        }

                        Execute(statement!);
                        statement = null;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
                statement = null;
            }
        }

        CloseConnection();
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Commands are case insensitive; SQL statements end with ';'");
        Console.WriteLine("help or ?      Display this help");
        Console.WriteLine("list           Toggle result list / stack trace mode");
        Console.WriteLine("maxwidth       Set maximum column width (default is 100)");
        Console.WriteLine("history        Show the last 20 statements");
        Console.WriteLine("quit or exit   Close the connection and exit");
        Console.WriteLine();
    }

    private static void CloseConnection()
    {
        if (connection is not null)
        {
            try
            {
                connection.Close();
                Console.WriteLine("Connection closed");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
            }
            finally
            {
                connection = null;
            }
        }
    }

    private static void Execute(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        if (connection is null)
        {
            Console.WriteLine("Not connected");
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using DbDataReader reader = command.ExecuteReader();
            int rowCount;
            if (reader.FieldCount > 0)
            {
                rowCount = listMode ? PrintList(reader) : PrintTable(reader);
            }
            else
            {
                int affected = reader.RecordsAffected;
                stopwatch.Stop();
                Console.WriteLine("(Update count: " + affected + ", " + stopwatch.ElapsedMilliseconds + " ms)");
                return;
            }

            stopwatch.Stop();
            Console.WriteLine("(" + rowCount + (rowCount == 1 ? " row, " : " rows, ") + stopwatch.ElapsedMilliseconds + " ms)");
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
            if (listMode)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }

    private static int PrintTable(DbDataReader reader)
    {
        int columnCount = reader.FieldCount;
        List<string[]> rows = new();
        string[] columns = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columns[i] = reader.GetName(i) ?? string.Empty;
        }

        rows.Add(columns);

        int rowCount = 0;
        bool truncated = false;
        while (reader.Read())
        {
            rowCount++;
            string[] row = new string[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                string value = reader.IsDBNull(i) ? "null" : (reader.GetValue(i)?.ToString() ?? "null");
                if (columnCount > 1 && value.Length > maxColumnSize)
                {
                    value = value.Substring(0, maxColumnSize);
                    truncated = true;
                }

                row[i] = value;
            }

            rows.Add(row);
            if (rows.Count > MaxRowBuffer)
            {
                PrintRows(rows, columnCount);
                rows.Clear();
            }
        }

        PrintRows(rows, columnCount);
        rows.Clear();
        if (truncated)
        {
            Console.WriteLine("(data is partially truncated)");
        }

        return rowCount;
    }

    private static void PrintRows(List<string[]> rows, int columnCount)
    {
        if (rows.Count == 0)
        {
            return;
        }

        int[] sizes = new int[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            int max = 0;
            foreach (string[] row in rows)
            {
                if (row[i].Length > max)
                {
                    max = row[i].Length;
                }
            }

            if (columnCount > 1)
            {
                max = Math.Min(maxColumnSize, max);
            }

            sizes[i] = max;
        }

        foreach (string[] row in rows)
        {
            StringBuilder builder = new();
            for (int i = 0; i < columnCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ').Append('|').Append(' ');
                }

                string value = row[i];
                builder.Append(value);
                if (i < columnCount - 1)
                {
                    for (int j = value.Length; j < sizes[i]; j++)
                    {
                        builder.Append(' ');
                    }
                }
            }

            Console.WriteLine(builder.ToString());
        }
    }

    private static int PrintList(DbDataReader reader)
    {
        int columnCount = reader.FieldCount;
        string[] columns = new string[columnCount];
        int longestLabel = 0;
        for (int i = 0; i < columnCount; i++)
        {
            columns[i] = reader.GetName(i) ?? string.Empty;
            if (columns[i].Length > longestLabel)
            {
                longestLabel = columns[i].Length;
            }
        }

        int rowCount = 0;
        StringBuilder builder = new();
        while (reader.Read())
        {
            rowCount++;
            if (rowCount > 1)
            {
                Console.WriteLine();
            }

            builder.Clear();
            for (int i = 0; i < columnCount; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                string label = columns[i];
                builder.Append(label);
                for (int j = label.Length; j < longestLabel; j++)
                {
                    builder.Append(' ');
                }

                string value = reader.IsDBNull(i) ? "null" : (reader.GetValue(i)?.ToString() ?? "null");
                builder.Append(": ").Append(value);
            }

            Console.WriteLine(builder.ToString());
        }

        if (rowCount == 0)
        {
            builder.Clear();
            for (int i = 0; i < columnCount; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(columns[i]);
            }

            Console.WriteLine(builder.ToString());
        }

        return rowCount;
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        StringBuilder builder = new();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char current = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                builder.Append(current);
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                builder.Append(current);
                if (current == '*' && next == '/')
                {
                    builder.Append(next);
                    i++;
                    inBlockComment = false;
                }

                continue;
            }

            if (inSingleQuote)
            {
                builder.Append(current);
                if (current == '\'')
                {
                    if (next == '\'')
                    {
                        builder.Append(next);
                        i++;
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (inDoubleQuote)
            {
                builder.Append(current);
                if (current == '"')
                {
                    if (next == '"')
                    {
                        builder.Append(next);
                        i++;
                    }
                    else
                    {
                        inDoubleQuote = false;
                    }
                }

                continue;
            }

            if (current == '-' && next == '-')
            {
                inLineComment = true;
                builder.Append(current);
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                builder.Append(current);
                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                builder.Append(current);
                continue;
            }

            if (current == '"')
            {
                inDoubleQuote = true;
                builder.Append(current);
                continue;
            }

            if (current == ';')
            {
                string statement = builder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    yield return statement;
                }

                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        string tail = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            yield return tail;
        }
    }
}
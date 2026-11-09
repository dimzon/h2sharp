using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Management.Automation;
using System.Text;
using H2Sharp.Data;

namespace H2Sharp.PowerShell;

[Cmdlet(VerbsLifecycle.Invoke, "H2Query")]
[OutputType(typeof(PSObject), typeof(DataTable), typeof(object))]
public sealed class InvokeH2QueryCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("Sql")]
    public string Query { get; set; } = string.Empty;

    [Parameter(Position = 1)]
    [Alias("DataSource", "Path")]
    public string? Database { get; set; }

    [Parameter]
    public string? ConnectionString { get; set; }

    [Parameter]
    [ValidateSet("File", "Memory", IgnoreCase = true)]
    public string Storage { get; set; } = "File";

    [Parameter]
    public Hashtable? Parameters { get; set; }

    [Parameter]
    [ValidateSet("PSObject", "DataTable", "SingleValue", "None", IgnoreCase = true)]
    public string As { get; set; } = "PSObject";

    [Parameter]
    public SwitchParameter NonQuery { get; set; }

    [Parameter]
    public string? InputFile { get; set; }

    private H2Connection? connection;

    protected override void BeginProcessing()
    {
        string query = Query;
        if (!string.IsNullOrWhiteSpace(InputFile))
        {
            query = File.ReadAllText(InputFile!);
            Query = query;
        }

        string connectionString = ConnectionString ?? BuildConnectionString();
        connection = new H2Connection(connectionString);
        connection.Open();
    }

    protected override void ProcessRecord()
    {
        if (connection is null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException("The connection is not open."),
                "ConnectionNotOpen",
                ErrorCategory.InvalidOperation,
                null));
            return;
        }

        bool asDataTable = string.Equals(As, "DataTable", StringComparison.OrdinalIgnoreCase);
        bool asSingleValue = string.Equals(As, "SingleValue", StringComparison.OrdinalIgnoreCase);
        bool asNone = string.Equals(As, "None", StringComparison.OrdinalIgnoreCase) || NonQuery.IsPresent;

        foreach (string statement in SplitStatements(Query))
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            using DbCommand command = connection.CreateCommand();
            command.CommandText = statement;
            AddParameters(command);

            using DbDataReader reader = command.ExecuteReader();
            if (reader.FieldCount <= 0)
            {
                if (NonQuery.IsPresent || asNone)
                {
                    WriteObject(reader.RecordsAffected);
                }

                continue;
            }

            if (asDataTable)
            {
                WriteObject(ReadDataTable(reader), false);
            }
            else if (asSingleValue)
            {
                if (reader.Read())
                {
                    WriteObject(reader.IsDBNull(0) ? null : reader.GetValue(0));
                }

                return;
            }
            else if (asNone)
            {
                while (reader.Read())
                {
                    // Drain the result set.
                }
            }
            else
            {
                while (reader.Read())
                {
                    PSObject row = new();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string name = reader.GetName(i);
                        object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row.Properties.Add(new PSNoteProperty(name, value));
                    }

                    WriteObject(row);
                }
            }
        }
    }

    protected override void EndProcessing()
    {
        if (connection is not null)
        {
            connection.Close();
            connection = null;
        }
    }

    protected override void StopProcessing()
    {
        if (connection is not null)
        {
            connection.Close();
            connection = null;
        }

        base.StopProcessing();
    }

    private string BuildConnectionString()
    {
        string database = string.IsNullOrWhiteSpace(Database) ? "test" : Database!;
        string storage = Storage;
        if (string.Equals(database, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            database = "shell";
            storage = "Memory";
        }

        return "Data Source=" + database + ";Storage=" + storage;
    }

    private void AddParameters(DbCommand command)
    {
        if (Parameters is null)
        {
            return;
        }

        foreach (DictionaryEntry entry in Parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "$" + entry.Key;
            parameter.Value = entry.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    private static DataTable ReadDataTable(DbDataReader reader)
    {
        DataTable table = new();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            table.Columns.Add(reader.GetName(i), typeof(object));
        }

        while (reader.Read())
        {
            DataRow row = table.NewRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }

            table.Rows.Add(row);
        }

        return table;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Adapters.Db;

/// <summary>
/// The SQL this data source is allowed to run, by name.
///
/// The whole point is that SQL is configuration and message content is data. A mapper is a Scriban
/// template evaluated over an inbound payload; if it can emit SQL text, then whoever can get a
/// message into Bitween can steer a statement against the customer's database. So the statement is
/// named here, at configure time, and the message supplies parameter VALUES only.
/// </summary>
public class StatementRegistry
{
    /// <summary>
    /// One configured statement. Most are just SQL; a statement a receiver polls with also carries
    /// the shape of its rows - which column is the cursor, which identifies the row.
    ///
    /// Those two live with the statement rather than with the subscription reading it because they
    /// describe what the QUERY returns. A poll statement returns the same cursor column whoever
    /// reads it, and letting each reader nominate its own is two chances to nominate the wrong one
    /// with nothing to check them against.
    /// </summary>
    public sealed class Statement
    {
        public string Sql { get; set; }
        public string CursorColumn { get; set; }
        public string KeyColumn { get; set; }
    }

    readonly Dictionary<string, Statement> statements =
        new(StringComparer.OrdinalIgnoreCase);

    public StatementRegistry(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        JObject parsed;
        try
        {
            parsed = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            // Named rather than swallowed: an adapter that silently starts with no statements
            // fails later, on a message, as "unknown statement" - which sends whoever is debugging
            // it looking in the wrong place entirely.
            throw new ArgumentException(
                $"The Statements setting is not valid JSON: {ex.Message}. It should be an object "
                + "of name to SQL, for example {\"getOrder\": \"select * from orders where id = :id\"}.");
        }

        foreach (var property in parsed.Properties())
        {
            // Two shapes. A string is the original and still the common case; an object is a
            // statement that also says which of its columns is the cursor and which is the key,
            // which only a polled statement needs.
            if (property.Value.Type == JTokenType.String)
            {
                statements[property.Name] = new Statement { Sql = property.Value.Value<string>() };
                continue;
            }

            if (property.Value is JObject shaped)
            {
                var sql = shaped.Value<string>("sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new ArgumentException(
                        $"Statement '{property.Name}' is an object without a 'sql' property, so "
                        + "there is nothing to run.");

                statements[property.Name] = new Statement
                {
                    Sql = sql,
                    CursorColumn = shaped.Value<string>("cursorColumn"),
                    KeyColumn = shaped.Value<string>("keyColumn"),
                };
                continue;
            }

            throw new ArgumentException(
                $"Statement '{property.Name}' has to be a string of SQL, or an object with a "
                + $"'sql' property - not a {property.Value.Type}.");
        }
    }

    public IReadOnlyCollection<string> Names => statements.Keys;

    public int Count => statements.Count;

    /// <summary>
    /// The whole configured statement by name, or null. For a receiver, which needs the row shape
    /// as well as the SQL.
    /// </summary>
    public Statement Find(string name) =>
        !string.IsNullOrWhiteSpace(name) && statements.TryGetValue(name, out var found) ? found : null;

    /// <summary>
    /// The SQL to run for this request. A name resolves against the configured set; raw SQL is
    /// refused outright unless the data source allows it.
    /// </summary>
    public string Resolve(string name, string sql, bool allowAdHoc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (statements.TryGetValue(name, out var found)) return found.Sql;

            throw new InvalidOperationException(
                $"'{name}' is not a statement this data source defines. Configured: "
                + (statements.Count == 0
                    ? "none - set the Statements property on the data source."
                    : string.Join(", ", statements.Keys.OrderBy(k => k))));
        }

        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException(
                "Nothing to run: give either the Name of a configured statement, or Sql if this "
                + "data source allows ad-hoc SQL.");

        if (!allowAdHoc)
            throw new InvalidOperationException(
                "This data source does not allow ad-hoc SQL, so a statement has to be named. SQL "
                + "sent with a message is SQL an inbound message can steer; define it on the data "
                + "source and pass parameters instead. AllowAdHocSql exists for the cases where "
                + "that trade is made deliberately.");

        return sql;
    }
}

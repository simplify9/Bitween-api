using Newtonsoft.Json;
using SW.Bitween.Domain.DataSources;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// Turns a data source's statement rows into the <c>Statements</c> value its adapter receives.
///
/// This is the seam that let statements become their own entity without touching the adapter
/// contract: the adapter has always been handed name-to-SQL as JSON and has never known whether
/// that came from a field somebody typed into or from rows with their own permissions and audit
/// trail. Keeping the composition here - rather than inline in the supervisor - is what makes it
/// testable without starting a database.
/// </summary>
public static class StatementComposer
{
    /// <summary>
    /// Active statements for one data source, as JSON. Null when there are none, so the caller can
    /// leave the setting absent rather than sending an empty object.
    ///
    /// Serialised rather than concatenated: SQL contains quotes, backslashes and newlines as a
    /// matter of course, and hand-built JSON breaks on the first statement written across two
    /// lines.
    /// </summary>
    public static string Compose(IEnumerable<DataSourceStatement> statements, int dataSourceId)
    {
        var mine = statements
            .Where(s => s.DataSourceId == dataSourceId && !s.Inactive)
            .ToList();

        if (mine.Count == 0) return null;

        // Ordered so the composed value is stable. The supervisor fingerprints startup values to
        // decide whether an adapter needs restarting, and dictionary ordering that varied between
        // reconciles would recycle a healthy connection every thirty seconds.
        return JsonConvert.SerializeObject(
            mine.OrderBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(s => s.Name, Value));
    }

    /// <summary>
    /// A bare SQL string, unless the statement carries the columns a receiver needs - in which
    /// case an object.
    ///
    /// Both shapes on purpose. A statement that is only ever queried composes to exactly the
    /// string it always did, so upgrading the host ahead of the adapters changes nothing for the
    /// statements they already run; only a polled statement takes the richer form, and only an
    /// adapter new enough to poll will ever be handed one.
    /// </summary>
    static object Value(DataSourceStatement statement) =>
        string.IsNullOrWhiteSpace(statement.CursorColumn) && string.IsNullOrWhiteSpace(statement.KeyColumn)
            ? statement.Sql
            : new Dictionary<string, string>
            {
                ["sql"] = statement.Sql,
                ["cursorColumn"] = statement.CursorColumn,
                ["keyColumn"] = statement.KeyColumn,
            };
}

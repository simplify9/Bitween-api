using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Bitween.MsSql;

/// <summary>
/// Builds an index on a table exchanges write to without stopping those writes, where the edition
/// allows it.
/// </summary>
/// <remarks>
/// ONLINE = ON keeps the table writable during the build, but only Enterprise (and Developer),
/// Azure SQL Database and Managed Instance have it; Standard refuses the statement outright. So the
/// edition is asked first, and the others build it as before, with the table locked for the build.
/// </remarks>
internal static class OnlineIndex
{
    /// <param name="definition">What follows <c>ON [table]</c>: the columns, and any WHERE.</param>
    public static void Create(MigrationBuilder migrationBuilder, string table, string name, string definition)
    {
        var create = $"CREATE INDEX [{name}] ON [{table}] {definition}".Replace("'", "''");
        migrationBuilder.Sql(
            $@"IF CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8)
                   EXEC('{create} WITH (ONLINE = ON)');
               ELSE
                   EXEC('{create}');");
    }
}

using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.DataSourceStatements;

/// <summary>
/// Adds a statement to a data source.
///
/// Gated on <c>data-source-statements.create</c>, NOT on <c>data-sources.edit</c>. That separation
/// is the whole reason this is an entity: writing a query and changing a database password are
/// different jobs, and before this they needed the same right.
/// </summary>
public class Create(BitweenDbContext dbContext, RequestContext requestContext,
    SW.Bitween.Services.DataSources.StatementValidator validator)
    : ICommandHandler<DataSourceStatementCreate, object>
{
    public async Task<object> Handle(DataSourceStatementCreate model)
    {
        var dataSourceId = model.DataSourceId;

        await requestContext.EnsurePermission(dbContext,
            Model.Permissions.DataSourceStatements.Create);

        var dataSource = await dbContext.Set<DataSource>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dataSourceId);

        if (dataSource == null)
            throw new SWNotFoundException($"DataSource with id '{dataSourceId}' was not found");

        // Only a relational source runs SQL. Refused rather than stored, because a statement on a
        // broker is configuration nothing will ever read - and silently keeping it is how people
        // conclude the feature is broken.
        if (dataSource.Kind != DataSourceKind.Relational)
            throw new SWException(
                $"'{dataSource.Name}' is a {dataSource.Kind} data source, and statements only mean "
                + "something to a Relational one.");

        await EnsureNameIsFree(dbContext, dataSourceId, model.Name, existingId: null);
        var checkedSql = await EnsureSqlIsValid(validator, dataSourceId, model.Sql);

        var entity = new DataSourceStatement
        {
            DataSourceId = dataSourceId,
            Name = model.Name.Trim(),
            Sql = model.Sql,
            CursorColumn = model.CursorColumn,
            KeyColumn = model.KeyColumn,
            Description = model.Description,
            WorkGroupId = model.WorkGroupId,
            Inactive = model.Inactive
        };

        dbContext.Add(entity);
        await dbContext.SaveChangesAsync();
        // The id, and whether the SQL was actually checked. Saying so matters because validation
        // is best-effort: when the adapter is not running there is nobody to ask, and a save that
        // reported nothing would look exactly like one that had been verified.
        return new { Id = entity.Id, Checked = checkedSql };
    }

    /// <summary>
    /// Refuses SQL the database itself will not accept, while the person who wrote it is still
    /// looking at it. The check prepares the statement - parsed and planned, never run.
    ///
    /// Silent when the adapter cannot answer: a connection that is down is a fact about the
    /// connection, and blocking someone from saving a fix because the thing they are fixing it
    /// for is broken would be exactly backwards.
    /// </summary>
    /// <returns>True when the database actually looked at it; false when nobody could be asked.</returns>
    internal static async Task<bool> EnsureSqlIsValid(
        SW.Bitween.Services.DataSources.StatementValidator validator, int dataSourceId, string sql)
    {
        var result = await validator.ValidateAsync(dataSourceId, sql);
        if (result.Checked && !result.Ok)
            throw new SWException($"The database will not accept this statement. {result.Error}");

        return result.Checked;
    }

    /// <summary>
    /// Case-insensitively unique within the data source. The database index enforces uniqueness but
    /// its collation is provider-specific, so the case rule is applied here - where it can also say
    /// which statement it collided with, rather than surfacing as a constraint violation.
    /// </summary>
    internal static async Task EnsureNameIsFree(BitweenDbContext dbContext, int dataSourceId,
        string name, int? existingId)
    {
        var trimmed = (name ?? "").Trim();

        var clash = await dbContext.Set<DataSourceStatement>().AsNoTracking()
            .Where(s => s.DataSourceId == dataSourceId)
            .Where(s => existingId == null || s.Id != existingId)
            .ToListAsync();

        if (clash.Any(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new SWException(
                $"This data source already has a statement called '{trimmed}'. Names are matched "
                + "without regard to case, because that is how the adapter resolves them.");
    }

    private class Validate : AbstractValidator<DataSourceStatementCreate>
    {
        public Validate()
        {
            RuleFor(i => i.DataSourceId).GreaterThan(0);
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            RuleFor(i => i.Sql).NotEmpty();
            RuleFor(i => i.Description).MaximumLength(1000);
        }
    }
}

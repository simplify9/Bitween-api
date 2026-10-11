using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.DataSourceStatements;

public class Update(BitweenDbContext dbContext, RequestContext requestContext,
    Services.DataSources.StatementValidator validator)
    : ICommandHandler<int, DataSourceStatementUpdate, object>
{
    public async Task<object> Handle(int key, DataSourceStatementUpdate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSourceStatements.Edit);

        var entity = await dbContext.Set<DataSourceStatement>().FirstOrDefaultAsync(s => s.Id == key);
        if (entity == null)
            throw new SWNotFoundException($"DataSourceStatement with id '{key}' was not found");

        await Create.EnsureNameIsFree(dbContext, entity.DataSourceId, model.Name, existingId: key);

        // A rename breaks every subscription naming the old one, and there is no way to fix that
        // from here - the subscription's properties are its own. So it is refused while anything
        // still points at it, and the message says what to change first.
        var renaming = !string.Equals(entity.Name, model.Name?.Trim(),
            System.StringComparison.OrdinalIgnoreCase);

        if (renaming)
        {
            var usage = await new Services.DataSources.StatementUsageReader(dbContext)
                .ForDataSourceAsync(entity.DataSourceId);

            if (usage.TryGetValue(entity.Name, out var users) && users.Count > 0)
                throw new SWException(
                    $"'{entity.Name}' cannot be renamed while {users.Count} subscription(s) name it: "
                    + string.Join(", ", users.Select(u => u.SubscriptionName).Distinct())
                    + ". Point them at the new name first, or add a statement and retire this one.");
        }

        // Only when it actually changed: re-checking untouched SQL would refuse a rename, or a
        // change of owner, because of a table someone dropped last week - a fault worth surfacing
        // but not here, and not as a block on an unrelated edit.
        // True when the SQL did not change: there was nothing to check, which is not the same as
        // "could not check" and should not warn as though it were.
        var checkedSql = true;
        if (!string.Equals(entity.Sql, model.Sql, System.StringComparison.Ordinal))
            checkedSql = await Create.EnsureSqlIsValid(validator, entity.DataSourceId, model.Sql);

        entity.Name = model.Name.Trim();
        entity.Sql = model.Sql;
        entity.CursorColumn = model.CursorColumn;
        entity.KeyColumn = model.KeyColumn;
        entity.Description = model.Description;
        entity.WorkGroupId = model.WorkGroupId;
        entity.Inactive = model.Inactive;

        await dbContext.SaveChangesAsync();
        return new { Id = entity.Id, Checked = checkedSql };
    }

    private class Validate : AbstractValidator<DataSourceStatementUpdate>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            RuleFor(i => i.Sql).NotEmpty();
            RuleFor(i => i.Description).MaximumLength(1000);
        }
    }
}

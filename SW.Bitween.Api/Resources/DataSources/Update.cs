using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DataSources;

public class Update(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, DataSourceUpdate, object>
{
    public async Task<object> Handle(int key, DataSourceUpdate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.Edit);

        Create.EnsureCeilingsAreUsable(model.SoftMemoryLimitMb, model.HardMemoryLimitMb,
            model.CpuPercentLimit, model.CpuLimitSamples);

        var entity = await dbContext.Set<DataSource>().FirstOrDefaultAsync(d => d.Id == key);
        if (entity == null)
            throw new SWNotFoundException($"DataSource with Id {key} not found");

        var nameTaken = await dbContext.Set<DataSource>()
            .AnyAsync(d => d.Name == model.Name && d.Id != key);
        if (nameTaken)
            throw new SWException($"A data source named '{model.Name}' already exists.");

        // Secrets came out of Get masked, so put them back from what is stored. A form that only
        // changed the prefetch must not overwrite the password with a row of dots.
        var keep = AdapterSecretProperties.MayKeepStoredSecrets(entity.AdapterId, model.AdapterId,
            entity.Properties, model.Properties ?? new Dictionary<string, string>());
        var properties = Secrets.Merge(keep ? entity.Properties : null, model.Properties);

        entity.Name = model.Name;
        entity.AdapterId = model.AdapterId;
        entity.Kind = Create.ParseKind(model.Kind);
        // Left out means unchanged. It used to mean Auto, so every client that didn't send it,
        // the admin UI included, put an Exclusive or PerNode source back to Auto on any save.
        if (!string.IsNullOrWhiteSpace(model.Placement))
            entity.Placement = Create.ParsePlacement(model.Placement);
        entity.Properties = properties;
        entity.SecretProperties = Secrets.Declare(properties, model.SecretProperties);
        entity.Inactive = model.Inactive;
        entity.DeduplicationWindowDays = model.DeduplicationWindowDays;
        entity.SoftMemoryLimitMb = model.SoftMemoryLimitMb;
        entity.HardMemoryLimitMb = model.HardMemoryLimitMb;
        entity.CpuPercentLimit = model.CpuPercentLimit;
        entity.CpuLimitSamples = model.CpuLimitSamples;

        await dbContext.SaveChangesAsync();

        // No cache to revoke and no adapter to restart from here: the supervisor reconciles against
        // these rows on its own loop, notices the fingerprint changed, and restarts the adapter.
        return null;
    }

    private class Validate : AbstractValidator<DataSourceUpdate>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            RuleFor(i => i.AdapterId).NotEmpty().MaximumLength(200);
            RuleFor(i => i.DeduplicationWindowDays).GreaterThanOrEqualTo(0);
            RuleFor(i => i.SoftMemoryLimitMb).GreaterThanOrEqualTo(0);
            RuleFor(i => i.HardMemoryLimitMb).GreaterThanOrEqualTo(0);
            RuleFor(i => i.SoftMemoryLimitMb)
                .LessThanOrEqualTo(i => i.HardMemoryLimitMb)
                .When(i => i.SoftMemoryLimitMb > 0 && i.HardMemoryLimitMb > 0)
                .WithMessage("The soft memory limit has to be at or below the hard limit, "
                           + "or it can never be reached.");
        }
    }
}

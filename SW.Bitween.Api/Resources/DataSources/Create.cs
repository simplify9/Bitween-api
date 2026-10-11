using System;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DataSources;

public class Create(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<DataSourceCreate, object>
{
    public async Task<object> Handle(DataSourceCreate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.Create);

        EnsureCeilingsAreUsable(model.SoftMemoryLimitMb, model.HardMemoryLimitMb,
            model.CpuPercentLimit, model.CpuLimitSamples);

        var nameTaken = await dbContext.Set<DataSource>()
            .AnyAsync(d => d.Name == model.Name);
        if (nameTaken)
            throw new SWException($"A data source named '{model.Name}' already exists.");

        // Nothing is stored behind a sentinel on a create, so any that arrives - from a data source
        // copied out of Get, say - is dropped rather than saved as the literal string.
        var properties = Secrets.Merge(null, model.Properties);

        var entity = new DataSource
        {
            Name = model.Name,
            AdapterId = model.AdapterId,
            Kind = ParseKind(model.Kind),
            Placement = ParsePlacement(model.Placement),
            Properties = properties,
            SecretProperties = Secrets.Declare(properties, model.SecretProperties),
            Inactive = model.Inactive,
            DeduplicationWindowDays = model.DeduplicationWindowDays,
            SoftMemoryLimitMb = model.SoftMemoryLimitMb,
            HardMemoryLimitMb = model.HardMemoryLimitMb,
            CpuPercentLimit = model.CpuPercentLimit,
            CpuLimitSamples = model.CpuLimitSamples
        };

        dbContext.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }

    /// <summary>
    /// Enforced here rather than only in the validator, because the validator runs in the HTTP
    /// pipeline and nothing else does: a caller reaching the handler another way - the supervisor's
    /// own tests, a future internal caller - would otherwise save a combination that can never
    /// work. A soft ceiling above the hard one is unreachable: the runtime fails the allocation
    /// first, so the graceful recycle it was configured for never happens.
    /// </summary>
    internal static void EnsureCeilingsAreUsable(int softMb, int hardMb,
        double cpuPercent = 0, int cpuSamples = 0)
    {
        if (softMb < 0 || hardMb < 0)
            throw new SWException("A memory ceiling cannot be negative. Use 0 for the host default.");

        if (cpuPercent < 0 || cpuSamples < 0)
            throw new SWException("A CPU ceiling cannot be negative. Use 0 for the host default.");

        // Above 100 is not a ceiling, because the figure is a share of the whole node - nothing can
        // ever exceed it, so the limit would silently never fire.
        if (cpuPercent > 100)
            throw new SWException(
                $"A CPU ceiling of {cpuPercent}% can never be reached: the figure is a share of the "
                + "whole node, so 100% is every core at once.");

        if (softMb > 0 && hardMb > 0 && softMb > hardMb)
            throw new SWException(
                $"The soft memory limit ({softMb} MB) has to be at or below the hard limit "
                + $"({hardMb} MB), or it can never be reached.");
    }

    /// <summary>
    /// Unparseable falls back to Auto rather than to a guess. Auto is the answer that follows from
    /// the kind, so a typo lands on the sensible default instead of pinning a database to one node
    /// or letting two nodes onto one queue.
    /// </summary>
    internal static DataSourcePlacement ParsePlacement(string placement) =>
        Enum.TryParse<DataSourcePlacement>(placement, ignoreCase: true, out var parsed)
            ? parsed
            : DataSourcePlacement.Auto;

    internal static DataSourceKind ParseKind(string kind) =>
        Enum.TryParse<DataSourceKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : DataSourceKind.Broker;

    private class Validate : AbstractValidator<DataSourceCreate>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            RuleFor(i => i.AdapterId).NotEmpty().MaximumLength(200);

            // Zero is meaningful - it turns deduplication off - so only a negative is rejected.
            RuleFor(i => i.DeduplicationWindowDays).GreaterThanOrEqualTo(0);

            // Zero means "leave the host default alone" for both.
            RuleFor(i => i.SoftMemoryLimitMb).GreaterThanOrEqualTo(0);
            RuleFor(i => i.HardMemoryLimitMb).GreaterThanOrEqualTo(0);

            // A soft ceiling above the hard one can never be reached: the runtime fails the
            // allocation first, so the recycle it was meant to trigger never happens.
            RuleFor(i => i.SoftMemoryLimitMb)
                .LessThanOrEqualTo(i => i.HardMemoryLimitMb)
                .When(i => i.SoftMemoryLimitMb > 0 && i.HardMemoryLimitMb > 0)
                .WithMessage("The soft memory limit has to be at or below the hard limit, "
                           + "or it can never be reached.");
        }
    }
}

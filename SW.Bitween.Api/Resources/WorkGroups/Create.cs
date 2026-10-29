using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Create(BitweenDbContext dbContext, RequestContext requestContext,IInfolinkCache _BitweenCache, IBroadcast _broadcast)
    : ICommandHandler<CreateWorkGroupModel, object>
{
    public async Task<object> Handle(CreateWorkGroupModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.WorkGroups.Create);
        await EnsureNameIsFree(dbContext, request.Name);

        var workgroup = new WorkGroup()
        {
            Name = request.Name,
            BusMessageName =  request.BusMessageName,
            Options = new WorkGroupOptions()
            {
                RabbitMqOptions = new ConsumerSettings
                {
                    Prefetch = request.Options?.RabbitMqOptions?.Prefetch,
                    Priority = request.Options?.RabbitMqOptions?.Priority,
                    Concurrency = request.Options?.RabbitMqOptions?.Concurrency
                }
            }
        };
        dbContext.Add(workgroup);
        await dbContext.SaveChangesAsync();
        await _BitweenCache.BroadcastRevoke();
        await _broadcast.RefreshConsumers();
        return new
        {
            workgroup.Id
        };
    }

    /// <summary>
    /// Work groups are told apart by name everywhere but the database: the pickers, bitween apply.
    /// Two applies running at once each made "Acme ERP" in the lab, split its subscriptions between
    /// the two, and every apply after refused to go on until one was removed by hand.
    /// </summary>
    internal static async Task EnsureNameIsFree(BitweenDbContext dbContext, string name, int? except = null)
    {
        var wanted = name?.Trim().ToLower();
        if (string.IsNullOrEmpty(wanted)) return;
        if (await dbContext.Set<WorkGroup>().AnyAsync(w => w.Id != except && w.Name.Trim().ToLower() == wanted))
            throw new SWValidationException("Name", $"There's already a work group named '{name.Trim()}'.");
    }

    private class Validate : AbstractValidator<CreateWorkGroupModel>
    {
        public Validate()
        {
            RuleFor(i => i.BusMessageName)
                .Matches("^\\S+$")
                .When(i => !string.IsNullOrEmpty(i.BusMessageName))
                .WithMessage("Bus message name cannot contain spaces.");
            RuleFor(i => i.Options.RabbitMqOptions.Concurrency)
                .GreaterThan((ushort)0)
                .When(i => i.Options?.RabbitMqOptions?.Concurrency != null)
                .WithMessage("Parallel exchanges must be at least 1.");
        }
    }
}
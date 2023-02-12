using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using Document = SW.Infolink.Domain.Document;

namespace SW.Infolink.Resources.Subscriptions;

[HandlerName("trail")]
public class GetTrail : IQueryHandler<SearchSubscriptionTrailModel>
{
    private readonly InfolinkDbContext dbContext;

    public GetTrail(InfolinkDbContext dbContext)
    {
        this.dbContext = dbContext;
    }


    public async Task<object> Handle(SearchSubscriptionTrailModel request)
    {
        request.Limit ??= 20;
        request.Offset ??= 0;
        var trails = dbContext.Set<SubscriptionTrail>()
            .Where(i => i.SubscriptionId == request.SubscriptionId)
            .Select(t => new SubscriptionTrailModel
            {
                Id = t.Id,
                CreatedOn = t.CreatedOn,
                Code = t.Code.ToString(),
                SubscriptionId = t.SubscriptionId,
                CreatedBy = t.CreatedBy,
                StateAfter = t.StateAfter,
                StateBefore = t.StateBefore
            })
            .AsNoTracking();

        var count = await trails.CountAsync();
        var data = await trails
            .OrderByDescending(i => i.CreatedOn)
            .Skip(request.Offset.Value)
            .Take(request.Limit.Value)
            .ToListAsync();

        return new SearchyResponse<SubscriptionTrailModel>
        {
            TotalCount = count,
            Result = data
        };
    }

    private class Validate : AbstractValidator<SearchSubscriptionTrailModel>
    {
        public Validate()
        {
            RuleFor(i => i.SubscriptionId).NotEmpty();
        }
    }
}
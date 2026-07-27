using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Document = SW.Bitween.Domain.Document;

namespace SW.Bitween.Resources.Documents;

[HandlerName("trail")]
public class GetTrail : IQueryHandler<SearchDocumentTrailModel,object>
{
    private readonly BitweenDbContext dbContext;
    private readonly RequestContext requestContext;

    public GetTrail(BitweenDbContext dbContext, RequestContext requestContext)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
    }


    public async Task<object> Handle(SearchDocumentTrailModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.View);

        request.Limit ??= 20;
        request.Offset ??= 0;
        var trails = dbContext.Set<DocumentTrail>()
            .Where(i => i.DocumentId == request.DocumentId)
            .Select(t => new DocumentTrailModel
            {
                Id = t.Id,
                CreatedOn = t.CreatedOn,
                Code = t.Code.ToString(),
                DocumentId = t.DocumentId,
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

        return new SearchyResponse<DocumentTrailModel>
        {
            TotalCount = count,
            Result = data
        };
    }

    private class Validate : AbstractValidator<SearchDocumentTrailModel>
    {
        public Validate()
        {
            RuleFor(i => i.DocumentId).NotEmpty();
        }
    }
}
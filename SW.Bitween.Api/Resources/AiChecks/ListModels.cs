using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.AiChecks;

/// <summary>
/// The chat models the provider in these AI settings lists, for Settings to offer as choices.
/// Null models for Azure OpenAI, whose deployment names only the Azure portal shows.
/// </summary>
[HandlerName("models")]
public class ListModels(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, AiModels models)
    : ICommandHandler<AiCandidate, object>
{
    public async Task<object> Handle(AiCandidate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.Edit);
        return new { Models = await models.Models(request.With(options.Ai)) };
    }
}

using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.AiChecks;

/// <summary>
/// Calls the model these AI settings name with a small request, before they're saved, so a wrong
/// key, model or address shows in Settings rather than in the first real use. Changes nothing.
/// </summary>
/// <remarks>Needs Settings edit: it can send the saved key, as saving can.</remarks>
public class Check(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, AiModels models)
    : ICommandHandler<AiCandidate, object>
{
    public async Task<object> Handle(AiCandidate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.Edit);
        var ai = request.With(options.Ai);
        var took = await models.Check(ai);
        return new { Model = ai.ResolvedModel, Milliseconds = (int)took.TotalMilliseconds };
    }
}

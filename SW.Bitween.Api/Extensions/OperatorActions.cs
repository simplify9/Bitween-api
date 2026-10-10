using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public static class OperatorActions
{
    /// <summary>Adds an operator action to be saved with the caller's next save.</summary>
    public static void Record(this BitweenDbContext dbContext, RequestContext requestContext,
        string action, string target, string detail = null) =>
        dbContext.Add(new OperatorAction(action, target, detail, requestContext.GetNameIdentifier()));

    /// <summary>Records and saves at once, for an action that saves nothing else of its own.</summary>
    public static async Task RecordNowAsync(this BitweenDbContext dbContext, RequestContext requestContext,
        string action, string target, string detail = null)
    {
        dbContext.Record(requestContext, action, target, detail);
        await dbContext.SaveChangesAsync();
    }
}

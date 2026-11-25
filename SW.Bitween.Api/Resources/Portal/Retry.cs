using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// Retries one of their failed exchanges with the subscription's current settings, so a mapping or
/// detail they just fixed is used. Only the latest attempt, as our team's retry, and not while a
/// retry policy has one scheduled.
/// </summary>
[HandlerName("retry")]
public class Retry(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
    : ICommandHandler<PortalRetry, object>
{
    public async Task<object> Handle(PortalRetry request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesOperate);
        // Someone else's exchange is simply not there for them.
        var xchange = await dbContext.Set<Xchange>()
                          .SingleOrDefaultAsync(x => x.Id == request.XchangeId && x.PartnerId == caller.PartnerId)
                      ?? throw new SWNotFoundException(request.XchangeId);

        var result = await dbContext.Set<XchangeResult>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == xchange.Id);
        if (result is null || result.Success)
            throw new SWValidationException("NOT_FAILED", "Only a failed exchange can be retried.");
        if (await dbContext.Set<DelayedRetry>().AnyAsync(d => d.Id == xchange.Id))
            throw new SWValidationException("AUTO_RETRY_SCHEDULED",
                "This exchange is already scheduled to be tried again. It runs on its own shortly.");
        if (await dbContext.Set<XchangeRetryClaim>().AnyAsync(c => c.Id == xchange.Id) ||
            await dbContext.Set<Xchange>().AnyAsync(x => x.RetryFor == xchange.Id))
            throw new SWValidationException("ALREADY_RETRIED", "This exchange was already retried. Retry its latest attempt instead.");

        var subscription = await dbContext.Subscriptions().FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId)
                           ?? throw new SWValidationException("SUBSCRIPTION_NOT_FOUND",
                               "This exchange can't be retried: what it ran for no longer exists.");

        var input = await xchangeService.GetFile(xchange, XchangeFileType.Input);
        await xchangeService.CreateXchange(subscription, xchange, new XchangeFile(input, xchange.InputName),
            manualRetry: true);
        dbContext.Record(requestContext, OperatorAction.Retry, xchange.Id, "from the partner portal");
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another retry of the same exchange was saved a moment before this one.
            if (await dbContext.Set<XchangeRetryClaim>().AsNoTracking().AnyAsync(c => c.Id == xchange.Id))
                throw new SWValidationException("ALREADY_RETRIED",
                    "This exchange was retried a moment ago. Retry its latest attempt instead.");
            throw;
        }

        // The claim the retry made names the exchange it made.
        var retryId = await dbContext.Set<XchangeRetryClaim>().AsNoTracking()
            .Where(c => c.Id == xchange.Id).Select(c => c.RetryXchangeId).SingleAsync();
        return new PortalRetried { XchangeId = retryId };
    }

    private class Validate : AbstractValidator<PortalRetry>
    {
        public Validate() => RuleFor(i => i.XchangeId).NotEmpty().MaximumLength(50);
    }
}

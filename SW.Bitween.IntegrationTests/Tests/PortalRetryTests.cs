using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A partner's person retries their own failed exchanges from the portal: only theirs, only failed,
/// only the latest attempt, and only with the permission to.
/// </summary>
[Collection("Bitween")]
public class PortalRetryTests(BitweenFixture fixture)
{
    private record World(int Mine, int Other, int Retrier, int Viewer, string MyFailed, string MyDelivered, string TheirFailed);

    private async Task<World> SetUpAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var doc = new Document(null, $"Portal retry {Guid.NewGuid():N}", DocumentFormat.Json);
        var mine = new Partner($"Retry mine {Guid.NewGuid():N}");
        var other = new Partner($"Retry other {Guid.NewGuid():N}");
        db.AddRange(doc, mine, other);
        await db.SaveChangesAsync();
        var sub = new Subscription($"Portal retry {Guid.NewGuid():N}", doc.Id) { Inactive = false };
        db.Add(sub);
        await db.SaveChangesAsync();

        async Task<string> Exchange(Partner partner, string exception)
        {
            var x = await xs.CreateXchange(sub, new XchangeFile("{}"), gatewayPartner: partner);
            await db.SaveChangesAsync();
            db.Add(new XchangeResult(x.Id, null, null, exception: exception));
            await db.SaveChangesAsync();
            return x.Id;
        }

        async Task<int> Person(params string[] permissions)
        {
            var role = new Role($"Retry role {Guid.NewGuid():N}", null, permissions, forPartners: true);
            db.Add(role);
            var account = new Account("Retrier", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, mine.Id);
            db.Add(account);
            await db.SaveChangesAsync();
            db.Add(new AccountRoleLink(account.Id, role.Id));
            await db.SaveChangesAsync();
            return account.Id;
        }

        return new World(mine.Id, other.Id,
            await Person(Permissions.Portal.ExchangesView, Permissions.Portal.ExchangesOperate),
            await Person(Permissions.Portal.ExchangesView),
            await Exchange(mine, "boom"), await Exchange(mine, null), await Exchange(other, "boom"));
    }

    private async Task<object> RetryAs(int person, string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        return await ActivatorUtilities.CreateInstance<Resources.Portal.Retry>(scope.ServiceProvider)
            .Handle(new PortalRetry { XchangeId = xchangeId });
    }

    [Fact]
    public async Task Retries_their_failed_exchange_once_with_their_partner_kept()
    {
        var w = await SetUpAsync();
        var retried = (PortalRetried)await RetryAs(w.Retrier, w.MyFailed);

        await using (var scope = fixture.CreateScope())
        {
            var retry = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>().AsNoTracking()
                .SingleAsync(x => x.Id == retried.XchangeId);
            Assert.Equal(w.MyFailed, retry.RetryFor);
            Assert.Equal(w.Mine, retry.PartnerId);
        }

        // Only the latest attempt is retried again.
        await Assert.ThrowsAsync<SWValidationException>(() => RetryAs(w.Retrier, w.MyFailed));
    }

    [Fact]
    public async Task Refuses_one_delivered_someone_elses_or_without_the_permission()
    {
        var w = await SetUpAsync();
        await Assert.ThrowsAsync<SWValidationException>(() => RetryAs(w.Retrier, w.MyDelivered));
        await Assert.ThrowsAsync<SWNotFoundException>(() => RetryAs(w.Retrier, w.TheirFailed));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => RetryAs(w.Viewer, w.MyFailed));
    }
}

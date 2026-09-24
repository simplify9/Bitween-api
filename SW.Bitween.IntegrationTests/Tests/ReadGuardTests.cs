using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Reads are guarded as well as writes, and in both directions: a narrow role must not read the
/// lists of areas it doesn't hold, yet every picker in the app needs id/name pairs from areas the
/// person may never browse. Lookup mode is what serves the pickers, so it stays open.
/// </summary>
[Collection("Bitween")]
public class ReadGuardTests(BitweenFixture fixture)
{
    /// <summary>Signs in as a fresh account whose only grant is reading information types.</summary>
    private static async Task SignInAsInformationTypeReader(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var role = new Role($"docs-reader-{Guid.NewGuid():N}", "Reads information types only",
            [Permissions.Documents.View]);
        db.Set<Role>().Add(role);
        var account = new Account("Docs Reader", $"docs-reader-{Guid.NewGuid():N}@test.local", "hash",
            AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, role.Id));
        await db.SaveChangesAsync();

        scope.As(account.Id);
    }

    private static Func<Task<object>> Search<THandler>(AsyncServiceScope scope, bool lookup = false)
        where THandler : ISearchyHandler =>
        () => ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider)
            .Handle(new SearchyRequest(), lookup);

    [Fact]
    public async Task One_view_permission_reads_its_own_list_and_no_other()
    {
        await using var scope = fixture.CreateScope();
        await SignInAsInformationTypeReader(scope);

        // The one area held is readable.
        await Search<Resources.Documents.Search>(scope)();

        var refused = new Dictionary<string, Func<Task<object>>>
        {
            ["partners"] = Search<Resources.Partners.Search>(scope),
            ["exchanges"] = Search<Resources.Xchanges.Search>(scope),
            ["subscriptions"] = Search<Resources.Subscriptions.Search>(scope),
            ["notifiers"] = Search<Resources.Notifiers.Search>(scope),
            ["API gateways"] = Search<Resources.ApiGateways.Search>(scope),
            ["bus gateways"] = Search<Resources.BusGateways.Search>(scope),
            ["retry policies"] = Search<Resources.RetryPolicies.Search>(scope),
            ["global values"] = Search<Resources.GlobalAdapterValuesSets.Search>(scope),
            ["scheduled retries"] = Search<Resources.DelayedRetries.Search>(scope),
            ["work groups"] = () => ActivatorUtilities.CreateInstance<Resources.WorkGroups.Search>(
                scope.ServiceProvider).Handle(new SearchWorkGroupModel()),
            ["queue health"] = () => ActivatorUtilities.CreateInstance<Resources.Ops.Summary>(
                scope.ServiceProvider).Handle(),
        };

        foreach (var (area, read) in refused)
        {
            var denied = await Record.ExceptionAsync(read);
            Assert.True(denied is SWUnauthorizedException, $"{area} should be refused, got {denied?.GetType().Name ?? "a result"}");
        }
    }

    [Fact]
    public async Task Lookup_mode_stays_readable_so_pickers_work_for_any_role()
    {
        await using var scope = fixture.CreateScope();
        await SignInAsInformationTypeReader(scope);

        await Search<Resources.Partners.Search>(scope, lookup: true)();
        await Search<Resources.Subscriptions.Search>(scope, lookup: true)();
        await Search<Resources.RetryPolicies.Search>(scope, lookup: true)();
        await ActivatorUtilities.CreateInstance<Resources.Accounts.Search>(scope.ServiceProvider)
            .Handle(new SearchMembersModel { Lookup = true });
    }
}

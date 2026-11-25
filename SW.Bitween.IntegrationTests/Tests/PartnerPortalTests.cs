using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
/// A partner's people see their partner's data and nothing else: scoped in the portal's handlers,
/// fenced off from every other endpoint, and never given a role from our own side.
/// </summary>
[Collection("Bitween")]
public class PartnerPortalTests(BitweenFixture fixture)
{
    private static string Unique(string label) => $"{label} {Guid.NewGuid():N}";

    private record World(Partner Mine, Partner Theirs, Subscription MySubscription, Subscription TheirSubscription);

    private static async Task<World> SetUpAsync(BitweenDbContext db)
    {
        var doc = new Document(null, Unique("Portal doc"), DocumentFormat.Json);
        var mine = new Partner(Unique("Portal mine"));
        var theirs = new Partner(Unique("Portal theirs"));
        db.AddRange(doc, mine, theirs);
        await db.SaveChangesAsync();

        var mySubscription = new Subscription(Unique("Portal my sub"), doc.Id, SubscriptionType.Internal, mine.Id);
        var theirSubscription = new Subscription(Unique("Portal their sub"), doc.Id, SubscriptionType.Internal, theirs.Id);
        db.AddRange(mySubscription, theirSubscription);
        await db.SaveChangesAsync();
        return new World(mine, theirs, mySubscription, theirSubscription);
    }

    private static async Task<string> ExchangeAsync(BitweenDbContext db, Subscription subscription, Partner partner,
        string exception = null)
    {
        var xchange = new Xchange(subscription, new XchangeFile("{}"), gatewayPartner: partner);
        db.Add(xchange);
        await db.SaveChangesAsync();
        db.Add(new XchangeResult(xchange.Id, null, null, exception: exception));
        await db.SaveChangesAsync();
        return xchange.Id;
    }

    private static async Task<int> PartnerRoleAsync(BitweenDbContext db, params string[] permissions)
    {
        var role = new Role(Unique("Portal role"), null, permissions, forPartners: true);
        db.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task<int> AccountAsync(BitweenDbContext db, int? partnerId, params int[] roleIds)
    {
        var account = new Account("Portal person", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, partnerId);
        db.Add(account);
        await db.SaveChangesAsync();
        foreach (var roleId in roleIds)
            db.Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task A_partner_sees_only_its_own_exchanges()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db);
        var mine = await ExchangeAsync(db, world.MySubscription, world.Mine);
        var theirs = await ExchangeAsync(db, world.TheirSubscription, world.Theirs);

        var role = await PartnerRoleAsync(db, Permissions.Portal.ExchangesView);
        scope.As(await AccountAsync(db, world.Mine.Id, role));
        var handler = ActivatorUtilities.CreateInstance<Resources.Portal.Xchanges>(scope.ServiceProvider);

        var page = (PortalXchangePage)await handler.Handle(new PortalXchangeSearch { Size = 100 });

        var ids = page.Result.Select(r => r.Id).ToList();
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);
    }

    [Fact]
    public async Task Errors_and_files_need_their_own_permissions()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db);
        var failed = await ExchangeAsync(db, world.MySubscription, world.Mine, exception: "Connection refused to 10.0.0.5");

        // A scope each, as each request has its own: a request context is signed in once.
        async Task<PortalXchangeRow> RowAs(params string[] permissions)
        {
            var account = await AccountAsync(db, world.Mine.Id, await PartnerRoleAsync(db, permissions));
            await using var request = fixture.CreateScope();
            request.As(account);
            var handler = ActivatorUtilities.CreateInstance<Resources.Portal.Xchanges>(request.ServiceProvider);
            var page = (PortalXchangePage)await handler.Handle(new PortalXchangeSearch { Status = "failed", Size = 100 });
            return page.Result.Single(r => r.Id == failed);
        }

        var bare = await RowAs(Permissions.Portal.ExchangesView);
        Assert.Equal("failed", bare.Status);
        Assert.Null(bare.Error);
        Assert.Null(bare.Files);

        var full = await RowAs(Permissions.Portal.ExchangesView, Permissions.Portal.ExchangeErrorsView,
            Permissions.Portal.ExchangeFilesView);
        Assert.Equal("Connection refused to 10.0.0.5", full.Error);
        Assert.Contains(full.Files!, f => f.Kind == "input");
    }

    [Fact]
    public async Task Our_own_administrators_are_refused_by_the_portal()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // An administrator holds the portal keys - that is what lets them hand out partner roles -
        // but has no partner, so there is nobody's data for the portal to show them.
        scope.As(await AccountAsync(db, null, Role.AdministratorId));
        var handler = ActivatorUtilities.CreateInstance<Resources.Portal.Xchanges>(scope.ServiceProvider);

        var refused = await Assert.ThrowsAsync<SWUnauthorizedException>(() => handler.Handle(new PortalXchangeSearch()));
        Assert.Contains("NOT_A_PARTNER_ACCOUNT", refused.Message);
    }

    [Fact]
    public async Task A_partner_role_edited_to_hold_more_still_grants_only_portal_keys()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db);

        // Past the role validation, as a row edited in the database would be.
        var role = await PartnerRoleAsync(db, Permissions.Portal.ExchangesView);
        var stored = await db.Set<Role>().FindAsync(role);
        stored!.Update(stored.Name, null, [Permissions.Portal.ExchangesView, Permissions.Partners.View]);
        await db.SaveChangesAsync();

        var account = await AccountAsync(db, world.Mine.Id, role, Role.AdministratorId);
        var granted = await RequestContextExtensions.GetPermissionsOf(db, account);

        Assert.Equal([Permissions.Portal.ExchangesView], granted.ToList());
    }

    [Fact]
    public async Task Roles_and_accounts_stay_on_their_own_side()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db);
        scope.Superuser();

        var createRole = ActivatorUtilities.CreateInstance<Resources.Roles.Create>(scope.ServiceProvider);
        var mixed = await Assert.ThrowsAsync<SWValidationException>(() => createRole.Handle(new RoleCreate
        {
            Name = Unique("Mixed"), ForPartners = true,
            Permissions = [Permissions.Portal.ExchangesView, Permissions.Partners.View]
        }));
        Assert.Contains("PERMISSIONS_DONT_FIT_ROLE", mixed.Message);
        await Assert.ThrowsAsync<SWValidationException>(() => createRole.Handle(new RoleCreate
        {
            Name = Unique("Staff with portal"), Permissions = [Permissions.Portal.ExchangesView]
        }));

        var partnerRole = (int)await createRole.Handle(new RoleCreate
        {
            Name = Unique("Partner ok"), ForPartners = true, Permissions = [Permissions.Portal.ExchangesView]
        });

        var createAccount = ActivatorUtilities.CreateInstance<Resources.Accounts.Create>(scope.ServiceProvider);
        var staffRoleOnPartner = await Assert.ThrowsAsync<SWValidationException>(() => createAccount.Handle(new CreateAccountModel
        {
            Name = "P", Email = $"{Guid.NewGuid():N}@test.local", Invite = true,
            PartnerId = world.Mine.Id, RoleIds = [Role.ViewerId]
        }));
        Assert.Contains("ROLE_DOESNT_SUIT_ACCOUNT", staffRoleOnPartner.Message);

        var partnerPerson = (int)await createAccount.Handle(new CreateAccountModel
        {
            Name = "P", Email = $"{Guid.NewGuid():N}@test.local", Invite = true,
            PartnerId = world.Mine.Id, RoleIds = [partnerRole]
        });

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWValidationException>(() =>
            setRoles.Handle(partnerPerson, new SetAccountRolesModel { RoleIds = [Role.AdministratorId] }));

        var staff = await AccountAsync(db, null, Role.ViewerId);
        await Assert.ThrowsAsync<SWValidationException>(() =>
            setRoles.Handle(staff, new SetAccountRolesModel { RoleIds = [partnerRole] }));
    }

    [Fact]
    public async Task A_partner_with_portal_users_cant_be_deleted()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Portal delete"));
        db.Add(partner);
        await db.SaveChangesAsync();
        await AccountAsync(db, partner.Id);
        scope.Superuser();

        var delete = ActivatorUtilities.CreateInstance<Resources.Partners.Delete>(scope.ServiceProvider);
        var refused = await Assert.ThrowsAsync<SWValidationException>(() => delete.Handle(partner.Id));
        Assert.Contains("portal user", refused.Message);
    }

    [Theory]
    [InlineData("/api/partners", false)]
    [InlineData("/api/xchanges", false)]
    [InlineData("/api/accounts", false)]
    [InlineData("/mcp", false)]
    [InlineData("/api/portal/xchanges", true)]
    [InlineData("/api/accounts/profile", true)]
    [InlineData("/api/accounts/login/", true)]
    public async Task The_gate_keeps_a_partner_inside_the_portal(string path, bool passes)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db);
        var partnerPerson = await AccountAsync(db, world.Mine.Id);
        var staff = await AccountAsync(db, null, Role.ViewerId);

        async Task<bool> Passes(int accountId)
        {
            var reached = false;
            var gate = new PartnerPortalGate(_ => { reached = true; return Task.CompletedTask; });
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())], "test"))
            };
            context.Request.Path = path;
            await gate.InvokeAsync(context);
            Assert.Equal(reached ? 200 : 403, context.Response.StatusCode);
            return reached;
        }

        Assert.Equal(passes, await Passes(partnerPerson));
        // Our own people are never stopped here.
        Assert.True(await Passes(staff));
        // A token whose account is gone gets no further than a partner's.
        Assert.Equal(passes, await Passes(int.MaxValue - Random.Shared.Next(1000)));
    }

    [Fact]
    public async Task The_built_in_partner_viewer_is_made_once_and_grants_every_portal_view()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BitweenDbContext>>();

        await BuiltInPartnerRoles.EnsureAsync(db, logger);
        await BuiltInPartnerRoles.EnsureAsync(db, logger);

        var viewer = Assert.Single(await db.Set<Role>().AsNoTracking()
            .Where(r => r.PresetKey == "partner-viewer").ToListAsync());
        Assert.True(viewer.IsSystem);
        Assert.True(viewer.ForPartners);

        var world = await SetUpAsync(db);
        var person = await AccountAsync(db, world.Mine.Id, viewer.Id);
        Assert.Equal(PermissionCatalog.PortalKeys, await RequestContextExtensions.GetPermissionsOf(db, person));

        // Built in, so nobody edits it.
        scope.Superuser();
        var update = ActivatorUtilities.CreateInstance<Resources.Roles.Update>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWValidationException>(() => update.Handle(viewer.Id,
            new RoleUpdate { Name = viewer.Name, Permissions = [Permissions.Portal.ExchangesView] }));
    }

    [Fact]
    public async Task Every_account_holds_one_role()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        scope.Superuser();
        var staff = await AccountAsync(db, null, Role.ViewerId);

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);
        var two = await Assert.ThrowsAsync<SWValidationException>(() =>
            setRoles.Handle(staff, new SetAccountRolesModel { RoleIds = [Role.ViewerId, Role.MemberId] }));
        Assert.Contains("ONE_ROLE", two.Message);
        await Assert.ThrowsAsync<SWValidationException>(() =>
            setRoles.Handle(staff, new SetAccountRolesModel { RoleIds = [] }));

        var createAccount = ActivatorUtilities.CreateInstance<Resources.Accounts.Create>(scope.ServiceProvider);
        var email = $"{Guid.NewGuid():N}@test.local";
        await Assert.ThrowsAsync<SWValidationException>(() => createAccount.Handle(new CreateAccountModel
        {
            Name = "Two", Email = email, Invite = true, RoleIds = [Role.ViewerId, Role.MemberId]
        }));
        // Refused before anything was made.
        Assert.False(await db.Set<Account>().AnyAsync(a => a.Email == email));

        var createService = ActivatorUtilities.CreateInstance<Resources.ServiceAccounts.Create>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWValidationException>(() => createService.Handle(new CreateServiceAccountModel
        {
            Name = Unique("svc"), RoleIds = [Role.ViewerId, Role.MemberId]
        }));
    }
}

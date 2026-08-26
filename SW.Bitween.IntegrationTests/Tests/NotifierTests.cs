using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Notifiers — the alerts sent when an exchange succeeds or fails.
/// </summary>
/// <remarks>
/// A notifier points at the integrations it watches rather than the other way round, so nothing
/// holds a foreign key to it and deleting one is always allowed. That makes the interesting cases
/// the quiet ones: a notifier whose watch list or handler settings are dropped on an edit goes on
/// existing while silently alerting on nothing.
/// </remarks>
[Collection("Bitween")]
public class NotifierTests
{
    private readonly BitweenFixture _fixture;

    public NotifierTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> Create(string name)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Notifiers.Create>(scope.ServiceProvider);
        return (int)await handler.Handle(new NotifierCreate { Name = name });
    }

    private async Task Update(int id, NotifierUpdate model)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Notifiers.Update>(scope.ServiceProvider);
        await handler.Handle(id, model);
    }

    private async Task<Notifier> Stored(int id)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Notifier>().AsNoTracking().SingleAsync(n => n.Id == id);
    }

    [Fact]
    public async Task A_new_notifier_starts_active()
    {
        var id = await Create(Unique("Fresh notifier"));

        // Unlike an integration, which is born inactive because it is created half-configured.
        // A notifier has nothing to configure before it can run.
        Assert.False((await Stored(id)).Inactive);
    }

    [Fact]
    public async Task Updating_with_no_handler_properties_clears_them_rather_than_failing()
    {
        var id = await Create(Unique("Had settings"));
        await Update(id, new NotifierUpdate
        {
            Name = Unique("Had settings"),
            HandlerId = "native:smtp",
            HandlerProperties = [new KeyAndValue { Key = "Host", Value = "smtp.example.com" }],
        });
        Assert.Single((await Stored(id)).HandlerProperties);

        // An absent list means none, as it does for a document's promoted properties. Left
        // implicit this threw ArgumentNullException — a 500 for a well-formed request.
        await Update(id, new NotifierUpdate { Name = Unique("No settings"), HandlerId = "native:smtp" });

        Assert.Empty((await Stored(id)).HandlerProperties);
    }

    [Fact]
    public async Task An_edit_that_names_no_handler_keeps_the_one_it_had()
    {
        var id = await Create(Unique("Keeps handler"));
        await Update(id, new NotifierUpdate { Name = Unique("With handler"), HandlerId = "native:smtp" });

        await Update(id, new NotifierUpdate { Name = Unique("Renamed"), HandlerId = null });

        // Losing the handler would leave a notifier that looks configured and can deliver nothing.
        Assert.Equal("native:smtp", (await Stored(id)).HandlerId);
    }

    [Fact]
    public async Task The_watch_list_is_replaced_by_what_the_edit_sends()
    {
        var id = await Create(Unique("Watcher"));
        int first, second;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(null, Unique("Notifier doc"), DocumentFormat.Json);
            db.Set<Document>().Add(document);
            await db.SaveChangesAsync();

            var a = new Subscription(Unique("Watched A"), document.Id);
            var b = new Subscription(Unique("Watched B"), document.Id);
            db.Set<Subscription>().AddRange(a, b);
            await db.SaveChangesAsync();
            first = a.Id;
            second = b.Id;
        }

        await Update(id, new NotifierUpdate
        {
            Name = Unique("Watcher"),
            HandlerId = "native:smtp",
            RunOnSubscriptions = [new NotifierSubscription { Id = first }, new NotifierSubscription { Id = second }],
        });
        Assert.Equal([first, second], (await Stored(id)).RunOnSubscriptions);

        await Update(id, new NotifierUpdate
        {
            Name = Unique("Watcher"),
            HandlerId = "native:smtp",
            RunOnSubscriptions = [new NotifierSubscription { Id = second }],
        });

        // The list is a replacement, not an addition — otherwise an integration can never be
        // taken off a notifier once added.
        Assert.Equal([second], (await Stored(id)).RunOnSubscriptions);
    }

    [Fact]
    public async Task Deleting_a_notifier_takes_its_watch_list_with_it()
    {
        var id = await Create(Unique("Doomed"));
        int subscriptionId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(null, Unique("Doomed doc"), DocumentFormat.Json);
            db.Set<Document>().Add(document);
            await db.SaveChangesAsync();
            var subscription = new Subscription(Unique("Still wanted"), document.Id);
            db.Set<Subscription>().Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }

        await Update(id, new NotifierUpdate
        {
            Name = Unique("Doomed"),
            HandlerId = "native:smtp",
            RunOnSubscriptions = [new NotifierSubscription { Id = subscriptionId }],
        });

        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.Notifiers.Delete>(scope.ServiceProvider);
            await handler.Handle(id);
        }

        await using var check = _fixture.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await checkDb.Set<Notifier>().AnyAsync(n => n.Id == id));

        // The watch list is the notifier's own column, so it goes with it — but the integration
        // it named is configuration in its own right and must survive.
        Assert.True(await checkDb.Set<Subscription>().AnyAsync(s => s.Id == subscriptionId));
    }

    [Fact]
    public async Task A_viewer_cannot_create_or_delete_a_notifier()
    {
        var id = await Create(Unique("Guarded notifier"));

        await using var scope = _fixture.CreateScope();
        await scope.AsNewViewer(Unique("notifier-viewer"));

        var create = ActivatorUtilities.CreateInstance<Resources.Notifiers.Create>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            create.Handle(new NotifierCreate { Name = "Should not exist" }));

        var delete = ActivatorUtilities.CreateInstance<Resources.Notifiers.Delete>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => delete.Handle(id));
    }
}

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Mappers;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Four handlers took a RequestContext and never asked it anything.
///
/// Injecting the thing that checks permissions and then not calling it looks exactly like a
/// handler that does check: the dependency is declared, the reader's eye stops there. The
/// compiler could not see it either, because an assigned-but-unread field is not a warning — it
/// only surfaced once these became primary constructor parameters and the unused ones got named.
///
/// So a viewer could rename the subscription categories everyone else files by, delete them, and
/// run the mapper preview, which reads every GlobalAdapterValuesSet there is.
///
/// These tests are here so that stays fixed. They assert refusal, not the absence of a call:
/// deleting the EnsurePermission line again fails them.
/// </summary>
[Collection("Bitween")]
public class UnguardedEndpointTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_viewer_cannot_create_a_subscription_category()
    {
        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer(Unique("cat-create"));

        var create = ActivatorUtilities.CreateInstance<Resources.SubscriptionCategories.Create>(
            scope.ServiceProvider);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            create.Handle(new CreateSubscriptionCategoryModel
            {
                Code = Unique("SHOULD-NOT-EXIST"),
                Description = "Created by someone with no permission to create it"
            }));
    }

    [Fact]
    public async Task A_viewer_cannot_rename_a_subscription_category()
    {
        var id = await CategoryAsAdmin();

        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer(Unique("cat-edit"));

        var update = ActivatorUtilities.CreateInstance<Resources.SubscriptionCategories.Update>(
            scope.ServiceProvider);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            update.Handle(id, new CreateSubscriptionCategoryModel
            {
                Code = Unique("RENAMED"),
                Description = "Renamed by someone with no permission to rename it"
            }));
    }

    [Fact]
    public async Task A_viewer_cannot_delete_a_subscription_category()
    {
        var id = await CategoryAsAdmin();

        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer(Unique("cat-delete"));

        var delete = ActivatorUtilities.CreateInstance<Resources.SubscriptionCategories.Delete>(
            scope.ServiceProvider);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            delete.Handle(id, new DeleteSubscriptionCategoryModel()));
    }

    /// <summary>
    /// The worst of the four: the preview loads every GlobalAdapterValuesSet in the deployment,
    /// which is where shared configuration lives.
    /// </summary>
    [Fact]
    public async Task A_viewer_cannot_run_the_mapper_preview()
    {
        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer(Unique("mapper-preview"));

        var preview = ActivatorUtilities.CreateInstance<Resources.Mappers.Preview>(
            scope.ServiceProvider);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            preview.Handle(new MapperPreviewRequest
            {
                InputJson = "{}",
                ScribanTemplate = "{}"
            }));
    }

    /// <summary>
    /// The rules-based preview maps against any partner the caller names, with every global value
    /// set besides, and took no RequestContext at all.
    /// </summary>
    [Fact]
    public async Task A_viewer_cannot_run_the_mapping_rules_preview()
    {
        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer(Unique("rules-preview"));

        var preview = ActivatorUtilities.CreateInstance<Resources.MappingPreviews.Preview>(
            scope.ServiceProvider);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            preview.Handle(new Resources.MappingPreviews.MappingPreviewRequest
            {
                MappingRules = "{}",
                SourceDocument = "{}"
            }));
    }

    private async Task<int> CategoryAsAdmin()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();

        var create = ActivatorUtilities.CreateInstance<Resources.SubscriptionCategories.Create>(
            scope.ServiceProvider);

        var created = await create.Handle(new CreateSubscriptionCategoryModel
        {
            Code = Unique("GUARDED"),
            Description = "Exists so the viewer has something to fail to change"
        });

        return (int)created.GetType().GetProperty("Id")!.GetValue(created)!;
    }

    private static string Unique(string prefix) => $"{prefix}-{System.Guid.NewGuid():N}"[..24];
}

using System;
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
/// A partner can choose whether a call it waited on is answered 200 or 202 when the work finished
/// with nothing to send back. Before this the choice was instance-wide, so two partners that
/// disagreed could not both be satisfied.
/// </summary>
[Collection("Bitween")]
public class PartnerAcceptedStatusCodeTests(BitweenFixture fixture)
{
    /// <summary>Nothing is imposed on a partner nobody has configured — it keeps deferring.</summary>
    [Fact]
    public async Task A_new_partner_has_no_choice_of_its_own()
    {
        var id = await CreatePartnerAsync(null);

        Assert.Null((await GetPartnerAsync(id)).AcceptedResponseStatusCode);
        Assert.Null(await StoredCodeAsync(id));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    public async Task A_choice_made_at_creation_is_stored_and_served(int code)
    {
        var id = await CreatePartnerAsync(code);

        Assert.Equal(code, (await GetPartnerAsync(id)).AcceptedResponseStatusCode);
        Assert.Equal(code, await StoredCodeAsync(id));
    }

    [Fact]
    public async Task The_choice_can_be_changed_and_taken_back_off()
    {
        var id = await CreatePartnerAsync(202);

        var row = await GetPartnerAsync(id);
        row.AcceptedResponseStatusCode = 200;
        await UpdatePartnerAsync(id, row);
        Assert.Equal(200, await StoredCodeAsync(id));

        row = await GetPartnerAsync(id);
        row.AcceptedResponseStatusCode = null;
        await UpdatePartnerAsync(id, row);
        Assert.Null(await StoredCodeAsync(id));
    }

    /// <summary>
    /// The round trip a form actually makes: read the partner, change something else, save the
    /// whole thing back. The update handler copies the model over the entity, so a payload that
    /// dropped this field would quietly clear it.
    /// </summary>
    [Fact]
    public async Task Saving_an_unrelated_edit_keeps_the_choice()
    {
        var id = await CreatePartnerAsync(200);

        var row = await GetPartnerAsync(id);
        row.Name = $"renamed-{Guid.NewGuid():N}";
        await UpdatePartnerAsync(id, row);

        Assert.Equal(200, await StoredCodeAsync(id));
    }

    /// <summary>
    /// The reply is built by asking whether the stored code is 200, so anything else behaves as
    /// 202. Storing 204 would look configured and act like it was not.
    /// </summary>
    [Fact]
    public async Task A_code_the_reply_cannot_express_is_refused()
    {
        await Assert.ThrowsAsync<SWValidationException>(() => CreatePartnerAsync(204));

        var id = await CreatePartnerAsync(200);
        var row = await GetPartnerAsync(id);
        row.AcceptedResponseStatusCode = 500;

        await Assert.ThrowsAsync<SWValidationException>(() => UpdatePartnerAsync(id, row));
        Assert.Equal(200, await StoredCodeAsync(id));
    }

    // ------------------------------------------------------------------- helpers

    private async Task<int> CreatePartnerAsync(int? acceptedResponseStatusCode)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Partners.Create>(scope.ServiceProvider);

        return (int)await handler.Handle(new PartnerCreate
        {
            Name = $"code-partner-{Guid.NewGuid():N}",
            AcceptedResponseStatusCode = acceptedResponseStatusCode
        });
    }

    private async Task<PartnerUpdate> GetPartnerAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Partners.Get>(scope.ServiceProvider);
        return (PartnerUpdate)await handler.Handle(id);
    }

    private async Task UpdatePartnerAsync(int id, PartnerUpdate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Partners.Update>(scope.ServiceProvider);
        await handler.Handle(id, model);
    }

    /// <summary>Reads past the API, so a handler that only echoes the model cannot pass.</summary>
    private async Task<int?> StoredCodeAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = await db.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == id);
        return partner.AcceptedResponseStatusCode;
    }
}

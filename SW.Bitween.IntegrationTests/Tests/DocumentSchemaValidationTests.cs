using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// An information type carrying a schema: kept across saves that don't mention it, enforced at
/// the gateway while the partner is still waiting, and on every exchange however it arrived.
/// What counts as a match is SW.Bitween.UnitTests' business (DocumentSchemaTests).
/// </summary>
[Collection("Bitween")]
public class DocumentSchemaValidationTests(BitweenFixture fixture)
{
    private const string Schema = """{ "type": "object", "required": ["orderId"] }""";
    private const string Responder = nameof(Adapters.NativeTestResponder);

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> CreateType(string schema)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (int)await ActivatorUtilities.CreateInstance<Resources.Documents.Create>(scope.ServiceProvider)
            .Handle(new DocumentCreate
                { Name = Unique("Schema type"), DocumentFormat = DocumentFormat.Json, ValidationSchema = schema });
    }

    private async Task<DocumentRow> GetType(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (DocumentRow)await ActivatorUtilities.CreateInstance<Resources.Documents.Get>(scope.ServiceProvider)
            .Handle(id);
    }

    private async Task UpdateType(int id, Action<DocumentUpdate> change)
    {
        var current = await GetType(id);
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var model = new DocumentUpdate
        {
            Id = id, Name = current.Name, Code = current.Code, DocumentFormat = current.DocumentFormat,
            PromotedProperties = current.PromotedProperties,
        };
        change(model);
        await ActivatorUtilities.CreateInstance<Resources.Documents.Update>(scope.ServiceProvider).Handle(id, model);
    }

    [Fact]
    public async Task A_schema_is_kept_by_saves_that_leave_it_out_and_removed_by_an_empty_one()
    {
        var id = await CreateType(Schema);
        Assert.Equal(Schema, (await GetType(id)).ValidationSchema);

        await UpdateType(id, m => m.Name += " renamed");
        Assert.Equal(Schema, (await GetType(id)).ValidationSchema);

        await UpdateType(id, m => m.ValidationSchema = "");
        Assert.Null((await GetType(id)).ValidationSchema);
    }

    [Fact]
    public async Task A_type_cannot_change_to_a_format_its_schema_does_not_fit()
    {
        var id = await CreateType(Schema);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            UpdateType(id, m => m.DocumentFormat = DocumentFormat.Xml));
    }

    [Fact]
    public async Task An_unusable_schema_is_refused_on_create()
    {
        await Assert.ThrowsAsync<SWValidationException>(() => CreateType("""{ "$ref": "http://169.254.169.254/" }"""));
    }

    [Fact]
    public async Task The_gateway_refuses_a_call_that_does_not_match_and_makes_no_exchange()
    {
        var docId = await CreateType(Schema);
        var urlName = Unique("schema").ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = new Partner(Unique("Schema partner"));
            partner.SetApiCredentials([new ApiCredential("main", key)]);
            db.Add(partner);
            var sub = new Subscription(Unique("Schema sub"), docId, SubscriptionType.GatewayApiCall) { Inactive = false };
            db.Add(sub);
            await db.SaveChangesAsync();
            db.Add(new ApiGateway
            {
                Name = Unique("Schema gateway"), UrlName = urlName,
                Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = sub.Id }],
            });
            await db.SaveChangesAsync();
        }

        async Task<IActionResult> Call(string body)
        {
            await using var scope = fixture.CreateScope();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Set(new ClaimsPrincipal(new ClaimsIdentity()),
                [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);
            var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return await controller.Post($"{urlName}/async");
        }

        var refused = Assert.IsType<BadRequestObjectResult>(await Call("""{ "customer": "x" }"""));
        Assert.Contains("orderId", JsonConvert.SerializeObject(refused.Value));
        Assert.IsType<AcceptedResult>(await Call("""{ "orderId": "A1" }"""));

        await using (var scope = fixture.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<Xchange>().CountAsync(x => x.DocumentId == docId));
    }

    [Fact]
    public async Task An_exchange_whose_input_does_not_match_fails_with_what_is_wrong()
    {
        var docId = await CreateType(Schema);
        int subId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var sub = new Subscription(Unique("Schema delivery"), docId, SubscriptionType.GatewayApiCall)
                { Inactive = false, HandlerId = Responder };
            db.Add(sub);
            await db.SaveChangesAsync();
            subId = sub.Id;
        }

        async Task<XchangeResult> Deliver(string body)
        {
            string xchangeId;
            await using (var scope = fixture.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
                xchangeId = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                    .SubmitSubscriptionXchange(subId, new XchangeFile(body));
            }

            await using (var scope = fixture.CreateScope())
                await scope.ServiceProvider.GetRequiredService<XchangeService>()
                    .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));

            await using (var scope = fixture.CreateScope())
                return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                    .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        }

        var failed = await Deliver("""{ "customer": "x" }""");
        Assert.False(failed.Success);
        Assert.Contains(nameof(DocumentSchemaException), failed.Exception);
        Assert.Contains("orderId", failed.Exception);

        var passed = await Deliver("""{ "orderId": "A1" }""");
        Assert.True(passed.Success, passed.Exception);
    }
}

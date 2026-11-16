using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Gateways that take signed calls: webhook senders that can't send a key or a token sign the body
/// with a secret instead, and the caller is the attached partner holding that secret.
/// </summary>
[Collection("Bitween")]
public class SignedGatewayTests(BitweenFixture fixture)
{
    const string Header = "X-Hook-Signature";
    const string SecretProperty = "WebhookSecret";
    const string Body = "{\"id\":42,\"event\":\"invoice.updated\"}";
    const string LongName = "WebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretWebhookSecretW";

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>A signed gateway with two attached partners, each with its own secret, and one partner not attached.</summary>
    private async Task<(string urlName, string secret, string otherSecret, string strangerSecret, string partnerName)> SignedGateway(
        GatewaySignatureScheme scheme = GatewaySignatureScheme.TimestampedHmacSha256)
    {
        var urlName = Unique("signed").ToLowerInvariant();
        var (secret, otherSecret, strangerSecret) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = Holding(Unique("Signer"), secret);
        var other = Holding(Unique("Other signer"), otherSecret);
        var stranger = Holding(Unique("Stranger"), strangerSecret);
        var doc = new Document(null, Unique("Signed doc"), DocumentFormat.Json);
        db.AddRange(partner, other, stranger, doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Signed sub"), doc.Id, SubscriptionType.GatewayApiCall) { Inactive = false };
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = Unique("Signed gateway"),
            UrlName = urlName,
            AuthMethod = GatewayAuthMethod.Signature,
            SignatureHeader = Header,
            SignatureScheme = scheme,
            SignatureSecretProperty = SecretProperty,
            Partners =
            [
                new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id },
                new ApiGatewayPartner { PartnerId = other.Id, SubscriptionId = subscription.Id },
            ],
        });
        await db.SaveChangesAsync();
        return (urlName, secret, otherSecret, strangerSecret, partner.Name);
    }

    static Partner Holding(string name, string secret)
    {
        var partner = new Partner(name)
        {
            AdapterProperties = new Dictionary<string, string> { [SecretProperty] = secret },
            SecretProperties = [SecretProperty],
        };
        partner.SetApiCredentials([new ApiCredential("key", Guid.NewGuid().ToString("N"))]);
        return partner;
    }

    static string Timestamped(string secret, string body, DateTimeOffset at)
    {
        var t = at.ToUnixTimeSeconds().ToString();
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{body}"));
        return $"t={t},v1={Convert.ToHexString(mac).ToLowerInvariant()}";
    }

    private async Task<IActionResult> Call(string urlName, string body, params (string name, string value)[] headers)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Set(new ClaimsPrincipal(new ClaimsIdentity()),
            headers.Select(h => new RequestValue(h.name, h.value, RequestValueType.HttpHeader)).ToArray());

        var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        foreach (var (name, value) in headers) controller.HttpContext.Request.Headers[name] = value;
        controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return await controller.Post($"{urlName}/async");
    }

    [Fact]
    public async Task A_signed_call_is_taken_as_the_partner_whose_secret_made_the_signature()
    {
        var (urlName, secret, _, _, partnerName) = await SignedGateway();

        var accepted = Assert.IsType<AcceptedResult>(await Call(urlName, Body, (Header, Timestamped(secret, Body, DateTimeOffset.UtcNow))));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchange = await db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == (string)accepted.Location);
        Assert.Contains($"signature: {partnerName}", xchange.References);
        // The exchange carries exactly the body that was signed.
        var file = await scope.ServiceProvider.GetRequiredService<XchangeService>().GetFile(xchange.Id, XchangeFileType.Input);
        Assert.Equal(Body, file);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("body-changed")]
    [InlineData("wrong-secret")]
    [InlineData("partner-not-attached")]
    [InlineData("partner-key")]
    public async Task A_signed_gateway_refuses_anything_else(string how)
    {
        var (urlName, secret, _, strangerSecret, _) = await SignedGateway();
        var now = DateTimeOffset.UtcNow;
        (string, string)[] headers = how switch
        {
            "missing" => [],
            "stale" => [(Header, Timestamped(secret, Body, now.AddMinutes(-10)))],
            "body-changed" => [(Header, Timestamped(secret, Body.Replace("42", "43"), now))],
            "wrong-secret" => [(Header, Timestamped("guessed", Body, now))],
            "partner-not-attached" => [(Header, Timestamped(strangerSecret, Body, now))],
            _ => [("partnerkey", "anything")],
        };

        Assert.IsType<UnauthorizedResult>(await Call(urlName, Body, headers));
    }

    [Fact]
    public async Task A_plain_hmac_signature_is_accepted_hex_or_base64()
    {
        var (urlName, secret, _, _, _) = await SignedGateway(GatewaySignatureScheme.HmacSha256);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(Body));

        Assert.IsType<AcceptedResult>(await Call(urlName, Body, (Header, "sha256=" + Convert.ToHexString(mac).ToLowerInvariant())));
        Assert.IsType<AcceptedResult>(await Call(urlName, Body, (Header, Convert.ToBase64String(mac))));
    }

    [Fact]
    public async Task Two_attached_partners_sharing_a_secret_are_both_refused()
    {
        var (urlName, secret, _, _, _) = await SignedGateway();
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = await db.Set<ApiGateway>().Include(g => g.Partners).SingleAsync(g => g.UrlName == urlName);
            var ids = gateway.Partners.Select(p => p.PartnerId).ToList();
            foreach (var partner in await db.Set<Partner>().Where(p => ids.Contains(p.Id)).ToListAsync())
                partner.AdapterProperties = new Dictionary<string, string> { [SecretProperty] = secret };
            await db.SaveChangesAsync();
        }

        Assert.IsType<UnauthorizedResult>(await Call(urlName, Body, (Header, Timestamped(secret, Body, DateTimeOffset.UtcNow))));
    }

    [Theory]
    [InlineData(null, GatewaySignatureScheme.TimestampedHmacSha256, SecretProperty, "GATEWAY_SIGNATURE_HEADER_INVALID")]
    [InlineData("Authorization", GatewaySignatureScheme.TimestampedHmacSha256, SecretProperty, "GATEWAY_SIGNATURE_HEADER_INVALID")]
    [InlineData(Header, null, SecretProperty, "GATEWAY_SIGNATURE_SCHEME_INVALID")]
    [InlineData(Header, GatewaySignatureScheme.TimestampedHmacSha256, " ", "GATEWAY_SIGNATURE_SECRET_REQUIRED")]
    [InlineData(Header, GatewaySignatureScheme.TimestampedHmacSha256, LongName, "GATEWAY_SIGNATURE_SECRET_REQUIRED")]
    public async Task A_signed_gateway_that_could_never_work_is_refused(string header, GatewaySignatureScheme? scheme,
        string secretProperty, string error)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Create>(scope.ServiceProvider);
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => handler.Handle(new ApiGatewayCreate
        {
            Name = Unique("Signed"),
            UrlName = Unique("signed-bad").ToLowerInvariant(),
            Authentication = new ApiGatewayAuthentication
            {
                Method = GatewayAuthMethod.Signature, SignatureHeader = header, SignatureScheme = scheme,
                SignatureSecretProperty = secretProperty,
            },
        }));
        Assert.StartsWith(error, ex.Message);
    }

    [Fact]
    public async Task A_signed_gateway_saved_through_the_api_reads_back_as_saved()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Create>(scope.ServiceProvider);
        var id = (int)await create.Handle(new ApiGatewayCreate
        {
            Name = Unique("Signed"),
            UrlName = Unique("signed-ok").ToLowerInvariant(),
            Authentication = new ApiGatewayAuthentication
            {
                Method = GatewayAuthMethod.Signature, SignatureHeader = Header,
                SignatureScheme = GatewaySignatureScheme.TimestampedHmacSha256, SignatureSecretProperty = SecretProperty,
                SignatureToleranceSeconds = 600,
            },
        });

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var gateway = await db.Set<ApiGateway>().AsNoTracking().SingleAsync(g => g.Id == id);
        Assert.Equal(GatewayAuthMethod.Signature, gateway.AuthMethod);
        Assert.Equal(Header, gateway.SignatureHeader);
        Assert.Equal(GatewaySignatureScheme.TimestampedHmacSha256, gateway.SignatureScheme);
        Assert.Equal(SecretProperty, gateway.SignatureSecretProperty);
        Assert.Equal(600, gateway.SignatureToleranceSeconds);
    }
}

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// A Microsoft ID token signs someone in only when it was issued to this Bitween's app, by the
/// configured tenant. Neither was checked before: a token issued to any app, anywhere, would do.
/// </summary>
[TestClass]
public class MicrosoftTokenValidationTests
{
    private const string ClientId = "11111111-1111-1111-1111-111111111111";
    private const string Tenant = "22222222-2222-2222-2222-222222222222";
    private const string OtherTenant = "33333333-3333-3333-3333-333333333333";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test" };

    private static string Token(string audience = ClientId, string tenant = Tenant, string issuerTenant = null,
        DateTime? expires = null, SecurityKey signWith = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: $"https://login.microsoftonline.com/{issuerTenant ?? tenant}/v2.0",
            audience: audience,
            claims:
            [
                new Claim("preferred_username", "Person@Example.com"),
                new Claim("oid", "aaaa-bbbb"),
                new Claim("tid", tenant),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-20),
            expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(signWith ?? Key, SecurityAlgorithms.RsaSha256)));

    private static AccountExtensions.MicrosoftIdentity Validate(string token, string tenantId = Tenant) =>
        AccountExtensions.ValidateMicrosoftToken(token, [Key], ClientId, tenantId);

    [TestMethod]
    public void A_token_for_this_app_from_its_tenant_signs_in()
    {
        var identity = Validate(Token());
        Assert.AreEqual("person@example.com", identity.Email);
        Assert.AreEqual($"aaaa-bbbb@{Tenant}", identity.ObjectAndTenant);
    }

    [TestMethod]
    public void A_token_issued_to_another_app_is_refused() =>
        Assert.ThrowsException<SecurityTokenInvalidAudienceException>(() => Validate(Token(audience: "another-app")));

    [TestMethod]
    public void A_token_from_another_tenant_is_refused_when_the_tenant_is_pinned() =>
        Assert.ThrowsException<SecurityTokenInvalidIssuerException>(() => Validate(Token(tenant: OtherTenant)));

    [TestMethod]
    public void A_multi_tenant_setup_accepts_any_tenant() =>
        Assert.IsNotNull(Validate(Token(tenant: OtherTenant), tenantId: "common"));

    [TestMethod]
    public void An_issuer_that_does_not_match_the_token_tenant_is_refused() =>
        Assert.ThrowsException<SecurityTokenInvalidIssuerException>(() =>
            Validate(Token(tenant: Tenant, issuerTenant: OtherTenant), tenantId: "common"));

    [TestMethod]
    public void An_expired_token_is_refused() =>
        Assert.ThrowsException<SecurityTokenExpiredException>(() =>
            Validate(Token(expires: DateTime.UtcNow.AddMinutes(-10))));

    [TestMethod]
    public void A_token_signed_by_someone_else_is_refused() =>
        Assert.ThrowsException<SecurityTokenSignatureKeyNotFoundException>(() =>
            Validate(Token(signWith: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "intruder" })));
}

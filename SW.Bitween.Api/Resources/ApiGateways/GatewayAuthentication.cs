using System;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ApiGateways;

/// <summary>
/// Copies how callers authenticate onto the gateway. A JWT gateway is refused when it could never
/// accept a token, or when it would accept tokens its login server issued for some other system.
/// </summary>
/// <remarks>
/// The JWT settings are kept when the gateway goes back to partner keys, so switching back and
/// forth doesn't mean typing them in again.
/// </remarks>
internal static class GatewayAuthentication
{
    public static ApiGatewayAuthentication Read(ApiGateway gateway) => new()
    {
        Method = gateway.AuthMethod,
        KeyHeader = gateway.PartnerKeyHeader,
        Issuer = gateway.JwtIssuer,
        Audience = gateway.JwtAudience,
        PartnerClaim = gateway.JwtPartnerClaim,
        SignatureHeader = gateway.SignatureHeader,
        SignatureScheme = gateway.SignatureHeader == null ? null : gateway.SignatureScheme,
        SignatureSecretProperty = gateway.SignatureSecretProperty,
        SignatureToleranceSeconds = gateway.SignatureToleranceSeconds,
    };

    public static void Apply(ApiGateway gateway, ApiGatewayAuthentication model)
    {
        if (!Enum.IsDefined(model.Method))
            throw new SWValidationException("GATEWAY_AUTH_METHOD_INVALID", $"'{model.Method}' is not a way to authenticate.");

        var issuer = Trimmed(model.Issuer);
        var audience = Trimmed(model.Audience);
        var keyHeader = Trimmed(model.KeyHeader);

        if (keyHeader != null && PartnerKeyHeaders.Problem(keyHeader) is { } problem)
            throw new SWValidationException("GATEWAY_KEY_HEADER_INVALID", problem);

        if (model.Method == GatewayAuthMethod.Jwt)
        {
            if (!OpenIdGatewayIssuers.IsUsable(issuer))
                throw new SWValidationException("GATEWAY_JWT_ISSUER_INVALID",
                    "The login server must be a full https:// address, exactly as its tokens name it in 'iss'.");

            if (audience == null)
                throw new SWValidationException("GATEWAY_JWT_AUDIENCE_REQUIRED",
                    "An audience is required. Without one, a token the login server issued for any " +
                    "other system would be accepted here.");
        }

        var signatureHeader = Trimmed(model.SignatureHeader);
        var secretProperty = Trimmed(model.SignatureSecretProperty);
        if (model.Method == GatewayAuthMethod.Signature)
        {
            if (signatureHeader == null || PartnerKeyHeaders.Problem(signatureHeader) is not null)
                throw new SWValidationException("GATEWAY_SIGNATURE_HEADER_INVALID",
                    "Name the header the signature comes in, as a plain header name.");
            if (model.SignatureScheme is not { } scheme || !Enum.IsDefined(scheme))
                throw new SWValidationException("GATEWAY_SIGNATURE_SCHEME_INVALID", "Say how the signature is written.");
            if (secretProperty == null)
                throw new SWValidationException("GATEWAY_SIGNATURE_SECRET_REQUIRED",
                    "Name the partner property holding the secret the caller signs with.");
            if (model.SignatureToleranceSeconds is < 1 or > 86400)
                throw new SWValidationException("GATEWAY_SIGNATURE_TOLERANCE_INVALID",
                    "The tolerance is between 1 second and a day.");
        }

        gateway.AuthMethod = model.Method;
        // Kept when the gateway switches to another way, as the JWT settings are.
        if (signatureHeader != null || model.Method == GatewayAuthMethod.Signature)
        {
            gateway.SignatureHeader = signatureHeader;
            gateway.SignatureScheme = model.SignatureScheme ?? GatewaySignatureScheme.HmacSha256;
            gateway.SignatureSecretProperty = secretProperty;
            gateway.SignatureToleranceSeconds = model.SignatureToleranceSeconds;
        }
        gateway.PartnerKeyHeader = keyHeader;
        gateway.JwtIssuer = issuer;
        gateway.JwtAudience = audience;
        gateway.JwtPartnerClaim = Trimmed(model.PartnerClaim);
    }

    static string Trimmed(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

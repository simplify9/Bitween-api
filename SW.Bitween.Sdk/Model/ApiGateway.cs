using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public class ApiGatewayCreate : IName
    {
        /// <summary>Both required; the server rejects a create without them.</summary>
        public string Name { get; set; } = null!;

        public string UrlName { get; set; } = null!;

        /// <summary>Off but kept, with its partner attachments. Calls to it are refused.</summary>
        public bool Inactive { get; set; }

        /// <summary>
        /// How callers prove who they are. Left out on create, the gateway takes partner keys;
        /// left out on update, it keeps what it has - so a save that only renames or pauses the
        /// gateway can't quietly switch it back to partner keys.
        /// </summary>
        public ApiGatewayAuthentication? Authentication { get; set; }

        /// <summary>
        /// What a sync call is told when its delivery fails. Left out on create, a new gateway answers
        /// Accurate; left out on update, it keeps what it has. Gateways made before this existed answer
        /// Legacy, as they always did.
        /// </summary>
        public GatewayFailureAnswer? FailureAnswer { get; set; }
    }

    /// <summary>What a sync gateway call is told when the delivery behind it fails.</summary>
    public enum GatewayFailureAnswer
    {
        /// <summary>400 with no body, whatever happened: what every gateway answered before.</summary>
        Legacy = 0,

        /// <summary>
        /// 202 when Bitween has scheduled a retry (it has the message: don't send it again), 422 when
        /// the message couldn't be mapped, 502 when the delivery failed and won't be retried. Each with
        /// the exchange id and a short reason.
        /// </summary>
        Accurate = 1,
    }

    /// <summary>How the callers of an API gateway prove who they are.</summary>
    public enum GatewayAuthMethod
    {
        /// <summary>A key issued on the partner's page, sent in any of the ways partner keys go.</summary>
        PartnerKey = 0,

        /// <summary>A token signed by a login server the gateway trusts, sent as a bearer token.</summary>
        Jwt = 1,

        /// <summary>
        /// A signature over the request body, made with a secret the partner shares with the caller:
        /// how webhook senders that can't add a key or a token prove who they are.
        /// </summary>
        Signature = 2,
    }

    /// <summary>How a signed gateway call carries its signature.</summary>
    public enum GatewaySignatureScheme
    {
        /// <summary>
        /// The header holds HMAC-SHA256 of the body, hex or base64, optionally after <c>sha256=</c>.
        /// </summary>
        HmacSha256 = 0,

        /// <summary>
        /// The header holds <c>t={unix seconds},v1={hex}</c>, where the hex is HMAC-SHA256 of
        /// <c>{t}.{body}</c>. A call whose <c>t</c> is further from now than the tolerance is refused.
        /// </summary>
        TimestampedHmacSha256 = 1,
    }

    public class ApiGatewayAuthentication
    {
        public GatewayAuthMethod Method { get; set; }

        /// <summary>
        /// Partner keys only: the header partners send their key in, overriding the system-wide
        /// one. Empty uses that. <c>partnerkey</c> is accepted either way.
        /// </summary>
        public string? KeyHeader { get; set; }

        /// <summary>
        /// JWT only: the login server, exactly as its tokens name it in <c>iss</c>. Its public
        /// keys are read from <c>{issuer}/.well-known/openid-configuration</c>.
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// JWT only: the <c>aud</c> a token must carry, so a token the same login server issued
        /// for some other system is refused.
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>JWT only: the claim holding a partner's login server identity; <c>sub</c> when empty.</summary>
        public string? PartnerClaim { get; set; }

        /// <summary>Signature only: the header the signature comes in. Required.</summary>
        public string? SignatureHeader { get; set; }

        /// <summary>Signature only: how the header is written and what is signed.</summary>
        public GatewaySignatureScheme? SignatureScheme { get; set; }

        /// <summary>
        /// Signature only: the partner property holding the secret the caller signs with, best marked
        /// secret on the partner. The caller is the attached partner whose secret makes the signature.
        /// </summary>
        public string? SignatureSecretProperty { get; set; }

        /// <summary>
        /// Signature, timestamped scheme only: how far the signed time may be from now, in seconds.
        /// 300 when empty.
        /// </summary>
        public int? SignatureToleranceSeconds { get; set; }
    }

    public class ApiGatewayRow : ApiGatewayUpdate
    {
        public int Id { get; set; }
        public int? PartnersCount { get; set; }

        /// <summary>
        /// Detail only: the system-wide key header, which applies while
        /// <see cref="ApiGatewayAuthentication.KeyHeader"/> is empty.
        /// </summary>
        public string? DefaultKeyHeader { get; set; }
    }

    public class ApiGatewayUpdate : ApiGatewayCreate
    {
        public ICollection<ApiGatewayPartnerDto> Partners { get; set; } = [];
    }

    public class ApiGatewayPartnerDto
    {
        public int PartnerId { get; set; }
        public int SubscriptionId { get; set; }
        public string PartnerName { get; set; } = null!;
        public string SubscriptionName { get; set; } = null!;

        /// <summary>Null when the partner has none, and so can't call a JWT gateway.</summary>
        public string? PartnerLoginIdentity { get; set; }
    }

    public class ApiGatewayPartnerCreate
    {
        public int PartnerId { get; set; }

        /// <summary>An integration that already exists. Exactly one of this and
        /// <see cref="NewIntegration"/> is given.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Define the integration here instead of creating it first. It is created as a
        /// GatewayApiCall in the same transaction as the attachment.</summary>
        public InlineIntegrationCreate? NewIntegration { get; set; }
    }

    public class SearchApiGatewayAttachmentsModel
    {
        public int ApiGatewayId { get; set; }
        /// <summary>Optional filter; null matches everything.</summary>
        public string? Search { get; set; }
        public int? Offset { get; set; }
        public int? Limit { get; set; }
    }
}


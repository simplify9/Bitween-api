using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{

    public class PartnerCreate  : IName
    {
        /// <summary>Required; the server rejects a create without it.</summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Referenced from adapter fields as {{partner.KEY}}. Accepted at creation so a
        /// partner can be made complete in one call — the UI creates partners from
        /// inside other flows, where a follow-up update that fails would leave a
        /// partner whose adapters resolve nothing.
        /// </summary>
        public Dictionary<string, string>? AdapterProperties { get; set; }

        /// <summary>
        /// Which of <see cref="AdapterProperties"/> are secrets. Their values come back from the
        /// API as the <c>__private__</c> sentinel; sending the sentinel back means "keep what is
        /// stored", so a form that changed only the partner's name never blanks a password.
        /// </summary>
        public ICollection<string> SecretProperties { get; set; } = [];

        /// <summary>
        /// 200 or 202 — what this partner is answered with when a call it waited on finished with
        /// nothing to send back. Null leaves it to the instance-wide setting. Every write carries
        /// it, so a form that changed only the name must send the value it read back unchanged.
        /// </summary>
        public int? AcceptedResponseStatusCode { get; set; }
    }
    public class PartnerRow : PartnerUpdate
    {
        public int Id { get; set; }
        public int? SubscriptionsCount { get; set; }
        public int? Keys { get; set; }
        /// <summary>
        /// The names of the partner's adapter properties — never their values, which
        /// can be secrets. Names alone are enough to count them in a list and to offer
        /// them as {{partner.x}} reference tokens when configuring an adapter.
        /// </summary>
        public ICollection<string> PropertyKeys { get; set; } = [];
    }

    public class PartnerUpdate : PartnerCreate
    {
        public ICollection<KeyAndValue> ApiCredentials { get; set; } = [];
        public ICollection<SubscriptionSearch> Subscriptions { get; set; } = [];
    }
}

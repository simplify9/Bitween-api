using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween
{
    static class BitweenDbContextExtensions
    {
        public static async Task<(Partner Partner, string KeyName)> AuthorizePartner(this BitweenDbContext dbContext,
            RequestContext requestContext, string keyHeader)
        {
            var (partnerAuthorized, partner, keyName) = await dbContext.CheckPartnerAuthorized(requestContext, keyHeader);
            return !partnerAuthorized
                ? throw new SWUnauthorizedException("Invalid or missing partner key")
                : (partner, keyName);
        }

        /// <param name="keyHeader">The header named for keys, by the gateway or system-wide.</param>
        public static async Task<(bool Authorized, Partner Partner, string KeyName)> CheckPartnerAuthorized(
            this BitweenDbContext dbContext,
            RequestContext requestContext,
            string keyHeader)
        {
            var (partnerKey, keyName) = ReadPartnerKey(requestContext, keyHeader);
            if (string.IsNullOrEmpty(partnerKey))
                return (false, null, null);

            // Stored hashed. The plain form still matches a row the startup pass has not converted
            // yet, so an upgrade does not lock anyone out for the moment between the two — but never
            // for a value in the stored form itself, or the hash out of a copied database would be
            // a working key. Real keys never look like one.
            var hashedKey = PartnerKeyHash.Of(partnerKey);
            var plainKey = PartnerKeyHash.IsHashed(partnerKey) ? hashedKey : partnerKey;
            var partnerQuery = from partner in dbContext.Set<Partner>()
                where partner.ApiCredentials.Any(cred => cred.Key == hashedKey || cred.Key == plainKey)
                select partner;

            var par = await partnerQuery.AsNoTracking().SingleOrDefaultAsync();
            if (par == null)
                return (false, null, null);

            // The database may compare without regard to case (SQL Server and MySQL often do), so
            // the row found can hold a key that only matches ignoring case. Keys are exact.
            var credential = par.ApiCredentials
                .SingleOrDefault(c => string.Equals(c.Key, hashedKey, StringComparison.Ordinal) ||
                                      string.Equals(c.Key, plainKey, StringComparison.Ordinal));
            if (credential == null)
                return (false, null, null);
            if (keyName != null && !string.Equals(keyName, credential.Name, StringComparison.OrdinalIgnoreCase))
                return (false, null, null);

            return (true, par, credential.Name);
        }

        /// <summary>
        /// The same key, from any of these places: the header named for keys, <c>partnerkey</c>
        /// (always, so renaming the header cuts no one off), a bearer token, or Basic auth. Basic
        /// carries the key as its password and the key's name as its username, so the name comes
        /// back too, to be checked against the key it found.
        /// </summary>
        static (string Key, string KeyName) ReadPartnerKey(RequestContext requestContext, string keyHeader)
        {
            var named = string.IsNullOrWhiteSpace(keyHeader) ? null : keyHeader.Trim().ToLowerInvariant();
            var partnerKey = named == null || named == PartnerKeyHeaders.Default
                ? null
                : requestContext.Values
                    .Where(item => item.Type == RequestValueType.HttpHeader && item.Name.ToLower() == named)
                    .Select(item => item.Value).FirstOrDefault();
            partnerKey ??= requestContext.Values.Where(item => item.Name.ToLower() == PartnerKeyHeaders.Default)
                .Select(item => item.Value).FirstOrDefault();
            if (partnerKey != null)
                return (partnerKey, null);

            var (scheme, credentials) = ReadAuthorization(requestContext);
            if (scheme == null)
                return (null, null);

            if (scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                return (credentials, null);

            if (!scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase))
                return (null, null);

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
            }
            catch (FormatException)
            {
                return (null, null);
            }

            // The username is everything before the first colon — Basic has no way to escape one.
            var colon = decoded.IndexOf(':');
            return colon < 0 ? (null, null) : (decoded[(colon + 1)..], decoded[..colon]);
        }

        /// <summary>The Authorization header split into its scheme and credentials; nulls without one.</summary>
        internal static (string Scheme, string Credentials) ReadAuthorization(RequestContext requestContext)
        {
            // Headers only: the context carries query parameters too, and ?authorization= is not
            // something any client sends.
            var authorization = requestContext.Values
                .Where(item => item.Type == RequestValueType.HttpHeader && item.Name.ToLower() == "authorization")
                .Select(item => item.Value).FirstOrDefault();
            var space = authorization?.IndexOf(' ') ?? -1;
            return space <= 0 ? (null, null) : (authorization[..space], authorization[(space + 1)..].Trim());
        }

        public static IQueryable<Subscription> Subscriptions(this BitweenDbContext dbContext) =>
            dbContext.Set<Subscription>().Include(s => s.WorkGroup);
    }
}
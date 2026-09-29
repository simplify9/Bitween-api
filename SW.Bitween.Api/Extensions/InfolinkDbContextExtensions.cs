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
            RequestContext requestContext)
        {
            var (partnerAuthorized, partner, keyName) = await dbContext.CheckPartnerAuthorized(requestContext);
            return !partnerAuthorized
                ? throw new SWUnauthorizedException("Invalid or missing partner key")
                : (partner, keyName);
        }

        public static async Task<(bool Authorized, Partner Partner, string KeyName)> CheckPartnerAuthorized(
            this BitweenDbContext dbContext,
            RequestContext requestContext)
        {
            var (partnerKey, keyName) = ReadPartnerKey(requestContext);
            if (string.IsNullOrEmpty(partnerKey))
                return (false, null, null);

            var partnerQuery = from partner in dbContext.Set<Partner>()
                where partner.ApiCredentials.Any(cred => cred.Key == partnerKey)
                select partner;

            var par = await partnerQuery.AsNoTracking().SingleOrDefaultAsync();
            if (par == null)
                return (false, null, null);

            // The database may compare without regard to case (SQL Server and MySQL often do), so
            // the row found can hold a key that only matches ignoring case. Keys are exact.
            var credential = par.ApiCredentials
                .SingleOrDefault(c => string.Equals(c.Key, partnerKey, StringComparison.Ordinal));
            if (credential == null)
                return (false, null, null);
            if (keyName != null && !string.Equals(keyName, credential.Name, StringComparison.OrdinalIgnoreCase))
                return (false, null, null);

            return (true, par, credential.Name);
        }

        /// <summary>
        /// The same key, from any of three places: our own <c>partnerkey</c> header, a bearer token,
        /// or Basic auth. Basic carries the key as its password and the key's name as its username,
        /// so the name comes back too, to be checked against the key it found.
        /// </summary>
        static (string Key, string KeyName) ReadPartnerKey(RequestContext requestContext)
        {
            var partnerKey = requestContext.Values.Where(item => item.Name.ToLower() == "partnerkey")
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
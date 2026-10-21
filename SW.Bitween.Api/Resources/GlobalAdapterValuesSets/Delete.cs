using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
    [HandlerName("delete")]
public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<string, DeleteGlobalAdapterValuesSetModel, object>
    {
        public async Task<object> Handle(string key, DeleteGlobalAdapterValuesSetModel _)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Delete);

            var entity = await dbContext.Set<GlobalAdapterValuesSet>().FindAsync(key);
            if (entity is null)
                throw new SWValidationException("NOT_FOUND", $"GlobalAdapterValuesSet with id {key} was not found");

            await EnsureNothingReferencesIt(key);

            dbContext.Remove(entity);
            await dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
            return null;
        }

        /// <summary>Refuses while a subscription or a notification channel names the set in a token.</summary>
        /// <remarks>
        /// A token whose set is gone is no error anywhere: it reaches the adapter as written, and the
        /// adapter fails at its next run on a password of <c>{{globals.smtp.password}}</c>, with
        /// nothing pointing back here. Read in memory because the properties are encrypted at rest,
        /// so the database can't search them.
        /// </remarks>
        private async Task EnsureNothingReferencesIt(string setId)
        {
            bool References(params IReadOnlyDictionary<string, string>[] properties) =>
                StartupValuesFiller.ReferencesIn(properties.Where(p => p != null).SelectMany(p => p.Values))
                    .Globals.Any(g => g.SetId.Equals(setId, StringComparison.OrdinalIgnoreCase));

            var subscriptions = (await dbContext.Set<Subscription>().AsNoTracking()
                    .Select(s => new { s.Name, s.HandlerProperties, s.MapperProperties, s.ReceiverProperties, s.ValidatorProperties })
                    .ToListAsync())
                .Where(s => References(s.HandlerProperties, s.MapperProperties, s.ReceiverProperties, s.ValidatorProperties))
                .Select(s => s.Name);

            var channels = (await dbContext.Set<NotificationChannel>().AsNoTracking()
                    .Select(c => new { c.Name, c.HandlerProperties })
                    .ToListAsync())
                .Where(c => References(c.HandlerProperties))
                .Select(c => $"the channel {c.Name}");

            var users = subscriptions.Concat(channels).ToList();
            if (users.Count == 0) return;

            throw new SWValidationException("GLOBAL_VALUES_IN_USE",
                $"'{setId}' is still referenced by {Describe(users)}. Change those references first.");
        }

        /// <summary>A few names and how many more - a set can be used by dozens of subscriptions.</summary>
        private static string Describe(List<string> names) => names.Count switch
        {
            1 => names[0],
            <= 3 => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
            _ => $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more"
        };
    }
}

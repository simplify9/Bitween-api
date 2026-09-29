using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Documents
{
    /// <summary>
    /// The one bus message type name rule, shared by Create and Update: a name belongs to one type.
    /// </summary>
    public static class BusMessageTypeNames
    {
        /// <summary>
        /// Refuses a name another type holds. Compared lower-cased, because that is how the bus
        /// compares them: both BasicPublisher and ConsumerDefinition derive the routing key with
        /// ToLower(), so "Foo" and "foo" are one message on the wire. ToLower() rather than a
        /// provider-specific collation — this runs on Postgres, MySql and MsSql.
        /// <para>
        /// A type paused on the bus holds its name too: its queue is still collecting messages for
        /// it, and a second type on the same name would consume them. Said as such, because
        /// "already publishes" is not what a paused type is doing.
        /// </para>
        /// </summary>
        public static async Task EnsureFree(BitweenDbContext dbContext, string name, int? exceptId = null)
        {
            if (string.IsNullOrEmpty(name)) return;

            var wanted = name.ToLower();
            var holder = await dbContext.Set<Document>()
                .AsNoTracking()
                .Where(d => exceptId == null || d.Id != exceptId)
                .Where(d => d.BusMessageTypeName != null && d.BusMessageTypeName.ToLower() == wanted)
                .Select(d => new { d.Name, d.BusEnabled })
                .FirstOrDefaultAsync();
            if (holder is null) return;

            throw new SWValidationException("DUPLICATED_BUS_TYPE_NAME", (holder.BusEnabled
                ? $"'{holder.Name}' already publishes as '{name}'. "
                : $"'{holder.Name}' is paused on the bus and still holds '{name}', because its queue is " +
                  "waiting for it. Rename or delete it to free the name. ")
                + "Names are compared ignoring case, because the bus does.");
        }
    }
}

using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink
{
    internal static class DbContextExtesions
    {

        async public static Task DeleteByKey<TEntity>(this InfolinkDbContext context, object key)
            where TEntity : BaseEntity
        {
            var entity = await context.FindAsync<TEntity>(key);
            context.Remove(entity);
            await context.SaveChangesAsync();
        }

    }
}

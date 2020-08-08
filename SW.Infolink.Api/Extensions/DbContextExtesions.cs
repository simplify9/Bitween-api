using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink
{
    internal static class DbContextExtesions
    {

        async public static Task Delete<TEntity>(this InfolinkDbContext context, object key)
            where TEntity : BaseEntity
        {
            var entity = await context.FindAsync<TEntity>(key);
            context.Remove(entity);
            await context.SaveChangesAsync();
        }

        async public static Task<string> Create<TEntity>(this InfolinkDbContext context, TEntity entity)
            where TEntity : BaseEntity
        {
            context.Add(entity);
            await context.SaveChangesAsync();
            return entity.Id.ToString();
        }

        async public static Task Update<TEntity>(this InfolinkDbContext context, object key, object dto)
            where TEntity : BaseEntity
        {
            var entity = await context.FindAsync<TEntity>(key);
            context.Entry(entity).SetProperties(dto);
            await context.SaveChangesAsync();
        }

        async public static Task CreateOrUpdate<TEntity>(this InfolinkDbContext context, object key, object dto, TEntity newEntity)
            where TEntity : BaseEntity
        {
            var existingEntity = await context.FindAsync<TEntity>(key);

            if (existingEntity is null)
            {
                context.Add(newEntity);
            }
            else
            {
                context.Entry(existingEntity).SetProperties(dto);
            }
            await context.SaveChangesAsync();
        }
    }
}

using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts
{
    public class Search : IQueryHandler<SearchMembersModel,object>
    {
        private readonly BitweenDbContext dbContext;

        public Search(BitweenDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(SearchMembersModel request)
        {
            request.Limit ??= 20;
            request.Offset ??= 0;
            var query = dbContext.Set<Account>().AsNoTracking().AsQueryable();


            if (request.Lookup)
            {
                return await query.OrderBy(i => i.DisplayName)
                    .ToDictionaryAsync(i => i.Id, i => i.DisplayName);
            }

            var count = await query.CountAsync();

            var accounts = await query.OrderBy(i => i.CreatedOn)
                .Skip(request.Offset.Value)
                .Take(request.Limit.Value)
                .Select(a => new AccountModel
                {
                    CreatedOn = a.CreatedOn,
                    Email = a.Email,
                    Name = a.DisplayName,
                    Id = a.Id,
                    Role = a.Role.ToString()
                })
                .ToListAsync();


            return new
            {
                Result = accounts,
                TotalCount = count
            };
        }
    }
}
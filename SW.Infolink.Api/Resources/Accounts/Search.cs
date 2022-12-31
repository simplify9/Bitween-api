using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts
{
    public class Search : IQueryHandler<SearchMembersModel>
    {
        private readonly InfolinkDbContext dbContext;

        public Search(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(SearchMembersModel request)
        {
            request.Limit ??= 20;
            request.Offset ??= 0;
            var query = dbContext.Set<Account>().AsNoTracking().AsQueryable();

            var count = await query.CountAsync();
            var accounts = await query.OrderBy(i => i.CreatedOn)
                .Skip(request.Offset.Value)
                .Take(request.Limit.Value)
                .ToArrayAsync();


            return new
            {
                Result = accounts.Select(a => new AccountModel
                {
                    CreatedOn = a.CreatedOn,
                    Email = a.Email,
                    Name = a.DisplayName,
                    Id= a.Id,
                }),
                TotalCount = count
            };
        }
    }
}
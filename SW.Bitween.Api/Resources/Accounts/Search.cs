using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts
{
    public class Search : IQueryHandler<SearchMembersModel, object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public Search(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        public async Task<object> Handle(SearchMembersModel request)
        {
            // Lookup returns only id/name pairs, which document and integration trails use to turn
            // createdBy ids into names; the member list itself is the data users.view covers.
            if (!request.Lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.View);

            request.Limit ??= 20;
            request.Offset ??= 0;
            var query = dbContext.Set<Account>().AsNoTracking().AsQueryable();

            if (request.Lookup)
            {
                // id -> display name only; needed across the app (e.g. audit trails)
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
                    Disabled = a.Disabled,
                    Role = a.Role.ToString(),
                    LockoutEnd = a.LockoutEnd
                })
                .ToListAsync();

            var accountIds = accounts.Select(a => a.Id).ToList();
            var rolesByAccount = await AccountRoles.For(dbContext, accountIds);

            foreach (var account in accounts)
                account.Roles = rolesByAccount.GetValueOrDefault(account.Id, []);

            return new
            {
                Result = accounts,
                TotalCount = count
            };
        }
    }
}

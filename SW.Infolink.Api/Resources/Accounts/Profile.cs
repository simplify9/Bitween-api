using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts;

[HandlerName("profile")]
public class Profile : IQueryHandler
{
    private readonly InfolinkDbContext dbContext;
    private readonly RequestContext requestContext;

    public Profile(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
    }

    public async Task<object> Handle()
    {
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        return await dbContext.Set<Account>().Select(a => new AccountModel
            {
                CreatedOn = a.CreatedOn,
                Email = a.Email,
                Name = a.DisplayName,
                Id = a.Id,
                Role = a.Role.ToString()
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId);
    }
}
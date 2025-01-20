using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

[HandlerName("profile")]
public class Profile : IQueryHandler<object>
{
    private readonly BitweenDbContext dbContext;
    private readonly RequestContext requestContext;

    public Profile(BitweenDbContext dbContext, RequestContext requestContext)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
    }

    public async Task<object> Handle()
    {
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        return await dbContext.Set<Account>()
            .AsNoTracking()
            .Select(a => new AccountModel
            {
                CreatedOn = a.CreatedOn,
                Email = a.Email,
                Name = a.DisplayName,
                Id = a.Id,
                Role = a.Role.ToString()
            })
            .FirstOrDefaultAsync(a => a.Id == accountId);
    }
}
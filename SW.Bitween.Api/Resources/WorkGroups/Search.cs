using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Search(IInfolinkCache infolinkCache)
    : IQueryHandler<SearchWorkGroupModel, object>
{

    public async Task<object> Handle(SearchWorkGroupModel request)
    {
        request.Limit ??= 20;
        request.Offset ??= 0;
        
        var workGroups = await infolinkCache.ListWorkGroupsAsync();

        var data= workGroups
            .OrderByDescending(i => i.Id)
            .Skip(request.Offset.Value)
            .Take(request.Limit.Value)
            .Select(workGroup => new WorkGroupModel()
            {
                Id = workGroup.Id,
                Name = workGroup.Name,
                BusMessageName = workGroup.BusMessageName,
                Options =  new WorkGroupOptions()
                {
                    RabbitMqOptions = new ConsumerSettings
                    {
                        Prefetch =workGroup.Options?.RabbitMqOptions?.Prefetch, 
                        Priority = workGroup.Options?.RabbitMqOptions?.Priority
                    }   
                }
            }).ToList();


        return new
        {
            Result = workGroups,
            TotalCount = data.Count
        };
    }
}
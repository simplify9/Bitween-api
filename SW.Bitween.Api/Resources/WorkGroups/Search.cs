using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Search(
    IInfolinkCache infolinkCache,
    IConsumerReader consumerReader,
    ILogger<Search> logger)
    : IQueryHandler<SearchWorkGroupModel, object>
{

    public async Task<object> Handle(SearchWorkGroupModel request)
    {
        request.Limit ??= 20;
        request.Offset ??= 0;
        
        var workGroups = await infolinkCache.ListWorkGroupsAsync();
        var consumerCounts = Array.Empty<ConsumerCount>();

        try
        {
            consumerCounts = await consumerReader.GetConsumerCount<XchangeService>();
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or System.Net.Http.HttpRequestException)
        {
            logger.LogWarning(ex, "Unable to load RabbitMQ consumer metrics for work groups.");
        }

        var data= workGroups
            .OrderByDescending(i => i.Id)
            .Skip(request.Offset.Value)
            .Take(request.Limit.Value)
            .Select(workGroup =>
            {
                var messageTypeName = workGroup.GetBusMessageName();
                var messageTypeNameForResponse = $"{messageTypeName}{XchangeService.ResultQueueSuffix}";
                var processorsCounts= consumerCounts.FirstOrDefault(c=> c.MessageName == messageTypeName);
                var notifiersCounts= consumerCounts.FirstOrDefault(c=> c.MessageName == messageTypeNameForResponse);
                return new WorkGroupModel()
                {
                    Id = workGroup.Id,
                    Name = workGroup.Name,
                    BusMessageName = workGroup.BusMessageName,
                    Options = new WorkGroupOptions()
                    {
                        RabbitMqOptions = new ConsumerSettings
                        {
                            Prefetch = workGroup.Options?.RabbitMqOptions?.Prefetch,
                            Priority = workGroup.Options?.RabbitMqOptions?.Priority
                        }
                    },
                    ProcessorAckRate = processorsCounts?.AckRate,
                    ProcessorIncomingRate = processorsCounts?.IncomingRate,
                    ProcessorProcessingCount = processorsCounts?.ProcessingCount,
                    ProcessorQueueCount = processorsCounts?.QueueCount,
                    NotifierAckRate = notifiersCounts?.AckRate,
                    NotifierIncomingRate = notifiersCounts?.IncomingRate,
                    NotifierProcessingCount = notifiersCounts?.ProcessingCount,
                    NotifierQueueCount = notifiersCounts?.QueueCount,
                };
            }).ToList();


        return new
        {
            Result = data,
            TotalCount = workGroups.Length
        };
    }
}

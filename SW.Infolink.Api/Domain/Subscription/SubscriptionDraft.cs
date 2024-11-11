using System;
using System.Collections.Generic;
using SW.EfCoreExtensions;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain;

public class SubscriptionDraft : BaseEntity, IAudited
{
    private SubscriptionDraft()
    {
    }

    public SubscriptionDraft(Subscription subscription)
    {
        Type = subscription.Type;
        SubscriptionId = subscription.Id;
        Category = subscription.Category;
        CategoryId = subscription.CategoryId;
        ValidatorId = subscription.ValidatorId;
        HandlerId = subscription.HandlerId;
        ReceiverId = subscription.ReceiverId;
        MapperId = subscription.MapperId;
        ResponseSubscriptionId = subscription.ResponseSubscriptionId;
        ResponseMessageTypeName = subscription.ResponseMessageTypeName;

        SetSchedules(new HashSet<Schedule>(subscription.Schedules));
        SetDictionaries(
            new Dictionary<string, string>(subscription.HandlerProperties),
            new Dictionary<string, string>(subscription.MapperProperties),
            new Dictionary<string, string>(subscription.ReceiverProperties),
            new Dictionary<string, string>(subscription.DocumentFilter),
            new Dictionary<string, string>(subscription.ValidatorProperties)
        );
        SetMatchExpression(subscription.MatchExpression);
    }

    public SubscriptionType Type { get; private set; }

    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; }
    public int? CategoryId { get; set; }
    public SubscriptionCategory Category { get; set; }
    public string ValidatorId { get; set; }
    public string HandlerId { get; set; }
    public string ReceiverId { get; set; }
    public string MapperId { get; set; }
    public IReadOnlyDictionary<string, string> ValidatorProperties { get; set; }
    public IReadOnlyDictionary<string, string> HandlerProperties { get; set; }
    public IReadOnlyDictionary<string, string> MapperProperties { get; set; }
    public IReadOnlyDictionary<string, string> ReceiverProperties { get; set; }
    public IReadOnlyDictionary<string, string> DocumentFilter { get; set; }
    public IPropertyMatchSpecification MatchExpression { get; set; }

    public int? ResponseSubscriptionId { get; set; }
    public string ResponseMessageTypeName { get; set; }
    public readonly HashSet<Schedule> Schedules;

    public void SetDictionaries(
        IReadOnlyDictionary<string, string> handler,
        IReadOnlyDictionary<string, string> mapper,
        IReadOnlyDictionary<string, string> receiver,
        IReadOnlyDictionary<string, string> document,
        IReadOnlyDictionary<string, string> validator
    )
    {
        HandlerProperties = handler;
        MapperProperties = mapper;
        ReceiverProperties = receiver;
        ValidatorProperties = validator;
        DocumentFilter = document;
    }

    public void SetSchedules(IEnumerable<Schedule> schedules = null)
    {
        switch (Type)
        {
            case SubscriptionType.Receiving:
            case SubscriptionType.Aggregation:
            {
                if (schedules != null) Schedules.Update(schedules);
                break;
            }
        }
    }

    public void SetMatchExpression(IPropertyMatchSpecification matchExpression)
    {
        MatchExpression = matchExpression;
    }

    public void Publish()
    {
        if (Subscription == null)
        {
            throw new ArgumentNullException(nameof(Subscription));
        }

        Subscription.Category = Category;
        Subscription.CategoryId = CategoryId;
        Subscription.ValidatorId = ValidatorId;
        Subscription.HandlerId = HandlerId;
        Subscription.ReceiverId = ReceiverId;
        Subscription.MapperId = MapperId;
        Subscription.ResponseSubscriptionId = ResponseSubscriptionId;
        Subscription.ResponseMessageTypeName = ResponseMessageTypeName;
        Subscription.SetDictionaries(
            new Dictionary<string, string>(HandlerProperties),
            new Dictionary<string, string>(MapperProperties),
            new Dictionary<string, string>(ReceiverProperties),
            new Dictionary<string, string>(DocumentFilter),
            new Dictionary<string, string>(ValidatorProperties)
        );
        Subscription.SetSchedules(new HashSet<Schedule>(Schedules));
        Subscription.SetMatchExpression(MatchExpression);
    }

    public DateTime? PublishedOn { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
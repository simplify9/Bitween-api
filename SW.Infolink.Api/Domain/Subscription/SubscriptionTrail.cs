using System;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain;

public class SubscriptionTrail : BaseEntity<string>, ICreationAudited
{
    private SubscriptionTrail()
    {
    }


    public SubscriptionTrail(SubscriptionTrialCode code, Subscription subscription, bool isNew = false)
    {
        Id = Guid.NewGuid().ToString("N");
        Code = code;
        if (isNew)
        {
            StateBefore = "{}";
            StateAfter = JsonConvert.SerializeObject(subscription);
        }
        else
        {
            StateBefore = JsonConvert.SerializeObject(subscription);
            StateAfter = "{}";
        }
        Subscription = subscription;
    }

    public void SetAfter(Subscription stateAfter)
    {
        StateAfter = JsonConvert.SerializeObject(stateAfter);
    }

    public int SubscriptionId { get; private set; }
    public Subscription Subscription { get; private set; }

    public SubscriptionTrialCode Code { get; private set; }

    public string StateBefore { get; private set; }

    public string StateAfter { get; private set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
}
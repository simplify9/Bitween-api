using System;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain;

public class SubscriptionTrail : BaseEntity, ICreationAudited
{
    private SubscriptionTrail()
    {
    }


    public SubscriptionTrail(SubscriptionTrialCode code, Subscription stateBefore)
    {
        Code = code;
        StateBefore = JsonConvert.SerializeObject(stateBefore);
        SubscriptionId = stateBefore.Id;
    }

    public void SetAfter(Subscription stateAfter)
    {
        StateAfter = JsonConvert.SerializeObject(stateAfter);
    }

    public int SubscriptionId { get; set; }
    public SubscriptionTrialCode Code { get; set; }

    public string StateBefore { get; set; }

    public string StateAfter { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
}
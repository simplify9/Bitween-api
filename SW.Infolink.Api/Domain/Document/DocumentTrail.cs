using System;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain;

public class DocumentTrail : BaseEntity<string>, ICreationAudited
{
    private DocumentTrail()
    {
    }


    public DocumentTrail(DocumentTrailCode code, Document stateBefore)
    {
        Id = Guid.NewGuid().ToString("N");

        Code = code;
        StateBefore = JsonConvert.SerializeObject(stateBefore);
        StateAfter = "{}";
        DocumentId = stateBefore.Id;
    }

    public void SetAfter(Document stateAfter)
    {
        StateAfter = JsonConvert.SerializeObject(stateAfter);
    }

    public int DocumentId { get; set; }
    public Document Document { get; set; }

    public DocumentTrailCode Code { get; set; }

    public string StateBefore { get; set; }

    public string StateAfter { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
}
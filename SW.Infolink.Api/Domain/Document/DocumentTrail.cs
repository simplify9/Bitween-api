using System;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain;

public class DocumentTrail : BaseEntity<string>, ICreationAudited
{
    private DocumentTrail()
    {
    }


    public DocumentTrail(DocumentTrailCode code, Document document,bool isNew = false)
    {
        Id = Guid.NewGuid().ToString("N");

        Code = code;
        if (isNew)
        {
            StateBefore = "{}";
            StateAfter = JsonConvert.SerializeObject(document);
        }
        else
        {
            StateBefore = JsonConvert.SerializeObject(document);
            StateAfter = "{}";
        }
        
        Document = document;
    }
    public void SetAfter(Document stateAfter)
    {
        StateAfter = JsonConvert.SerializeObject(stateAfter);
    }

    public int DocumentId { get; private set; }
    public Document Document { get; private set; }

    public DocumentTrailCode Code { get; private set; }

    public string StateBefore { get; private set; }

    public string StateAfter { get; private set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
}
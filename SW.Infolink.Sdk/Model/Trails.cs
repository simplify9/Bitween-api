using System;

namespace SW.Infolink.Model;

public class TrailBaseModel
{
    public string Id { get; set; }
    public string Code { get; set; }
    public string StateBefore { get; set; }
    public string StateAfter { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
}
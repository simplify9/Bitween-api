namespace SW.Bitween.Model;

/// <summary>A metrics query over the last <see cref="Days"/> days (1 to 90; 7 when not given).</summary>
public class MetricsWindowRequest
{
    public int? Days { get; set; }
}

public class AdapterMetricsRequest
{
    public string Id { get; set; }
    public int? Days { get; set; }
}

public class SubscriptionMetricsRequest
{
    public int Id { get; set; }
    public int? Days { get; set; }
}

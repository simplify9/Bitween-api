using System.Collections.Generic;

namespace SW.Bitween.Model;

public class RetryPolicyCreate
{
    public required string Name { get; set; }
    public List<RetryGroup> Groups { get; set; } = [];
}

public class RetryPolicyUpdate : RetryPolicyCreate { }

public class RetryPolicyRow
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int GroupCount { get; set; }
}

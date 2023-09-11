using System;

namespace SW.Infolink.Model;

public class SubscriptionCategoryModel
{
    public int Id { get; set; }
    public string Code { get; set; }
    public string Description { get; set; }
    public DateTime CreatedOn { get; set; }
}

public class CreateSubscriptionCategoryModel
{
    public string Code { get; set; }
    public string Description { get; set; }
}

public class SearchSubscriptionCategoryModel
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

public class UpdateSubscriptionCategoryModel : CreateSubscriptionCategoryModel
{
}

public class DeleteSubscriptionCategoryModel
{
}
using System.Linq;
using SW.Infolink.Domain;

namespace SW.Infolink;

public static class SubscriptionExtensions
{
    public static IPropertyMatchSpecification TransformOldMatchExpression(this Subscription sub, Document doc)
    {
        return (from filter in sub.DocumentFilter
            let path = doc.PromotedProperties.Where(p => p.Key == filter.Key)
                .Select(p => p.Value)
                .First()
            select new OneOfSpec(path, filter.Value.Split(","))).Aggregate<OneOfSpec, IPropertyMatchSpecification>(null, (current, expression) => current == null
            ? expression
            : new AndSpec(current, expression));
    }
    
    public static IPropertyMatchSpecification BackwardCompatibleMatchExpression(this Subscription sub, Document doc)
    {
        
        return sub.MatchExpression ?? (sub.DocumentFilter != null ? TransformOldMatchExpression(sub, doc) : null);
    }
}
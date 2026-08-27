using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Puts a <see cref="SubscriptionConfiguration"/> onto a subscription. Create and Update both
/// go through here, so a field cannot be honoured by one and silently ignored by the other —
/// which is what happened for as long as create couldn't accept a pipeline at all.
/// <para>
/// Deliberately does not touch name, document, partner or aggregation-for. On create those are
/// decided by the constructor, and an Aggregation subscription's <c>DocumentId</c> is
/// <see cref="Document.AggregationDocumentId"/> rather than anything the caller sent — copying
/// the model over it would corrupt the subscription.
/// </para>
/// </summary>
internal static class SubscriptionConfigurationApplier
{
    /// <summary>
    /// A property whose value the client is not allowed to see. It gets this sentinel instead,
    /// and sending it back means "keep what is stored" rather than "set it to this string".
    /// </summary>
    private const string PrivateSentinel = "__private__";

    public static async Task Apply(BitweenDbContext dbContext, Subscription entity, SubscriptionConfiguration model)
    {
        entity.ReceiverId = model.ReceiverId;
        entity.ValidatorId = model.ValidatorId;
        entity.MapperId = model.MapperId;
        entity.HandlerId = model.HandlerId;
        entity.CategoryId = model.CategoryId;
        entity.WorkGroupId = model.WorkGroupId;
        entity.ResponseSubscriptionId = model.ResponseSubscriptionId;
        entity.ResponseMessageTypeName = model.ResponseMessageTypeName;
        // Meaningless for every other type, where it stays at its default — but harmless
        // there, and applying it unconditionally is what stops create and update disagreeing.
        entity.AggregationTarget = model.AggregationTarget;

        // Only when the caller said something about them. A Receiving subscription with no
        // schedules is what SetSchedules throws on, and a create that mentions no schedule at
        // all is the old, still-supported "empty subscription" call.
        if (model.Schedules != null)
            entity.SetSchedules(model.Schedules.Select(dto => new Schedule(dto.Recurrence,
                System.TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());

        entity.SetDictionaries(
            MergeWithOriginal(entity.HandlerProperties, model.HandlerProperties),
            MergeWithOriginal(entity.MapperProperties, model.MapperProperties),
            MergeWithOriginal(entity.ReceiverProperties, model.ReceiverProperties),
            model.DocumentFilter?.ToDictionary() ?? new Dictionary<string, string>(),
            MergeWithOriginal(entity.ValidatorProperties, model.ValidatorProperties)
        );
        entity.SetMatchExpression(model.MatchExpression);

        if (model.CustomRetryPolicy == null && model.RetryPolicyId != null &&
            !await dbContext.Set<RetryPolicy>().AnyAsync(p => p.Id == model.RetryPolicyId))
            throw new SWValidationException("RETRY_POLICY_NOT_FOUND",
                $"Retry policy {model.RetryPolicyId} was not found.");

        entity.SetRetryPolicy(model.RetryPolicyId, model.CustomRetryPolicy);
    }

    private static Dictionary<string, string> MergeWithOriginal(
        IReadOnlyDictionary<string, string> original,
        ICollection<KeyAndValue> incoming)
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in incoming ?? Enumerable.Empty<KeyAndValue>())
        {
            if (kv.Value == PrivateSentinel)
            {
                // Private prop: restore the original stored value, don't overwrite with sentinel.
                if (original != null && original.TryGetValue(kv.Key, out var stored))
                    result[kv.Key] = stored;
            }
            else
            {
                result[kv.Key] = kv.Value;
            }
        }
        return result;
    }
}

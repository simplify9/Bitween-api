using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Resources.NotificationChannels;
using SW.Bitween.Resources.RetryPolicies;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Puts a <see cref="SubscriptionConfiguration"/> onto a subscription. Create and Update both
/// go through here, so a field cannot be honoured by one and silently ignored by the other -
/// which is what happened for as long as create couldn't accept a pipeline at all.
/// <para>
/// Deliberately does not touch name, document, partner or aggregation-for. On create those are
/// decided by the constructor, and an Aggregation subscription's <c>DocumentId</c> is
/// <see cref="Document.AggregationDocumentId"/> rather than anything the caller sent - copying
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

    /// <summary>The adapters a subscription held when it was loaded, which its stored secrets belong to.</summary>
    internal record StoredAdapters(string Handler, string Mapper, string Receiver, string Validator,
        string HandlerRef, string MapperRef, string ReceiverRef, string ValidatorRef)
    {
        public static StoredAdapters Of(Subscription subscription) => new(
            subscription.HandlerId, subscription.MapperId, subscription.ReceiverId, subscription.ValidatorId,
            subscription.HandlerRef, subscription.MapperRef, subscription.ReceiverRef, subscription.ValidatorRef);
    }

    /// <param name="stored">
    /// Update reads these before its property copy, which already puts the model's adapter ids on
    /// the entity; read here instead, the old adapter would be the new one, and a stored secret would
    /// be kept for an adapter it was never meant for. Left out, the entity's are used, which is right
    /// for one being created.
    /// </param>
    public static async Task Apply(BitweenDbContext dbContext, AdapterRequirements adapterRequirements,
        Subscription entity, SubscriptionConfiguration model, StoredAdapters stored = null)
    {
        // Whether a stored secret may be kept depends on the adapter it was stored for. See
        // AdapterSecretProperties.MayKeepStoredSecrets.
        stored ??= StoredAdapters.Of(entity);
        var (oldHandler, oldMapper, oldReceiver, oldValidator) =
            (stored.Handler, stored.Mapper, stored.Receiver, stored.Validator);
        var (oldHandlerRef, oldMapperRef, oldReceiverRef, oldValidatorRef) =
            (stored.HandlerRef, stored.MapperRef, stored.ReceiverRef, stored.ValidatorRef);

        // A pin without an adapter means nothing, and a blank one means "follow current".
        static string Pin(string adapterId, string version) =>
            string.IsNullOrWhiteSpace(adapterId) || string.IsNullOrWhiteSpace(version) ? null : version.Trim();

        var pins = (
            Handler: Pin(model.HandlerId, model.HandlerVersion),
            Mapper: Pin(model.MapperId, model.MapperVersion),
            Receiver: Pin(model.ReceiverId, model.ReceiverVersion),
            Validator: Pin(model.ValidatorId, model.ValidatorVersion));

        static bool Changed(string before, string adapterId, string version) =>
            !string.Equals(before, SW.Serverless.Contract.Catalog.AdapterCatalogPaths.Ref(adapterId, version),
                System.StringComparison.OrdinalIgnoreCase);

        await adapterRequirements.EnsureRunnable("handler", model.HandlerId, pins.Handler, Changed(oldHandlerRef, model.HandlerId, pins.Handler));
        await adapterRequirements.EnsureRunnable("mapper", model.MapperId, pins.Mapper, Changed(oldMapperRef, model.MapperId, pins.Mapper));
        await adapterRequirements.EnsureRunnable("receiver", model.ReceiverId, pins.Receiver, Changed(oldReceiverRef, model.ReceiverId, pins.Receiver));
        await adapterRequirements.EnsureRunnable("validator", model.ValidatorId, pins.Validator, Changed(oldValidatorRef, model.ValidatorId, pins.Validator));

        entity.HandlerVersion = pins.Handler;
        entity.MapperVersion = pins.Mapper;
        entity.ReceiverVersion = pins.Receiver;
        entity.ValidatorVersion = pins.Validator;

        entity.ReceiverId = model.ReceiverId;
        entity.ValidatorId = model.ValidatorId;
        entity.MapperId = model.MapperId;
        entity.HandlerId = model.HandlerId;
        entity.DataSourceId = model.DataSourceId;
        entity.CategoryId = model.CategoryId;
        entity.WorkGroupId = model.WorkGroupId;
        entity.ResponseSubscriptionId = model.ResponseSubscriptionId;
        if (model.NewResponseSubscription != null)
        {
            if (model.ResponseSubscriptionId != null)
                throw new SWValidationException(ResponseRoutingValidation.Code,
                    "Hand the response to an existing response subscription or to a new one, not both.");

            // Staged, not saved: EF inserts it first and fills ResponseSubscriptionId from it on the
            // caller's one save, so a subscription pointing at one that was never committed can't happen.
            entity.ResponseSubscription = await InlineIntegration.Stage(dbContext, adapterRequirements,
                model.NewResponseSubscription, model.NewResponseSubscription.DocumentId, SubscriptionType.Response);
        }
        entity.ResponseMessageTypeName = model.ResponseMessageTypeName;
        entity.RunOnBadResponses = model.RunOnBadResponses;
        if (model.AutoPauseAfterFailures is < 1 or > 1000)
            throw new SWValidationException("AutoPauseAfterFailures",
                "Pause after between 1 and 1000 failures in a row, or leave it empty to never pause.");
        entity.AutoPauseAfterFailures = model.AutoPauseAfterFailures;
        // Replaced like the fields around it: an update that leaves it out stops expecting traffic.
        entity.SetExpectTraffic(TrafficWindow.Normalize(model.ExpectTraffic));
        // Meaningless for every other type, where it stays at its default - but harmless
        // there, and applying it unconditionally is what stops create and update disagreeing.
        entity.AggregationTarget = model.AggregationTarget;

        // Only when the caller said something about them. A Receiving subscription with no
        // schedules is what SetSchedules throws on, and a create that mentions no schedule at
        // all is the old, still-supported "empty subscription" call.
        if (model.Schedules != null)
            entity.SetSchedules(model.Schedules.Select(dto => new Schedule(dto.Recurrence,
                System.TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());

        entity.SetDictionaries(
            MergeWithOriginal(entity.HandlerProperties, model.HandlerProperties, oldHandler, model.HandlerId),
            MergeWithOriginal(entity.MapperProperties, model.MapperProperties, oldMapper, model.MapperId),
            MergeWithOriginal(entity.ReceiverProperties, model.ReceiverProperties, oldReceiver, model.ReceiverId),
            model.DocumentFilter?.ToDictionary() ?? new Dictionary<string, string>(),
            MergeWithOriginal(entity.ValidatorProperties, model.ValidatorProperties, oldValidator, model.ValidatorId)
        );
        entity.SetMatchExpression(model.MatchExpression);

        if (model.CustomRetryPolicy == null && model.RetryPolicyId != null &&
            !await dbContext.Set<RetryPolicy>().AnyAsync(p => p.Id == model.RetryPolicyId))
            throw new SWValidationException("RETRY_POLICY_NOT_FOUND",
                $"Retry policy {model.RetryPolicyId} was not found.");

        // Here rather than in the update handler alone, so a subscription created with an inline
        // policy is held to the same rules as a shared policy and as an update.
        if (model.CustomRetryPolicy != null)
        {
            RetryGroupValidation.EnsureCanFire(model.CustomRetryPolicy.Groups);
            await RetryGroupValidation.EnsureAlertChannelsExist(dbContext,
                RetryGroupValidation.AlertChannelIds(model.CustomRetryPolicy.Groups));
        }

        entity.SetRetryPolicy(model.RetryPolicyId, model.CustomRetryPolicy);

        // Null leaves them as they are, so a caller that has never heard of notifications, such as
        // an older client or a script, does not wipe them by saving something else.
        if (model.Notifications != null)
        {
            await ChannelValidation.EnsureNotificationsCanSend(dbContext, model.Notifications);
            entity.SetNotifications(model.Notifications);
        }
    }

    private static Dictionary<string, string> MergeWithOriginal(
        IReadOnlyDictionary<string, string> original,
        ICollection<KeyAndValue> incoming, string originalAdapterId, string adapterId)
    {
        if (!AdapterSecretProperties.MayKeepStoredSecrets(originalAdapterId, adapterId, original,
                (incoming ?? []).Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value))))
            original = null;

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

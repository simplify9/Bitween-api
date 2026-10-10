using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Services;
using SW.EfCoreExtensions;

namespace SW.Bitween;

/// <summary>
/// What the audit trail records, and what it must never record. Owned by C# in the same way as the
/// permission catalog, so the rules sit in one readable list rather than being spread across the
/// handlers that happen to write each entity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Default deny.</b> An entity absent from <see cref="Audited"/> produces no audit rows at all.
/// That is deliberate: everything in Bitween shares one <see cref="BitweenDbContext"/>, so an
/// allow-everything policy would write several rows per message processed and bury the
/// configuration changes the trail exists to show. It also means <see cref="AuditEntry"/> cannot
/// audit itself.
/// </para>
/// <para>
/// <b>Credentials.</b> <see cref="Redacted"/> names the properties that are never captured in any
/// entity state. An audit table is read by more people than the screens that mask these values, so
/// a property that is hidden in the UI and recorded here in full would be a leak wearing a
/// different hat.
/// </para>
/// </remarks>
public static class AuditPolicy
{
    /// <summary>
    /// The configuration entities, their owned children, and the account/role records. Runtime
    /// traffic — every <c>Xchange</c>, result, delivery, receive attempt, retry usage counter and
    /// refresh token — is deliberately absent.
    /// </summary>
    private static readonly HashSet<Type> Audited =
    [
        typeof(Subscription),
        typeof(Schedule),               // owned by Subscription
        typeof(SubscriptionCategory),
        typeof(Partner),
        typeof(ApiCredential),          // owned by Partner
        typeof(Document),
        typeof(ApiGateway),
        typeof(ApiGatewayPartner),
        typeof(BusGateway),
        typeof(BusGatewayRoute),
        typeof(WorkGroup),
        typeof(RetryPolicy),
        typeof(RetryAlertOverride),
        typeof(Notifier),
        typeof(GlobalAdapterValuesSet),
        typeof(Setting),
        typeof(Account),
        typeof(Role),
        typeof(AccountRoleLink),
        // Not configuration, but reading an adapter's source is what the trail is for.
        typeof(AdapterSourceAccess),
        // Code written in Bitween, and what was published and made current from it.
        typeof(AdapterDraft),
        typeof(AdapterRelease),
        // Retries, exports, requeues and queue deletions: not configuration, but who did them is
        // what the trail is asked.
        typeof(OperatorAction)
    ];

    /// <summary>
    /// Properties whose values never enter the trail. The adapter property bags are here because
    /// each is stored as a single JSON column, so the change tracker sees one property rather than
    /// the individual keys — there is no way to keep <c>Host</c> and drop <c>Password</c> without
    /// cracking the JSON open and asking the adapter which of its keys are <c>[Secure]</c>, which
    /// would mean starting a serverless adapter in the middle of a save. Recording that a
    /// subscription was edited without saying which adapter key changed is the deliberate trade.
    /// </summary>
    private static readonly Dictionary<Type, HashSet<string>> Redacted = new()
    {
        [typeof(Subscription)] =
        [
            nameof(Subscription.HandlerProperties),
            nameof(Subscription.MapperProperties),
            nameof(Subscription.ReceiverProperties),
            nameof(Subscription.ValidatorProperties)
        ],
        [typeof(Partner)] = [nameof(Partner.AdapterProperties)],
        [typeof(Notifier)] = [nameof(Notifier.HandlerProperties)],
        [typeof(RetryPolicy)] = [nameof(RetryPolicy.AlertHandlerProperties)],
        [typeof(RetryAlertOverride)] = [nameof(RetryAlertOverride.AlertHandlerProperties)],

        // Free-form values an adapter reads by name. Nothing marks which of them is a credential,
        // so there is no safe subset to keep.
        [typeof(GlobalAdapterValuesSet)] = [nameof(GlobalAdapterValuesSet.Values)],

        // The key itself is the credential. Name survives, so adding or revoking one is visible.
        [typeof(ApiCredential)] = [nameof(ApiCredential.Key)],

        [typeof(Account)] = [nameof(Account.Password)],

        // Not a secret, but the code would be in the trail twice on every save. FilesHash is
        // recorded instead: each save shows, and the code itself is in the published package.
        [typeof(AdapterDraft)] = [nameof(AdapterDraft.FilesJson)]
    };

    public static readonly AuditOptions Options = new()
    {
        ShouldAuditEntity = entry => Audited.Contains(entry.Metadata.ClrType),
        ShouldAuditProperty = ShouldAuditProperty
    };

    private static bool ShouldAuditProperty(EntityEntry entry, IProperty property)
    {
        if (Redacted.TryGetValue(entry.Metadata.ClrType, out var redacted) &&
            redacted.Contains(property.Name))
            return false;

        // A setting's value is only sometimes a secret, and unlike an adapter's properties the
        // catalog says which — a static lookup, no adapter to start — so the theme colour stays
        // readable in the trail while the licence key never enters it.
        if (entry.Entity is Setting setting && property.Name == nameof(Setting.Value))
            return SettingsCatalog.Find(setting.Id)?.Secret != true;

        return true;
    }
}

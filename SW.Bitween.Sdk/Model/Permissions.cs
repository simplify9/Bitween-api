using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Model;

/// <summary>
/// Every privilege Bitween recognises, as "&lt;area&gt;.&lt;action&gt;" keys. This is the single
/// source of truth: handlers guard on these constants, roles store the keys, and the
/// management UI renders the catalog served by GET /permissions. A unit test asserts
/// these constants and <see cref="PermissionCatalog"/> never drift apart.
/// </summary>
public static class Permissions
{
    public static class Exchanges
    {
        public const string View = "exchanges.view";
        public const string Operate = "exchanges.operate";
    }

    public static class Monitoring
    {
        public const string View = "monitoring.view";
    }

    public static class Dashboard
    {
        public const string View = "dashboard.view";
    }

    public static class Subscriptions
    {
        public const string View = "subscriptions.view";
        public const string Create = "subscriptions.create";
        public const string Edit = "subscriptions.edit";
        public const string Delete = "subscriptions.delete";
        public const string Operate = "subscriptions.operate";
    }

    public static class Partners
    {
        public const string View = "partners.view";
        public const string Create = "partners.create";
        public const string Edit = "partners.edit";
        public const string Delete = "partners.delete";
    }

    public static class Documents
    {
        public const string View = "documents.view";
        public const string Create = "documents.create";
        public const string Edit = "documents.edit";
        public const string Delete = "documents.delete";
    }

    public static class GlobalValues
    {
        public const string View = "global-values.view";
        public const string Create = "global-values.create";
        public const string Edit = "global-values.edit";
        public const string Delete = "global-values.delete";
    }

    public static class Notifiers
    {
        public const string View = "notifiers.view";
        public const string Create = "notifiers.create";
        public const string Edit = "notifiers.edit";
        public const string Delete = "notifiers.delete";
    }

    public static class ApiGateways
    {
        public const string View = "api-gateways.view";
        public const string Create = "api-gateways.create";
        public const string Edit = "api-gateways.edit";
        public const string Delete = "api-gateways.delete";
    }

    public static class BusGateways
    {
        public const string View = "bus-gateways.view";
        public const string Create = "bus-gateways.create";
        public const string Edit = "bus-gateways.edit";
        public const string Delete = "bus-gateways.delete";
    }

    public static class WorkGroups
    {
        public const string View = "workgroups.view";
        public const string Create = "workgroups.create";
        public const string Edit = "workgroups.edit";
        public const string Delete = "workgroups.delete";
    }

    public static class RetryPolicies
    {
        public const string View = "retry-policies.view";
        public const string Create = "retry-policies.create";
        public const string Edit = "retry-policies.edit";
        public const string Delete = "retry-policies.delete";
        public const string Operate = "retry-policies.operate";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Edit = "users.edit";
        public const string Delete = "users.delete";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Create = "roles.create";
        public const string Edit = "roles.edit";
        public const string Delete = "roles.delete";
    }

    public static class Settings
    {
        public const string View = "settings.view";
        public const string Edit = "settings.edit";
    }
}

public class PermissionActionModel
{
    public string Id { get; set; }

    /// <summary>What this specific grant allows, in end-user words.</summary>
    public string Description { get; set; }
}

public class PermissionAreaModel
{
    public string Id { get; set; }
    public string Label { get; set; }

    /// <summary>Mirrors the app's navigation groups, so a role's grants map onto what its members see.</summary>
    public string Group { get; set; }

    public string Description { get; set; }
    public List<PermissionActionModel> Actions { get; set; } = [];
}

public static class PermissionCatalog
{
    public const string View = "view";
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Operate = "operate";

    private static PermissionAreaModel Area(string id, string label, string group, string description,
        params (string Id, string Description)[] actions) => new()
    {
        Id = id,
        Label = label,
        Group = group,
        Description = description,
        Actions = actions.Select(a => new PermissionActionModel { Id = a.Id, Description = a.Description }).ToList()
    };

    public static readonly List<PermissionAreaModel> Areas =
    [
        // ——— Operate ———
        Area("exchanges", "Exchanges", "Operate", "Every message that flows through Bitween.",
            (View, "Browse exchanges, payloads and traces."),
            (Operate, "Retry or resubmit failed exchanges.")),

        Area("monitoring", "Queue health", "Operate", "Live message-queue throughput and consumers.",
            (View, "See queue health and rates.")),

        Area("dashboard", "Dashboard", "Operate", "Traffic and health overview (reached from the logo).",
            (View, "See the dashboard.")),

        // ——— Integrations ———
        Area("subscriptions", "Integrations", "Integrations", "The configured pipelines that process exchanges.",
            (View, "Browse integrations and their configuration."),
            (Create, "Create integrations."),
            (Edit, "Change adapters, mappings and settings."),
            (Delete, "Delete integrations."),
            (Operate, "Pause, resume, receive now, aggregate now.")),

        Area("partners", "Partners", "Integrations", "The external parties you exchange data with.",
            (View, "Browse partners and their properties."),
            (Create, "Create partners."),
            (Edit, "Change partner details, properties and API keys."),
            (Delete, "Delete partners.")),

        Area("documents", "Information types", "Integrations",
            "The kinds of business documents that flow between partners.",
            (View, "Browse information types."),
            (Create, "Create information types."),
            (Edit, "Change information types, codes and promoted properties."),
            (Delete, "Delete unused information types.")),

        Area("global-values", "Global values", "Integrations", "Shared value sets adapters can reference.",
            (View, "Browse global value sets."),
            (Create, "Create value sets."),
            (Edit, "Change value sets."),
            (Delete, "Delete value sets.")),

        Area("notifiers", "Notifiers", "Integrations", "Alerts sent when exchanges fail or succeed.",
            (View, "Browse notifiers and their delivery history."),
            (Create, "Create notifiers."),
            (Edit, "Change notifiers."),
            (Delete, "Delete notifiers.")),

        Area("api-gateways", "API gateways", "Integrations", "HTTP entry points partners call into.",
            (View, "Browse API gateways and attached partners."),
            (Create, "Create new API gateways."),
            (Edit, "Change gateways and partner attachments."),
            (Delete, "Delete API gateways.")),

        Area("bus-gateways", "Bus gateways", "Integrations", "Bus listeners that route documents to integrations.",
            (View, "Browse bus gateways and routes."),
            (Create, "Create new bus gateways."),
            (Edit, "Change gateways and routes."),
            (Delete, "Delete bus gateways.")),

        // ——— Configuration ———
        Area("workgroups", "Work groups", "Configuration", "Processing lanes that spread load across queues.",
            (View, "See work groups and their throughput."),
            (Create, "Create work groups."),
            (Edit, "Change work group settings."),
            (Delete, "Delete unused work groups.")),

        Area("retry-policies", "Retry policies", "Configuration", "Rules for retrying failed exchanges.",
            (View, "Browse retry policies and scheduled retries."),
            (Create, "Create retry policies."),
            (Edit, "Change retry policies."),
            (Delete, "Delete retry policies."),
            (Operate, "Run scheduled retries now.")),

        // ——— Administration ———
        Area("users", "Members", "Administration", "The people who can sign in to this Bitween instance.",
            (View, "See the member list."),
            (Create, "Invite new members."),
            (Edit, "Change members' roles, disable accounts, reset passwords."),
            (Delete, "Remove members.")),

        Area("roles", "Roles", "Administration", "What each kind of member is allowed to do.",
            (View, "See roles and their permissions."),
            (Create, "Create roles."),
            (Edit, "Change role permissions."),
            (Delete, "Delete unassigned roles.")),

        Area("settings", "Settings", "Administration", "Instance-wide configuration.",
            (View, "See instance settings."),
            (Edit, "Change instance settings."))
    ];

    /// <summary>Every valid permission key.</summary>
    public static readonly HashSet<string> AllKeys =
        Areas.SelectMany(a => a.Actions.Select(x => $"{a.Id}.{x.Id}")).ToHashSet();

    /// <summary>Drops anything not in the catalog — stale keys left over from a removed area.</summary>
    public static List<string> Sanitize(IEnumerable<string> keys) =>
        (keys ?? []).Where(AllKeys.Contains).Distinct().ToList();

    /// <summary>Every key in the given navigation groups, optionally view-only.</summary>
    public static List<string> InGroups(bool viewOnly, params string[] groups) =>
        Areas.Where(a => groups.Contains(a.Group))
            .SelectMany(a => a.Actions.Where(x => !viewOnly || x.Id == View).Select(x => $"{a.Id}.{x.Id}"))
            .ToList();
}

using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>Turns object IDs found in policies and assignments into readable names using the captured directory collections.</summary>
public sealed class NameResolver
{
    private static readonly IReadOnlyDictionary<string, string> WellKnown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["All"] = "All",
        ["None"] = "None",
        ["GuestsOrExternalUsers"] = "Guests or external users",
        ["Office365"] = "Office 365 (all)",
        ["MicrosoftAdminPortals"] = "Microsoft Admin Portals",
        ["AllTrusted"] = "All trusted locations",
        ["00000002-0000-0ff1-ce00-000000000000"] = "Exchange Online",
        ["00000003-0000-0ff1-ce00-000000000000"] = "SharePoint Online",
        ["00000003-0000-0000-c000-000000000000"] = "Microsoft Graph"
    };

    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The directory collections whose names win if an ID appears in more than one collection.</summary>
    private static readonly string[] DirectoryCollections = { "users", "groups", "directoryRoles", "namedLocations", "apps", "licences" };

    public static NameResolver FromSnapshot(TenantSnapshot? snapshot) => FromSnapshot(snapshot, null);

    /// <summary>
    /// Names for every object the capture holds, plus the client's recorded exclusion accounts. Every collection is
    /// indexed, not only the directory ones, so a policy, app or location referenced by ID elsewhere reads by name too.
    /// Users read as "Display name (sign-in name)", and directory roles are also indexed by their role template ID,
    /// which is how Conditional Access refers to them. Display only: nothing decides anything from a name.
    /// </summary>
    public static NameResolver FromSnapshot(TenantSnapshot? snapshot, TenantProfile? profile)
    {
        var resolver = new NameResolver();
        if (snapshot is not null)
        {
            foreach (var key in DirectoryCollections)
                if (snapshot.Collections.TryGetValue(key, out var capture))
                    foreach (var item in capture.Items) resolver.Index(key, item, overwrite: true);
            foreach (var (key, capture) in snapshot.Collections)
                if (!DirectoryCollections.Contains(key, StringComparer.Ordinal))
                    foreach (var item in capture.Items) resolver.Index(key, item, overwrite: false);
        }
        // Emergency and exclusion accounts are resolved by the engineer when they are chosen, so they have names even
        // when the capture holds no users collection.
        foreach (var account in profile?.ExclusionAccounts ?? new())
        {
            var name = UserName(account.DisplayName, account.UserPrincipalName);
            if (name.Length > 0 && !string.IsNullOrWhiteSpace(account.ObjectId)) resolver._names.TryAdd(account.ObjectId, name);
        }
        return resolver;
    }

    private void Index(string collection, JsonObject item, bool overwrite)
    {
        var id = Text(item["id"]);
        var name = collection == "users"
            ? UserName(Text(item["displayName"]), Text(item["userPrincipalName"]))
            : Text(item["displayName"]) ?? Text(item["name"]) ?? Text(item["userPrincipalName"]) ?? Text(item["skuPartNumber"]) ?? "";
        if (name.Length == 0) return;
        if (!string.IsNullOrEmpty(id))
        {
            if (overwrite) _names[id] = name; else _names.TryAdd(id, name);
        }
        if (collection == "directoryRoles" && Text(item["roleTemplateId"]) is { Length: > 0 } template) _names.TryAdd(template, name);
    }

    private static string UserName(string? displayName, string? userPrincipalName) =>
        (displayName, userPrincipalName) switch
        {
            ({ Length: > 0 } d, { Length: > 0 } u) when !string.Equals(d, u, StringComparison.OrdinalIgnoreCase) => $"{d} ({u})",
            ({ Length: > 0 } d, _) => d,
            (_, { Length: > 0 } u) => u,
            _ => ""
        };

    /// <summary>
    /// A captured property as text, or null when absent or not a string. Names are display only, and every page that
    /// shows tenant objects builds a resolver from the whole capture, so one unexpected value must not stop it.
    /// </summary>
    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public void Add(string id, string name)
    {
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) _names[id] = name;
    }

    public bool Knows(string id) => _names.ContainsKey(id) || WellKnown.ContainsKey(id);

    public string Display(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (_names.TryGetValue(id, out var name)) return $"{name} [{id}]";
        if (WellKnown.TryGetValue(id, out var known)) return known;
        return ProfileValidator.IsGuid(id) ? $"Unresolved [{id}]" : id;
    }

    /// <summary>Renders a node as compact text with known IDs replaced by names. Used for report cells.</summary>
    public string Render(JsonNode? node)
    {
        switch (node)
        {
            case null: return "";
            case JsonValue v when v.TryGetValue<string>(out var s): return Knows(s) ? Display(s) : s;
            case JsonValue v: return CanonicalJson.ScalarText(v);
            case JsonArray arr:
                return arr.Count == 0 ? "[]" : string.Join("; ", arr.Select(Render));
            case JsonObject obj:
                return obj.Count == 0 ? "{}" : string.Join("; ", obj.Select(p => p.Key + "=" + Render(p.Value)));
            default: return node.ToJsonString();
        }
    }

    /// <summary>Summarises Conditional Access user targeting or Intune assignments in one line.</summary>
    public string AssignmentSummary(JsonObject item)
    {
        if (item["conditions"] is JsonObject conditions && conditions["users"] is JsonObject users)
        {
            var parts = new List<string>();
            foreach (var key in new[] { "includeUsers", "includeGroups", "includeRoles", "excludeUsers", "excludeGroups", "excludeRoles" })
                if (users[key] is JsonArray arr && arr.Count > 0)
                    parts.Add(key + ": " + string.Join(", ", arr.Select(Render)));
            return parts.Count == 0 ? "No user targeting" : string.Join(" | ", parts);
        }
        if (item[Collection.TenantCollector.AssignmentsUnknownKey] is not null) return "Assignments could not be read";
        if (item[Collection.TenantCollector.AssignmentsKey] is JsonArray assignments)
        {
            if (assignments.Count == 0) return "Unassigned";
            var targets = new List<string>();
            foreach (var a in assignments)
            {
                if (a is not JsonObject ao || ao["target"] is not JsonObject target) continue;
                var type = Text(target["@odata.type"]) ?? "";
                var groupId = Text(target["groupId"]);
                var label = type.Contains("allLicensedUsers", StringComparison.OrdinalIgnoreCase) ? "All users"
                    : type.Contains("allDevices", StringComparison.OrdinalIgnoreCase) ? "All devices"
                    : type.Contains("exclusion", StringComparison.OrdinalIgnoreCase) ? "Exclude " + Display(groupId)
                    : groupId is not null ? Display(groupId) : type;
                if (label.Length > 0) targets.Add(label);
            }
            return targets.Count == 0 ? "Assigned (targets unreadable)" : string.Join(", ", targets);
        }
        return "";
    }
}

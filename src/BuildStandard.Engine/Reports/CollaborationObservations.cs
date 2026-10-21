using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
/// <summary>Direct returned metadata only; never effective access or inferred resource ownership.</summary>
public static class CollaborationObservations
{
    public static CollaborationSiteRow Site(JsonObject value, string? selectedId = null)
    {
        var id = Text(value, "id", 512);
        if (selectedId is not null)
        {
            _ = CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteMetadata, selectedId);
            if (id is not null && id != selectedId) throw new TenantMismatchException("Returned site identity differs from the selected site.");
        }
        var row = new CollaborationSiteRow
        {
            Id = id, Name = Text(value, "displayName"), ReportedName = Text(value, "name"),
            SiteCollectionHost = value["siteCollection"] is JsonObject collection ? Text(collection, "hostname") : null,
            WebUrl = Text(value, "webUrl"), CreatedAt = Time(value, "createdDateTime"), ModifiedAt = Time(value, "lastModifiedDateTime")
        };
        if (id is null || row.Name is null || row.ReportedName is null || row.SiteCollectionHost is null || row.WebUrl is null || row.CreatedAt is null || row.ModifiedAt is null)
        { row.ReadStatus = ReportReadState.Partial; row.Error = "Returned site metadata is incomplete. Search visibility and hostname do not establish ownership or complete estate coverage."; }
        else row.ReadStatus = ReportReadState.Collected;
        return row;
    }
    public static CollaborationLibraryRow Library(JsonObject value, string selectedSiteId)
    {
        _ = CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteLibraries, selectedSiteId);
        var id = Text(value, "id", 512); var owner = value["owner"] as JsonObject;
        var user = owner?["user"] as JsonObject; var group = owner?["group"] as JsonObject;
        var row = new CollaborationLibraryRow
        {
            Id = TupleIdentity(selectedSiteId, id), SiteId = selectedSiteId, DriveId = id, Name = Text(value, "name"),
            DriveType = Text(value, "driveType"), WebUrl = Text(value, "webUrl"),
            OwnerUserId = user is null ? null : Text(user, "id", 512), OwnerUserName = user is null ? null : Text(user, "displayName"),
            OwnerGroupId = group is null ? null : Text(group, "id", 512), OwnerGroupName = group is null ? null : Text(group, "displayName")
        };
        if (id is null || row.Name is null || row.WebUrl is null || row.DriveType != "documentLibrary" || row.OwnerUserId is null && row.OwnerGroupId is null)
        { row.ReadStatus = ReportReadState.Partial; row.Error = "Existing library metadata or reported owner identity is incomplete or unclassified. No owner membership, permissions or OneDrive estate was inferred."; }
        else row.ReadStatus = ReportReadState.Collected;
        return row;
    }
    public static CollaborationOwnerRow Owner(JsonObject value, string selectedGroupId)
    {
        _ = CollaborationInventoryRegistry.Path(CollaborationReadKind.GroupOwners, selectedGroupId);
        var id = Text(value, "id", 512);
        return new() { Id = TupleIdentity(selectedGroupId, id), GroupId = selectedGroupId, ObjectId = id, Name = Text(value, "displayName"),
            ResourceType = Text(value, "@odata.type"), Coverage = OwnerCoverage, ReadStatus = ReportReadState.Partial, Error = OwnerCoverage };
    }
    public const string OwnerCoverage = "Graph v1.0 omits service-principal owners. Exchange-created, distribution and synchronised groups are unsupported; unknown Exchange provenance prevents complete coverage. Returned empty owners never prove no owners exist.";
    public static CollaborationGroupRow Group(JsonObject value, string selectedId)
    {
        if (!ReportValues.IsCanonicalGuid(selectedId)) throw new ConfigurationException("An exact selected group GUID is required.");
        var id = GuidValue(value, "id");
        if (id is not null && id != selectedId) throw new TenantMismatchException("Returned group identity differs from the selected group.");
        var types = Strings(value, "groupTypes");
        var options = Strings(value, "resourceProvisioningOptions");
        var mail = Boolean(value, "mailEnabled"); var security = Boolean(value, "securityEnabled"); var sync = Boolean(value, "onPremisesSyncEnabled");
        var unsupported = CollaborationPopulation.OwnersUnavailable(value);
        return new()
        {
            Id = id, Name = Text(value, "displayName"), GroupTypes = types, MailEnabled = mail, SecurityEnabled = security,
            OnPremisesSyncEnabled = sync, ProvisioningOptions = options,
            SyncMeaning = sync == true ? "Synced" : sync == false ? "PreviouslySynced" : value.TryGetPropertyValue("onPremisesSyncEnabled", out var syncValue) && syncValue is null ? "NeverSynced" : "Unknown",
            TeamProvisioned = CollaborationPopulation.HasTeam(value),
            OwnerReadDisposition = unsupported ? "BlockedUnsupportedPopulation" : "ReadWithCoverageLimits",
            OwnerCoverage = OwnerCoverage, ReadStatus = ReportReadState.Partial,
            Error = unsupported ? "Owner enumeration is unsupported for this observed group population. Use manual verification; no empty-success substitute." : OwnerCoverage
        };
    }
    public static CollaborationTeamMemberRow Member(JsonObject value, string selectedTeamId)
    {
        if (!ReportValues.IsCanonicalGuid(selectedTeamId)) throw new ConfigurationException("An exact selected Team GUID is required.");
        var member = Text(value, "id", 512); var roles = Strings(value, "roles"); var type = Text(value, "@odata.type");
        var knownType = type is "#microsoft.graph.aadUserConversationMember" or "microsoft.graph.aadUserConversationMember";
        var meaning = roles is null ? "Unknown" : !knownType || roles.Any(role => role is not ("owner" or "guest")) || roles.Distinct(StringComparer.Ordinal).Count() != roles.Count
            ? "Unclassified" : roles.Count == 0 ? "Member" : roles.Count == 2 ? "Owner and guest" : roles[0] == "owner" ? "Owner" : "Guest";
        var row = new CollaborationTeamMemberRow
        {
            Id = TupleIdentity(selectedTeamId, member),
            TeamId = selectedTeamId, MemberId = member, ResourceType = type, Name = Text(value, "displayName"),
            UserId = GuidValue(value, "userId"), UserTenantId = GuidValue(value, "tenantId"), Roles = roles, RoleMeaning = meaning
        };
        if (member is null || row.Name is null || row.UserId is null || row.UserTenantId is null || meaning is "Unknown" or "Unclassified")
        { row.ReadStatus = ReportReadState.Partial; row.Error = "Member identity, type or returned roles are missing, malformed or unclassified. Tenant difference is not a guest-role inference."; }
        else row.ReadStatus = ReportReadState.Collected;
        return row;
    }
    private static string TupleIdentity(string parent, string? child)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { parent, child ?? "" }))));
    private static string? Time(JsonObject value, string key)
    {
        var text = Text(value, key);
        return DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? Timestamps.Format(parsed) : null;
    }
    private static string? Text(JsonObject value, string key, int maximum = 4096)
        => value[key] is JsonValue scalar && scalar.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            && text.Length <= maximum && !text.Any(char.IsControl) ? text : null;
    private static string? GuidValue(JsonObject value, string key)
    { var text = Text(value, key); return ReportValues.IsCanonicalGuid(text) ? text : null; }
    private static bool? Boolean(JsonObject value, string key)
        => value[key] is JsonValue scalar && scalar.TryGetValue<bool>(out var flag) ? flag : null;
    private static List<string>? Strings(JsonObject value, string key)
    {
        if (value[key] is not JsonArray array || array.Count > OperationalReportRegistry.MaximumRoles) return null;
        var result = new List<string>();
        foreach (var item in array)
        {
            if (item is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > 512 || text.Any(char.IsControl)) return null;
            result.Add(text);
        }
        return result;
    }
}

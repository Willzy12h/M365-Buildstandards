using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
public static partial class OperationalContextSchema
{
    public const string CollaborationNotice = "Bounded selected metadata and direct roles, not a complete tenant estate, resource ownership or effective access. Site permissions require unapproved Sites.FullControl.All and remain blocked. User/me OneDrive discovery can provision storage and is blocked. Content, sharing links and inheritance are not checked. " + CollaborationObservations.OwnerCoverage;
    internal sealed record CollaborationSection(string Id, CollaborationReadKind Kind, string Target, Type RowType);
    internal static IReadOnlyList<CollaborationSection> CollaborationSections(CollaborationReadSelection selection)
    {
        var result = new List<CollaborationSection>();
        if (selection.Search is not null) result.Add(new("site-search", CollaborationReadKind.SiteSearch, selection.Search, typeof(CollaborationSiteRow)));
        for (var i = 0; i < selection.Sites.Count; i++)
        {
            result.Add(new("site-" + i, CollaborationReadKind.SiteMetadata, selection.Sites[i], typeof(CollaborationSiteRow)));
            if (selection.Libraries) result.Add(new("libraries-" + i, CollaborationReadKind.SiteLibraries, selection.Sites[i], typeof(CollaborationLibraryRow)));
        }
        for (var i = 0; i < selection.Groups.Count; i++)
        {
            result.Add(new("group-" + i, CollaborationReadKind.GroupMetadata, selection.Groups[i], typeof(CollaborationGroupRow)));
            if (selection.Owners) result.Add(new("owners-" + i, CollaborationReadKind.GroupOwners, selection.Groups[i], typeof(CollaborationOwnerRow)));
            if (selection.Members) result.Add(new("members-" + i, CollaborationReadKind.TeamMembers, selection.Groups[i], typeof(CollaborationTeamMemberRow)));
        }
        return result;
    }
    internal static CollaborationReadSelection FrozenSelection(CollaborationReportSelection? selection)
    {
        if (selection?.Sites is null || selection.Groups is null || selection.ReturnedCounts is null || selection.ReturnedCounts.Count > 151)
            throw Bad("Missing or oversized collaboration selection.");
        return new(selection.Search, selection.Sites, selection.Groups, selection.Libraries, selection.Owners, selection.Members);
    }
    private static void ValidateCollaboration(OperationalContextReport report)
    {
        if (report.CustomerTenantId is not null || report.PartnerCoverage is not null) throw Bad("Collaboration is not partner/customer relationship evidence.");
        var frozen = FrozenSelection(report.CollaborationSelection);
        var selected = CollaborationSections(frozen);
        var counts = report.CollaborationSelection!.ReturnedCounts;
        if (report.Sections is null || report.Sections.Count != selected.Count || counts.Count != selected.Count) throw Bad("Collaboration source selection/order differs.");
        var declarations = SourceDeclarations(report.ReportId);
        if (report.Sources is null || report.Sources.Count != declarations.Count || report.Sources.Where((s,i) => s is null || s.Api != declarations[i].Api || s.RegisteredRoute != declarations[i].RegisteredRoute || s.Reference != declarations[i].Reference).Any())
            throw Bad("Collaboration declarations differ from the registered read surface.");
        var parents = new Dictionary<string, GraphReportRow>(StringComparer.Ordinal);
        var total = selected.Count; // capture reserves one diagnostic per source within the row bound
        for (var i = 0; i < selected.Count; i++)
        {
            var definition = selected[i]; var section = report.Sections[i]; var count = counts[i];
            if (section is null || section.Id != definition.Id || section.Rows is null || section.Limitations is null || !section.Limitations.Contains(CollaborationNotice, StringComparer.Ordinal))
                throw Bad("Missing collaboration source or mandatory coverage/blocked-boundary guidance.");
            RequireState(section.Status, section.Error);
            foreach (var limitation in section.Limitations) Text(limitation);
            total = checked(total + section.Rows.Count);
            if (total > OperationalReportRegistry.MaximumRows) throw Bad("Collaboration rows and diagnostics exceed5000.");
            if (section.Status is ReportReadState.NotAttempted or ReportReadState.Failed or ReportReadState.Cancelled)
            {
                if (count is not null || section.Rows.Count != 0) throw Bad("Failed, cancelled or unattempted source cannot claim a known count or retained rows without Partial.");
            }
            if (count is not null && count != section.Rows.Count || section.Status == ReportReadState.Collected && count is null)
                throw Bad("Returned count conceals incomplete collection or differs from observations.");
            if (definition.Kind is CollaborationReadKind.SiteLibraries or CollaborationReadKind.GroupOwners or CollaborationReadKind.TeamMembers
                && section.Status != ReportReadState.NotAttempted)
            {
                var key = (definition.Kind == CollaborationReadKind.SiteLibraries ? "site:" : "group:") + definition.Target;
                if (!parents.TryGetValue(key, out var parent)) throw Bad("Attempted child, including empty or failed reads, requires admitted selected parent metadata.");
                if (parent is CollaborationGroupRow group && (definition.Kind == CollaborationReadKind.GroupOwners && group.OwnerReadDisposition == "BlockedUnsupportedPopulation"
                    || definition.Kind == CollaborationReadKind.TeamMembers && group.TeamProvisioned != true)) throw Bad("Unsupported or unknown parent population cannot claim a child attempt.");
            }
            var typedRows = new List<GraphReportRow>();
            foreach (var raw in section.Rows)
            {
                GraphReportRow row;
                try { row = (GraphReportRow)(raw?.Deserialize(definition.RowType, RegisteredReportJson.Strict) ?? throw Bad("Missing collaboration row.")); }
                catch (JsonException) { throw Bad("Collaboration observation does not match its selected source type."); }
                RequireState(row.ReadStatus, row.Error);
                if (row.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)) throw Bad("Returned collaboration object must be observed or partial.");
                foreach (var field in raw!)
                {
                    if (field.Value is JsonObject) throw Bad("Nested raw collaboration content is not admitted.");
                    if (field.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) Text(text);
                    if (field.Value is JsonArray array)
                    {
                        if (array.Count > 100) throw Bad("Collaboration list exceeds100.");
                        foreach (var item in array) if (item is not JsonValue v || !v.TryGetValue<string>(out var value) || !Identity(value)) throw Bad("Invalid collaboration list value.");
                    }
                }
                if (row.Id is not null && !Identity(row.Id)) throw Bad("Malformed collaboration identity.");
                var expected = ReprojectCollaboration(row, definition);
                if (row.Id != expected.Id || row.ReadStatus == ReportReadState.Collected && expected.ReadStatus != ReportReadState.Collected)
                    throw Bad("Collaboration identity/completeness differs from returned field meaning.");
                if (row is CollaborationGroupRow actualGroup && expected is CollaborationGroupRow expectedGroup
                    && (actualGroup.OwnerReadDisposition != expectedGroup.OwnerReadDisposition || actualGroup.TeamProvisioned != expectedGroup.TeamProvisioned || actualGroup.OwnerCoverage != CollaborationObservations.OwnerCoverage))
                    throw Bad("Group population or Team meaning was changed.");
                if (row is CollaborationTeamMemberRow actualMember && expected is CollaborationTeamMemberRow expectedMember
                    && (actualMember.RoleMeaning != expectedMember.RoleMeaning || actualMember.UserId != expectedMember.UserId || actualMember.UserTenantId != expectedMember.UserTenantId))
                    throw Bad("Direct member roles or user/tenant identifiers were reinterpreted.");
                if (row is CollaborationOwnerRow owner && owner.Coverage != CollaborationObservations.OwnerCoverage) throw Bad("Owner population coverage is missing.");
                typedRows.Add(row);
            }
            foreach (var duplicate in typedRows.Where(r => r.Id is not null).GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                if (duplicate.Any(r => r.ReadStatus != ReportReadState.Partial)) throw Bad("Duplicate observations must remain partial.");
            if (section.Status == ReportReadState.Collected && typedRows.Any(r => r.ReadStatus != ReportReadState.Collected)) throw Bad("Source conceals incomplete observations.");
            if (definition.Kind == CollaborationReadKind.GroupOwners && section.Status == ReportReadState.Collected) throw Bad("Owner enumeration cannot establish complete population coverage, even with zero returned owners.");
            if (definition.Kind is CollaborationReadKind.SiteMetadata or CollaborationReadKind.GroupMetadata)
            {
                if (typedRows.Count > 1 || typedRows.Any(r => r.Id != definition.Target) || count is not null && count != 1)
                    throw Bad("Selected metadata must be exactly one identified parent.");
                if (count == 1) parents.Add((definition.Kind == CollaborationReadKind.SiteMetadata ? "site:" : "group:") + definition.Target, typedRows.Single());
            }
        }
    }
    private static GraphReportRow ReprojectCollaboration(GraphReportRow row, CollaborationSection selected)
    {
        static JsonNode? Strings(List<string>? values) => values is null ? null : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        var raw = new JsonObject { ["id"] = row.Id, ["displayName"] = row.Name };
        switch (row)
        {
            case CollaborationSiteRow site:
                raw["name"] = site.ReportedName; raw["siteCollection"] = new JsonObject { ["hostname"] = site.SiteCollectionHost };
                raw["webUrl"] = site.WebUrl; raw["createdDateTime"] = site.CreatedAt; raw["lastModifiedDateTime"] = site.ModifiedAt;
                foreach (var stamp in new[] { site.CreatedAt, site.ModifiedAt }) if (stamp is not null && !Utc(stamp, out _)) throw Bad("Invalid site timestamp.");
                return CollaborationObservations.Site(raw, selected.Kind == CollaborationReadKind.SiteMetadata ? selected.Target : null);
            case CollaborationGroupRow group:
                raw["groupTypes"] = Strings(group.GroupTypes); raw["mailEnabled"] = group.MailEnabled; raw["securityEnabled"] = group.SecurityEnabled;
                raw["resourceProvisioningOptions"] = Strings(group.ProvisioningOptions);
                if (group.SyncMeaning is not ("Synced" or "PreviouslySynced" or "NeverSynced" or "Unknown")
                    || group.SyncMeaning != (group.OnPremisesSyncEnabled == true ? "Synced" : group.OnPremisesSyncEnabled == false ? "PreviouslySynced" : group.SyncMeaning == "NeverSynced" ? "NeverSynced" : "Unknown")) throw Bad("Sync observation meaning is inconsistent.");
                if (group.SyncMeaning != "Unknown") raw["onPremisesSyncEnabled"] = group.OnPremisesSyncEnabled;
                return CollaborationObservations.Group(raw, selected.Target);
            case CollaborationLibraryRow library:
                if (library.SiteId != selected.Target) throw Bad("Library has another parent.");
                raw["id"] = library.DriveId; raw["name"] = library.Name; raw["driveType"] = library.DriveType; raw["webUrl"] = library.WebUrl;
                raw["owner"] = new JsonObject { ["user"] = new JsonObject { ["id"] = library.OwnerUserId, ["displayName"] = library.OwnerUserName }, ["group"] = new JsonObject { ["id"] = library.OwnerGroupId, ["displayName"] = library.OwnerGroupName } };
                return CollaborationObservations.Library(raw, selected.Target);
            case CollaborationOwnerRow owner:
                if (owner.GroupId != selected.Target) throw Bad("Owner has another parent.");
                raw["id"] = owner.ObjectId; raw["@odata.type"] = owner.ResourceType;
                return CollaborationObservations.Owner(raw, selected.Target);
            case CollaborationTeamMemberRow member:
                if (member.TeamId != selected.Target) throw Bad("Member has another Team.");
                raw["id"] = member.MemberId; raw["@odata.type"] = member.ResourceType; raw["userId"] = member.UserId;
                raw["tenantId"] = member.UserTenantId; raw["roles"] = Strings(member.Roles);
                return CollaborationObservations.Member(raw, selected.Target);
            default: throw Bad("Unregistered collaboration row.");
        }
    }
}

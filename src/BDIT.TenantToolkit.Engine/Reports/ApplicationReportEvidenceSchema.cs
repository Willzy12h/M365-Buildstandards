using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Strict schema 2 readers, typed parameters and registered metadata-only rows. Never converts schema 1.</summary>
public static class ApplicationReportEvidenceSchema
{
    public const int MaximumBytes = ReportEvidenceSchema.MaximumBytes;
    public const string HistoricalOwnerNotice = "Historical owner outcome consistency unverified";
    public static bool IsHistoricalExpiry(ApplicationReportEvidence e) => e is ApplicationExpiryEvidence
        && e.ReportSchemaVersion == 1 && e.ModuleVersion == ApplicationReportRegistry.AdapterVersion;
    public static string Serialise(ApplicationReportEvidence evidence) => JsonSerializer.Serialize(evidence, evidence.GetType(), RegisteredReportJson.Strict);
    public static JsonObject Row(GraphReportRow row) => JsonSerializer.SerializeToNode(row, row.GetType(), RegisteredReportJson.Strict)!.AsObject();
    public static T ReadRow<T>(JsonObject row) where T : GraphReportRow =>
        JsonSerializer.Deserialize<T>(row.ToJsonString(), RegisteredReportJson.Strict) ?? throw Bad("Empty application report row.");
    public static ApplicationReportEvidence Read(string json, string expectedTenantId) => ReadCore(json, expectedTenantId, false);
    public static ApplicationReportEvidence ReadHistorical(string json, string expectedTenantId) => ReadCore(json, expectedTenantId, true);
    private static ApplicationReportEvidence ReadCore(string json, string expectedTenantId, bool historical)
    {
        using var document = RegisteredReportJson.Parse(json);
        var (schema, id) = RegisteredReportJson.Inspect(document.RootElement);
        var definition = ApplicationReportRegistry.Find(schema, id);
        try
        {
            var report = (ApplicationReportEvidence)(JsonSerializer.Deserialize(json, definition.EnvelopeType, RegisteredReportJson.Strict)
                ?? throw Bad("Empty application report envelope."));
            var sourceNodes = document.RootElement.GetProperty("sources");
            if (sourceNodes.ValueKind != JsonValueKind.Array) throw Bad("Invalid application sources.");
            foreach (var source in sourceNodes.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object) throw Bad("Invalid application source.");
                if (source.TryGetProperty("retainedOwnerOutcomes", out _) && (report is not ApplicationExpiryEvidence
                    || report.ReportSchemaVersion != 2 || !source.TryGetProperty("registeredRoute", out var route)
                    || route.GetString() is not ("/applications/{id}/owners" or "/servicePrincipals/{id}/owners")))
                    throw Bad("Owner outcomes are not registered for this source/revision, including explicit null fields.");
            }
            if (historical) ValidateHistorical(report, expectedTenantId); else Validate(report, expectedTenantId);
            return report;
        }
        catch (JsonException) { throw Bad("Invalid registered application report fields or parameters."); }
    }
    public static void Seal(ApplicationReportEvidence evidence)
    {
        // Object serialisation retains the concrete envelope's typed parameters in the shared integrity algorithm.
        evidence.IntegrityDigest = EvidenceIntegrity.Compute<object>(evidence); Validate(evidence, evidence.TenantId);
    }
    public static void Validate(ApplicationReportEvidence e, string expectedTenantId)
        => ValidateCore(e, expectedTenantId, false);
    public static void ValidateHistorical(ApplicationReportEvidence e, string expectedTenantId)
    {
        if (!IsHistoricalExpiry(e)) throw Bad("Historical inspection requires original expiry report revision 1 and its registered adapter.");
        ValidateCore(e, expectedTenantId, true);
    }
    private static void ValidateCore(ApplicationReportEvidence e, string expectedTenantId, bool historical)
    {
        if (e.SchemaVersion != 2 || e.Kind != "reportEvidence" || e.Resource != "Graph")
            throw Bad("Unsupported application report kind, resource or schema.");
        var definition = ApplicationReportRegistry.Find(e.SchemaVersion, e.ReportId);
        if (IsHistoricalExpiry(e) && !historical)
            throw Bad(HistoricalOwnerNotice + "; use explicit limited historical inspection/export. This record cannot enter current-revision owner validation.");
        if (!historical && (e.ReportSchemaVersion != definition.ReportSchemaVersion || e.ModuleVersion != definition.RegisteredAdapter))
            throw Bad("Unregistered application report revision/adapter pair.");
        if (e.GetType() != definition.EnvelopeType) throw Bad("Report ID and typed envelope do not match.");
        Guid(e.Id); Guid(e.TenantId);
        if (!ReportValues.TryGuid(expectedTenantId, out var expected) || e.TenantId != expected)
            throw new TenantMismatchException("Application report evidence belongs to another tenant.");
        if (e.SourceMode == "live") Guid(e.AccountObjectId);
        else if (e.SourceMode == "historical") { if (e.AccountObjectId is not null) Guid(e.AccountObjectId); }
        else throw Bad("Unknown report source mode.");
        if (!Utc(e.StartedAt, out var started) || !Utc(e.EndedAt, out var ended) || ended < started)
            throw Bad("Invalid report collection interval.");
        Text(e.ToolkitVersion, 128, true);
        switch (e)
        {
            case ApplicationExpiryEvidence expiry: ValidateParameters(expiry.Parameters); break;
            case ApplicationConsentEvidence consent when consent.Parameters is not null: break;
            default: throw Bad("Missing application report parameters.");
        }
        Strings(e.Limitations);
        if (e.Sources is null || e.Sources.Count != definition.Routes.Count || e.Sources.Any(s => s is null)
            || !e.Sources.Select(s => s.RegisteredRoute).SequenceEqual(definition.Routes.Select(r => r.Path), StringComparer.Ordinal))
            throw Bad("Application report sources do not match the schema/ID registration.");
        foreach (var source in e.Sources)
        {
            var ownerSource = e is ApplicationExpiryEvidence && source.RegisteredRoute.EndsWith("/owners", StringComparison.Ordinal);
            if (ownerSource && !historical && source.RetainedOwnerOutcomes is null)
                throw Bad("Current expiry owner sources require retainedOwnerOutcomes.");
            if ((!ownerSource || historical) && source.RetainedOwnerOutcomes is not null)
                throw Bad("Owner outcomes cannot be added to historical or non-owner sources.");
            if (source.Api != "v1.0" || source.Reference != definition.Reference || source.Rows < 0 || source.Rows > (source.RegisteredRoute.EndsWith("/owners", StringComparison.Ordinal) ? ApplicationReportRegistry.MaximumChildObjects * ApplicationReportRegistry.MaximumOwners : ApplicationReportRegistry.MaximumRows)
                || source.ObjectsAttempted < 0 || source.ObjectsAttempted > ApplicationReportRegistry.MaximumChildObjects)
                throw Bad("Invalid application report source provenance or bounds.");
            if (!source.RegisteredRoute.Contains("{id}", StringComparison.Ordinal) && source.ObjectsAttempted != 0) throw Bad("Root sources cannot claim child-object attempts.");
            if (!Utc(source.StartedAt, out var from) || !Utc(source.EndedAt, out var to) || from < started || to < from || to > ended)
                throw Bad("Invalid source collection interval.");
            Outcome(source.Status, source.Error);
            if (source.Status is ReportReadState.Failed or ReportReadState.NotAttempted && source.Rows != 0)
                throw Bad("Failed/unattempted sources cannot claim collected rows.");
        }
        if (e.Sections is null || e.Sections.Count != definition.Sections.Count || e.Sections.Any(s => s is null)
            || !e.Sections.Select(s => s.Id).SequenceEqual(definition.Sections.Select(s => s.Id), StringComparer.Ordinal))
            throw Bad("Invalid application report sections.");
        foreach (var section in e.Sections)
        {
            Outcome(section.Status, section.Error); Strings(section.Limitations);
            if (section.Rows is null || section.Rows.Count > ApplicationReportRegistry.MaximumRows || section.Rows.Any(r => r is null))
                throw Bad("Excessive application report rows.");
            if (section.Status is ReportReadState.Failed or ReportReadState.NotAttempted && section.Rows.Count != 0)
                throw Bad("Failed/unattempted sections cannot contain rows.");
            var registration = definition.Sections.Single(s => s.Id == section.Id);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in section.Rows)
            {
                GraphReportRow row;
                try { row = (GraphReportRow)(JsonSerializer.Deserialize(node.ToJsonString(), registration.RowType, RegisteredReportJson.Strict) ?? throw Bad("Empty metadata row.")); }
                catch (JsonException) { throw Bad("Invalid registered metadata row fields."); }
                Texts(node);
                Outcome(row.ReadStatus, row.Error);
                if (row.Id is not null && (!ids.Add(row.Id) || !(registration.GuidIdentity ? ReportValues.IsCanonicalGuid(row.Id) : TextIdentity(row.Id))))
                    throw Bad("Invalid or duplicate application report row identity.");
                if (row.ReadStatus == ReportReadState.Collected && row.Id is null) throw Bad("Successful rows require exact identities.");
                if (section.Status == ReportReadState.Collected && row.ReadStatus != ReportReadState.Collected)
                    throw Bad("Partial rows cannot become a successful section.");
                switch (row)
                {
                    case ApplicationExpiryRow expiry: Expiry(expiry, section.Id, ended, ((ApplicationExpiryEvidence)e).Parameters.HorizonDays); break;
                    case ApplicationPrincipalReportRow principal: Principal(principal); break;
                    case DelegatedGrantReportRow grant: Grant(grant); break;
                    case ApplicationRoleAssignmentReportRow assignment: Assignment(assignment); break;
                }
            }
        }
        ValidateSourceAccounting(e, historical);
        if (e is ApplicationConsentEvidence) ValidateResolutions(e);
        State(e.Status);
        if (e.Status != ReportEvidenceSchema.Overall(e.Sections.Select(s => s.Status))) throw Bad("Report state does not match its sections.");
        if (e.Sources.Any(s => s.Status != ReportReadState.Collected) && e.Status == ReportReadState.Collected)
            throw Bad("Incomplete source reads cannot claim a collected report.");
        if (e.IntegrityDigest is not { Length: 64 } || !e.IntegrityDigest.All(Uri.IsHexDigit) || !EvidenceIntegrity.Verify<object>(e, e.IntegrityDigest))
            throw Bad("Application report integrity check failed; unsigned digests do not authenticate source claims.");
        if (Encoding.UTF8.GetByteCount(Serialise(e)) > MaximumBytes) throw Bad("Application evidence exceeds the 32 MiB limit.");
    }
    private static void ValidateSourceAccounting(ApplicationReportEvidence e, bool historical)
    {
        // The validated paired registration fixes the source/section order. Expiry has two extra owner sources.
        for (var i = 0; i < e.Sections.Count; i++)
        {
            var section = e.Sections[i]; var source = e.Sources[i];
            if (source.Rows != section.Rows.Count)
                throw Bad("Retained section rows do not match their registered source count.");
            // A complete source may be refined to Partial by returned metadata or an incomplete parent catalog.
            if (section.Status != source.Status && !(source.Status == ReportReadState.Collected && section.Status == ReportReadState.Partial))
                throw Bad("Section read state contradicts its registered source outcome.");
        }
        if (e.Sources.Where(s => s.RegisteredRoute.Contains("{id}", StringComparison.Ordinal)).Sum(s => s.ObjectsAttempted) > ApplicationReportRegistry.MaximumChildObjects)
            throw Bad("Combined child-object attempts exceed the report fan-out bound.");
        if (e is not ApplicationExpiryEvidence) return;
        for (var i = 0; i < 2; i++)
        {
            var parents = e.Sections[i].Rows.Select(ReadRow<ApplicationExpiryRow>).ToList();
            var eligible = parents.Where(r => r.IdentityStatus == "Exact").ToList(); var source = e.Sources[i + 2];
            if (!historical)
            {
                var counts = source.RetainedOwnerOutcomes!;
                var values = new[] { counts.Collected, counts.Partial, counts.Failed, counts.Cancelled, counts.NotAttempted };
                var states = new[] { ReportReadState.Collected, ReportReadState.Partial, ReportReadState.Failed, ReportReadState.Cancelled, ReportReadState.NotAttempted };
                if (values.Any(v => v < 0 || v > ApplicationReportRegistry.MaximumRows) || values.Sum() != eligible.Count
                    || values.Where((v, index) => v != eligible.Count(r => r.OwnersReadStatus == states[index])).Any())
                    throw Bad("Retained owner outcomes contradict the exact parent rows or their bounds.");
            }
            if (source.Rows != parents.Sum(r => r.Owners.Count))
                throw Bad("Retained owners do not match their registered source count.");
            var requiredAttempts = eligible.Count(r => r.OwnersReadStatus is ReportReadState.Collected or ReportReadState.Partial or ReportReadState.Failed);
            var possibleAttempts = requiredAttempts + eligible.Count(r => r.OwnersReadStatus == ReportReadState.Cancelled);
            // Cancellation can occur before a query or during it; preserve either truthful attempt count.
            if (source.ObjectsAttempted < requiredAttempts || source.ObjectsAttempted > possibleAttempts)
                throw Bad("Owner query attempts do not match the exact parent read states.");
            if (source.Status == ReportReadState.Collected && (eligible.Any(r => r.OwnersReadStatus != ReportReadState.Collected)
                || eligible.Count == 0 && e.Sources[i].Status != ReportReadState.Collected)
                || source.Status == ReportReadState.NotAttempted && eligible.Any(r => r.OwnersReadStatus != ReportReadState.NotAttempted)
                || source.Status == ReportReadState.Failed && eligible.Any(r => r.OwnersReadStatus != ReportReadState.Failed))
                throw Bad("Owner read states contradict their registered source outcome.");
        }
    }
    public static void ValidateParameters(ApplicationExpiryParameters? p)
    {
        if (p is null || p.HorizonDays is < 1 or > 365) throw Bad("Expiry horizonDays must be an integer from 1 to 365.");
    }
    private static void Expiry(ApplicationExpiryRow r, string section, DateTimeOffset ended, int horizon)
    {
        if (r.ObjectKind != (section == "applications" ? "application" : "servicePrincipal")) throw Bad("Directory object kind does not match its section.");
        Identity(r.Id, r.IdentityStatus, r.ReadStatus); Reference(r.ApplicationId);
        Outcome(r.CredentialsReadStatus, r.CredentialReadReason); Outcome(r.OwnersReadStatus, r.OwnerReadReason);
        if (r.Credentials is null || r.Credentials.Count > ApplicationReportRegistry.MaximumCredentials || r.Credentials.Any(c => c is null)
            || r.Owners is null || r.Owners.Count > ApplicationReportRegistry.MaximumOwners || r.Owners.Any(o => o is null)) throw Bad("Invalid nested credential/owner bounds.");
        foreach (var c in r.Credentials)
        {
            if (c.Kind is not ("password" or "key")) throw Bad("Unknown credential metadata kind.");
            Reference(c.KeyId); var expected = ApplicationCredentialClassification.Classify(c, ended, horizon);
            if (c.Classification != expected.State || c.Reason != expected.Reason) throw Bad("Credential classification is not derived from fixed end/horizon metadata.");
            if (c.Classification == "Unknown" && r.CredentialsReadStatus == ReportReadState.Collected) throw Bad("Unknown credentials cannot become a complete credential read.");
        }
        if (r.CredentialsReadStatus == ReportReadState.Collected && r.Credentials.Select(c => c.Kind + ":" + c.KeyId).Distinct(StringComparer.Ordinal).Count() != r.Credentials.Count) throw Bad("Duplicate credentials cannot claim a complete list.");
        if (r.OwnersReadStatus == ReportReadState.Collected && r.Owners.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != r.Owners.Count) throw Bad("Duplicate owners cannot claim a complete list.");
        foreach (var o in r.Owners)
        {
            Reference(o.Id); Outcome(o.ReadStatus, o.Reason);
            if (o.ReadStatus == ReportReadState.Collected && (!ReportValues.IsCanonicalGuid(o.Id) || !ReportValues.HasText(o.Name)))
                throw Bad("Resolved owners require exact identity and reported name.");
            if (o.ReadStatus != ReportReadState.Collected && r.OwnersReadStatus == ReportReadState.Collected) throw Bad("Unresolved owners cannot become a complete owner list.");
        }
        if (r.ReadStatus == ReportReadState.Collected && (r.IdentityStatus != "Exact" || !ReportValues.IsCanonicalGuid(r.ApplicationId)
            || !ReportValues.HasText(r.Name) || r.CredentialsReadStatus != ReportReadState.Collected || r.OwnersReadStatus != ReportReadState.Collected))
            throw Bad("Incomplete identity, credentials or owners cannot become a successful object row.");
        if (r.IdentityStatus != "Exact" && (r.Owners.Count != 0 || r.OwnersReadStatus == ReportReadState.Collected)) throw Bad("Ambiguous/unknown objects cannot drive owner reads.");
    }
    private static void Principal(ApplicationPrincipalReportRow r)
    {
        Identity(r.Id, r.IdentityStatus, r.ReadStatus); Reference(r.ApplicationId); Outcome(r.AppRolesReadStatus, r.AppRolesReadReason);
        if (r.AppRoles is null || r.AppRoles.Count > ApplicationReportRegistry.MaximumAppRoles || r.AppRoles.Any(a => a is null)) throw Bad("Invalid app-role metadata bounds.");
        foreach (var role in r.AppRoles) Reference(role.Id);
        if (r.AppRolesReadStatus == ReportReadState.Collected && (r.AppRoles.Any(a => !ReportValues.IsCanonicalGuid(a.Id) || !ReportValues.HasText(a.DisplayName) || !ReportValues.HasText(a.Value) || a.IsEnabled is null)
            || r.AppRoles.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != r.AppRoles.Count)) throw Bad("Collected role definitions need unique exact IDs and reported metadata.");
        if (r.ReadStatus == ReportReadState.Collected && (r.IdentityStatus != "Exact" || !ReportValues.IsCanonicalGuid(r.ApplicationId)
            || !ReportValues.HasText(r.Name) || r.AppRolesReadStatus != ReportReadState.Collected)) throw Bad("Incomplete principals cannot become collected rows.");
    }
    private static void Grant(DelegatedGrantReportRow r)
    {
        Reference(r.ClientId); Reference(r.ResourceId); Reference(r.PrincipalId); Resolution(r.ResolutionStatus, r.ResolutionReason, r.ReadStatus);
        if (r.ReadStatus == ReportReadState.Collected && (!ReportValues.IsCanonicalGuid(r.ClientId) || !ReportValues.IsCanonicalGuid(r.ResourceId)
            || !ReportValues.HasText(r.Scope) || r.ConsentType is not ("AllPrincipals" or "Principal")
            || r.ConsentType == "Principal" && !ReportValues.IsCanonicalGuid(r.PrincipalId)
            || r.ConsentType == "AllPrincipals" && r.PrincipalId is not null)) throw Bad("Incomplete delegated-grant metadata cannot claim success.");
    }
    private static void Assignment(ApplicationRoleAssignmentReportRow r)
    {
        Reference(r.QueriedPrincipalId); Reference(r.PrincipalId); Reference(r.ResourceId); Reference(r.AppRoleId);
        Resolution(r.ResolutionStatus, r.ResolutionReason, r.ReadStatus);
        if (!ReportValues.IsCanonicalGuid(r.QueriedPrincipalId)) throw Bad("Assignments need the exact principal queried by the registered child route.");
        if (r.ReadStatus == ReportReadState.Collected && (r.PrincipalId != r.QueriedPrincipalId || !ReportValues.IsCanonicalGuid(r.ResourceId)
            || !ReportValues.IsCanonicalGuid(r.AppRoleId))) throw Bad("Mismatched/zero assignment relationships cannot claim success.");
    }
    private static void ValidateResolutions(ApplicationReportEvidence e)
    {
        var principals = e.Sections[0].Rows.Select(ReadRow<ApplicationPrincipalReportRow>).Where(p => p.IdentityStatus == "Exact" && p.Id is not null)
            .ToDictionary(p => p.Id!, StringComparer.Ordinal);
        foreach (var n in e.Sections[1].Rows)
        {
            var r = ReadRow<DelegatedGrantReportRow>(n);
            if (r.ClientName is not null && (!principals.TryGetValue(r.ClientId ?? "", out var client) || r.ClientName != client.Name)
                || r.ResourceName is not null && (!principals.TryGetValue(r.ResourceId ?? "", out var resource) || r.ResourceName != resource.Name))
                throw Bad("Grant names are not joined by exact reported service-principal identity.");
            if (r.ResolutionStatus == "Resolved" && (r.ClientName is null || r.ResourceName is null)) throw Bad("Resolved grants require exact reported principal names.");
        }
        foreach (var n in e.Sections[2].Rows)
        {
            var r = ReadRow<ApplicationRoleAssignmentReportRow>(n);
            if (r.PrincipalName is not null && (!principals.TryGetValue(r.PrincipalId ?? "", out var principal) || r.PrincipalName != principal.Name)
                || r.ResourceName is not null && (!principals.TryGetValue(r.ResourceId ?? "", out var resource) || r.ResourceName != resource.Name)) throw Bad("Assignment names are not exact-identity joins.");
            if (r.RoleName is not null || r.RoleValue is not null || r.ResolutionStatus == "Resolved")
            {
                if (!principals.TryGetValue(r.ResourceId ?? "", out var roleResource) || roleResource.AppRolesReadStatus != ReportReadState.Collected)
                    throw Bad("Role names need complete metadata from the exact resource object.");
                var roles = roleResource.AppRoles.Where(a => a.Id == r.AppRoleId).ToList();
                if (roles.Count != 1 || !ReportValues.IsCanonicalGuid(r.AppRoleId) || r.RoleName != roles[0].DisplayName || r.RoleValue != roles[0].Value)
                    throw Bad("Role name/value is not resolved from the exact unique resource role ID.");
            }
            if (r.ResolutionStatus == "Resolved" && (r.PrincipalId != r.QueriedPrincipalId || r.PrincipalName is null || r.ResourceName is null)) throw Bad("Resolved assignment principal is not exact.");
        }
    }
    private static void Identity(string? id, string state, string read)
    {
        if (state is not ("Exact" or "Unknown" or "Ambiguous") || state == "Exact" && !ReportValues.IsCanonicalGuid(id)
            || state != "Exact" && read == ReportReadState.Collected) throw Bad("Invalid directory-object identity state.");
    }
    private static void Resolution(string status, string? reason, string read)
    {
        if (status is not ("Resolved" or "Unresolved" or "Ambiguous") || status != "Resolved" && string.IsNullOrWhiteSpace(reason)
            || status == "Resolved" && reason is not null || read == ReportReadState.Collected && status != "Resolved") throw Bad("Invalid grant/assignment resolution state.");
        Text(reason);
    }
    private static void Outcome(string state, string? reason)
    {
        State(state); Text(reason);
        if (state == ReportReadState.Collected ? reason is not null : string.IsNullOrWhiteSpace(reason)) throw Bad("Read status requires an explicit truthful reason.");
    }
    private static void State(string value) { if (!ReportReadState.All.Contains(value, StringComparer.Ordinal)) throw Bad("Unknown report read state."); }
    private static void Guid(string? value) { if (!ReportValues.IsCanonicalGuid(value)) throw Bad("Expected canonical nonzero report identity."); }
    internal static bool TextIdentity(string? value) => ReportValues.IsTextIdentity(value) && value!.Length <= 256 && !value.Any(char.IsControl);
    private static void Reference(string? value)
    {
        if (value is not null && (value.Length != 36 || !System.Guid.TryParseExact(value, "D", out var id) || id.ToString("D") != value)) throw Bad("Invalid exact reported relationship/key ID.");
    }
    private static bool Utc(string? value, out DateTimeOffset at) => Timestamps.TryParse(value, out at) && value!.EndsWith('Z');
    private static void Text(string? value, int max = 4096, bool required = false) { if (value is not null && value.Length > max || required && string.IsNullOrWhiteSpace(value)) throw Bad("Missing or excessive metadata text."); }
    private static void Strings(List<string>? values) { if (values is null || values.Count > 64 || values.Any(s => s is null || s.Length > 4096)) throw Bad("Invalid report limitations."); }
    private static void Texts(JsonNode node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var text)) Text(text);
        else if (node is JsonObject o) foreach (var child in o.Select(p => p.Value).Where(p => p is not null)) Texts(child!);
        else if (node is JsonArray a) foreach (var child in a.Where(p => p is not null)) Texts(child!);
    }
    private static ConfigurationException Bad(string message) => new(message);
}

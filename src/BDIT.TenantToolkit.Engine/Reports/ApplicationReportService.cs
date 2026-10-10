using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Bounded metadata-only collection over the existing verified account. No authentication or consent.</summary>
public sealed class ApplicationReportService(IClock clock, TimeSpan? readBudget = null)
{
    public Task<ApplicationReportEvidence> CaptureExpiryAsync(IGraphClient graph, TenantSession session,
        ApplicationExpiryParameters parameters, CancellationToken ct)
    {
        ApplicationReportEvidenceSchema.ValidateParameters(parameters);
        return Capture(graph, session, new ApplicationExpiryEvidence { Parameters = new() { HorizonDays = parameters.HorizonDays } }, ct);
    }
    public Task<ApplicationReportEvidence> CaptureConsentAsync(IGraphClient graph, TenantSession session,
        ApplicationConsentParameters parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return Capture(graph, session, new ApplicationConsentEvidence(), ct);
    }
    private async Task<ApplicationReportEvidence> Capture(IGraphClient graph, TenantSession session, ApplicationReportEvidence report, CancellationToken ct)
    {
        var budget = readBudget ?? TimeSpan.FromMinutes(5);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromMinutes(5)) throw new ConfigurationException("Report read budget must be positive and no more than five minutes.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(budget);
        var context = Context.Pin(graph, session);
        var definition = ApplicationReportRegistry.Find(2, report.ReportId);
        report.Id = Guid.NewGuid().ToString("D"); report.TenantId = Canonical(session.TenantId)!;
        report.AccountObjectId = Canonical(session.AccountObjectId); report.StartedAt = Timestamps.Format(clock.UtcNow);
        report.ToolkitVersion = ToolkitVersion.Current; report.ModuleVersion = ApplicationReportRegistry.AdapterVersion;
        report.Sources = definition.Routes.Select(r => new ApplicationReportSource
        { RegisteredRoute = r.Path, Reference = definition.Reference, StartedAt = report.StartedAt, EndedAt = report.StartedAt, Error = "No read attempted." }).ToList();
        report.Sections = definition.Sections.Select(s => new ReportSection { Id = s.Id, Error = "No read attempted." }).ToList();
        report.Limitations = [definition.Limitations, "Unsigned report source claims and digests are not authentication or complete deployment before-evidence.",
            $"Reads have a five-minute maximum budget, {ApplicationReportRegistry.MaximumRows} rows per section, {ApplicationReportRegistry.MaximumCredentials} credentials per object, {ApplicationReportRegistry.MaximumOwners} owners per object, {ApplicationReportRegistry.MaximumAppRoles} role definitions per principal and {ApplicationReportRegistry.MaximumAssignmentsPerPrincipal} assignments per principal.",
            $"At most {ApplicationReportRegistry.MaximumChildObjects} exact objects receive child reads per report. Metadata admission reserves envelope space within the existing 32 MiB limit. Omitted data remains partial/unknown, never absent."];
        var collection = new Collection(graph, session, clock, context, report, bounded.Token);
        if (report is ApplicationExpiryEvidence) await collection.Expiry(); else await collection.Consent();
        context.Assert(graph, session);
        var ended = clock.UtcNow; report.EndedAt = Timestamps.Format(ended);
        collection.Finish(ended);
        report.Status = ReportEvidenceSchema.Overall(report.Sections.Select(s => s.Status));
        ApplicationReportEvidenceSchema.Seal(report); return report;
    }
    private sealed record Context(string Tenant, string Account, string Operator, string Client, string AccountName,
        SessionMode Mode, string Expires, string Scopes)
    {
        internal static Context Pin(IGraphClient graph, TenantSession session)
        {
            if (!session.TenantVerified || !session.OperatorVerified || Canonical(session.TenantId) is null || Canonical(session.AccountObjectId) is null
                || Canonical(session.OperatorObjectId) != Canonical(session.AccountObjectId) || Canonical(session.ClientId) is null
                || graph.Mode != session.Mode || session.Scopes is null || session.Mode is not (SessionMode.Assessment or SessionMode.Deployment)
                || !string.Equals(graph.TenantId, session.TenantId, StringComparison.OrdinalIgnoreCase)
                || !Timestamps.TryParse(session.TokenExpiresAt, out _))
                throw new TenantMismatchException("Application reports require the current verified Graph tenant/account context.");
            return new(session.TenantId, session.AccountObjectId, session.OperatorObjectId!, session.ClientId, session.Account,
                session.Mode, session.TokenExpiresAt, string.Join("\n", session.Scopes.OrderBy(s => s, StringComparer.Ordinal)));
        }
        internal void Assert(IGraphClient graph, TenantSession session)
        {
            if (Pin(graph, session) != this) throw new TenantMismatchException("The verified application report context changed; no result was accepted.");
        }
    }
    private sealed class Collection(IGraphClient graph, TenantSession session, IClock clock, Context context, ApplicationReportEvidence report, CancellationToken ct)
    {
        private const int MetadataBudget = ApplicationReportEvidenceSchema.MaximumBytes - 1024 * 1024;
        private readonly List<GraphReportRow>[] _rows = report.Sections.Select(_ => new List<GraphReportRow>()).ToArray();
        private int _bytes, _children;
        private Dictionary<string, ApplicationPrincipalReportRow> _principals = new(StringComparer.Ordinal);
        private readonly Dictionary<int, List<string>> _childStates = new();
        internal async Task Expiry()
        {
            await Root(0, 0, "/applications?$select=id,displayName,appId,passwordCredentials,keyCredentials", n => ExpiryRow(n, "application"));
            await Root(1, 1, "/servicePrincipals?$select=id,displayName,appId,passwordCredentials,keyCredentials", n => ExpiryRow(n, "servicePrincipal"));
            for (var section = 0; section < 2; section++)
            {
                var source = section + 2;
                var parents = _rows[section].Cast<ApplicationExpiryRow>().ToList();
                foreach (var row in parents)
                {
                    if (row.IdentityStatus != "Exact") { row.OwnerReadReason = "No owner query for missing or ambiguous object identity."; Partial(row, row.OwnerReadReason); continue; }
                    if (!ChildAvailable(source))
                    { row.OwnerReadReason = report.Sources[source].Error; row.OwnersReadStatus = report.Sources[source].Status == ReportReadState.Cancelled ? ReportReadState.Cancelled : ReportReadState.NotAttempted; Partial(row, row.OwnerReadReason!); continue; }
                    var path = (section == 0 ? "/applications/" : "/servicePrincipals/") + row.Id + "/owners?$select=id,displayName";
                    var seen = new Dictionary<string, ApplicationOwnerMetadata>(StringComparer.Ordinal);
                    var result = await Child(source, path, ApplicationReportRegistry.MaximumOwners, n => Owner(n), owner =>
                    {
                        if (owner.Id is { } id && seen.TryGetValue(id, out var existing))
                        { existing.ReadStatus = ReportReadState.Partial; existing.Reason = "Duplicate owner identity; no unique owner is inferred."; return false; }
                        var before = Footprint(row); row.Owners.Add(owner);
                        if (!Update(row, before)) { row.Owners.RemoveAt(row.Owners.Count - 1); return false; }
                        if (owner.Id is not null) seen.Add(owner.Id, owner); return true;
                    });
                    row.OwnersReadStatus = result.Status; row.OwnerReadReason = result.Error;
                    if (row.Owners.Any(o => o.ReadStatus != ReportReadState.Collected) && row.OwnersReadStatus == ReportReadState.Collected)
                    { row.OwnersReadStatus = ReportReadState.Partial; row.OwnerReadReason = "Some owners returned limited, malformed or duplicate identity/name metadata."; AddChildState(source, ReportReadState.Partial, row.OwnerReadReason); }
                    if (row.OwnersReadStatus != ReportReadState.Collected) Partial(row, row.OwnerReadReason!);
                }
                CompleteChildSource(source, report.Sources[section].Status, parents.Count);
            }
        }
        internal async Task Consent()
        {
            await Root(0, 0, "/servicePrincipals?$select=id,displayName,appId,appRoles", Principal);
            _principals = _rows[0].Cast<ApplicationPrincipalReportRow>().Where(p => p.Id is not null && p.IdentityStatus == "Exact").ToDictionary(p => p.Id!, StringComparer.Ordinal);
            await Root(1, 1, "/oauth2PermissionGrants?$select=id,clientId,resourceId,consentType,principalId,scope", Grant);
            var parents = _rows[0].Cast<ApplicationPrincipalReportRow>().ToList();
            foreach (var principal in parents.Where(p => p.IdentityStatus == "Exact"))
            {
                if (_rows[2].Count == ApplicationReportRegistry.MaximumRows)
                { AddChildState(2, ReportReadState.Partial, "Assignment section row cap reached; remaining principals were not queried."); break; }
                if (!ChildAvailable(2)) break;
                await Child(2, "/servicePrincipals/" + principal.Id + "/appRoleAssignments?$select=id,principalId,resourceId,appRoleId",
                    ApplicationReportRegistry.MaximumAssignmentsPerPrincipal, n => Assignment(n, principal.Id!), row => Add(2, row));
            }
            CompleteChildSource(2, report.Sources[0].Status, parents.Count);
            report.Sections[2].Status = report.Sources[2].Status; report.Sections[2].Error = report.Sources[2].Error;
        }
        private async Task Root<T>(int section, int sourceIndex, string path, Func<JsonObject, T> project) where T : GraphReportRow
        {
            var source = report.Sources[sourceIndex]; var registration = ApplicationReportRegistry.Find(2, report.ReportId).Routes[sourceIndex];
            if (!ApplicationReportRegistry.HasAccess(session, registration))
            {
                source.Error = "The verified context lacks " + registration.Scope + ". No authentication or consent is requested; this source is NotAttempted.";
                report.Sections[section].Error = source.Error; return;
            }
            context.Assert(graph, session); source.StartedAt = Timestamps.Format(clock.UtcNow);
            source.Status = ReportReadState.Collected; source.Error = null;
            try
            {
                await foreach (var item in graph.GetBoundedAsync(GraphApi.V1, path, ApplicationReportRegistry.MaximumRows + 1, ct))
                {
                    ct.ThrowIfCancellationRequested(); context.Assert(graph, session);
                    if (_rows[section].Count == ApplicationReportRegistry.MaximumRows)
                    { source.Status = ReportReadState.Partial; source.Error = "Section row cap reached; omitted objects were not read or checked."; break; }
                    var row = project(item);
                    if (!Add(section, row)) { source.Status = ReportReadState.Partial; source.Error = "Duplicate identity or metadata byte limit encountered; omitted data remains unknown."; }
                    if (_bytes >= MetadataBudget) break;
                }
            }
            catch (OperationCanceledException) { source.Status = ReportReadState.Cancelled; source.Error = "Read cancelled; retained rows are partial and remaining objects/pages were not checked."; }
            catch (TenantMismatchException) { throw; }
            catch (Exception) { source.Status = _rows[section].Count > 0 ? ReportReadState.Partial : ReportReadState.Failed; source.Error = "Read failed; endpoint/exception text is excluded because it may contain sensitive data. Retained rows and remaining pages are incomplete."; }
            source.Rows = _rows[section].Count; source.EndedAt = Timestamps.Format(clock.UtcNow);
            report.Sections[section].Status = source.Status; report.Sections[section].Error = source.Error;
        }
        private bool Add(int section, GraphReportRow row)
        {
            if (_rows[section].Count >= ApplicationReportRegistry.MaximumRows) return false;
            if (row.Id is not null && _rows[section].FirstOrDefault(r => r.Id == row.Id) is { } existing)
            {
                if (existing is ApplicationExpiryRow expiry) expiry.IdentityStatus = "Ambiguous";
                if (existing is ApplicationPrincipalReportRow principal) principal.IdentityStatus = "Ambiguous";
                if (existing is DelegatedGrantReportRow grant) { grant.ResolutionStatus = "Ambiguous"; grant.ResolutionReason = "Duplicate grant identity; no unique observed grant is inferred."; }
                if (existing is ApplicationRoleAssignmentReportRow assignment) { assignment.ResolutionStatus = "Ambiguous"; assignment.ResolutionReason = "Duplicate assignment identity; no unique observed assignment is inferred."; }
                Partial(existing, "Duplicate returned identity; no child query or name-based join is made."); return false;
            }
            var size = Footprint(row); if (size > MetadataBudget - _bytes) { _bytes = MetadataBudget; return false; }
            _rows[section].Add(row); _bytes += size; return true;
        }
        private bool Update(GraphReportRow row, int before)
        {
            var delta = Footprint(row) - before;
            if (delta > MetadataBudget - _bytes) return false;
            _bytes += delta; return true;
        }
        private bool ChildAvailable(int sourceIndex)
        {
            var source = report.Sources[sourceIndex];
            var route = ApplicationReportRegistry.Find(2, report.ReportId).Routes[sourceIndex];
            if (!ApplicationReportRegistry.HasAccess(session, route)) { source.Error = "Current access lacks " + route.Scope + "; no new consent is requested."; return false; }
            if (ct.IsCancellationRequested) { AddChildState(sourceIndex, ReportReadState.Cancelled, "Cancelled before remaining child reads; omitted data is unknown."); return false; }
            if (_children >= ApplicationReportRegistry.MaximumChildObjects)
            { AddChildState(sourceIndex, ReportReadState.Partial, "Child read fan-out cap reached; remaining objects were not queried."); return false; }
            if (_bytes >= MetadataBudget) { AddChildState(sourceIndex, ReportReadState.Partial, "Metadata byte cap reached; remaining child reads were not attempted."); return false; }
            return true;
        }
        private async Task<(string Status, string? Error)> Child<T>(int sourceIndex, string path, int maximum, Func<JsonObject, T> project, Func<T, bool> admit)
        {
            context.Assert(graph, session); var source = report.Sources[sourceIndex];
            if (source.ObjectsAttempted == 0) source.StartedAt = Timestamps.Format(clock.UtcNow);
            source.ObjectsAttempted++; _children++; var count = 0; var state = ReportReadState.Collected; string? error = null;
            try
            {
                await foreach (var item in graph.GetBoundedAsync(GraphApi.V1, path, maximum + 1, ct))
                {
                    ct.ThrowIfCancellationRequested(); context.Assert(graph, session);
                    if (count == maximum) { state = ReportReadState.Partial; error = "Per-object child-list cap reached; omitted data is unknown."; break; }
                    if (!admit(project(item))) { state = ReportReadState.Partial; error = "Duplicate identity, section cap or metadata byte cap; omitted child metadata remains unknown."; break; }
                    count++; source.Rows++;
                }
            }
            catch (OperationCanceledException) { state = ReportReadState.Cancelled; error = "Child pagination cancelled; retained entries and remaining objects are incomplete."; }
            catch (TenantMismatchException) { throw; }
            catch (Exception) { state = count > 0 ? ReportReadState.Partial : ReportReadState.Failed; error = "Child metadata read failed; sensitive endpoint/exception text is excluded."; }
            source.EndedAt = Timestamps.Format(clock.UtcNow); AddChildState(sourceIndex, state, error); return (state, error);
        }
        private void AddChildState(int index, string state, string? error)
        {
            if (!_childStates.TryGetValue(index, out var states)) { states = []; _childStates.Add(index, states); }
            // The aggregate records outcomes, not an unbounded per-object error log.
            if (states.Count < ApplicationReportRegistry.MaximumChildObjects + 1) states.Add(state);
            else if (!states.Contains(state, StringComparer.Ordinal)) states[0] = state;
            var source = report.Sources[index]; source.Status = ReportEvidenceSchema.Overall(states);
            source.Error = source.Status == ReportReadState.Collected ? null : error ?? source.Error ?? "Some child reads were incomplete; omitted data remains unknown.";
            source.EndedAt = Timestamps.Format(clock.UtcNow);
        }
        private void CompleteChildSource(int index, string parentState, int parents)
        {
            var source = report.Sources[index];
            if (!_childStates.ContainsKey(index))
            {
                if (parents == 0 && parentState == ReportReadState.Collected) { source.Status = ReportReadState.Collected; source.Error = null; }
                else source.Error = "No eligible exact objects were queried; unavailable/ambiguous parent inventory does not prove an empty child list.";
            }
            else if (parentState != ReportReadState.Collected) AddChildState(index, parentState == ReportReadState.Cancelled ? ReportReadState.Cancelled : ReportReadState.Partial,
                (source.Error is null ? "" : source.Error + " ") + "Parent inventory is incomplete; unreturned objects received no child read.");
            source.EndedAt = Timestamps.Format(clock.UtcNow);
        }
        private ApplicationExpiryRow ExpiryRow(JsonObject n, string kind)
        {
            var row = new ApplicationExpiryRow { Id = Canonical(Text(n, "id", 256)), Name = Text(n, "displayName"), ObjectKind = kind,
                ApplicationId = Reference(n, "appId"), ReadStatus = ReportReadState.Collected, OwnersReadStatus = ReportReadState.NotAttempted,
                OwnerReadReason = "No owner read attempted.", CredentialsReadStatus = ReportReadState.Collected };
            row.IdentityStatus = row.Id is null ? "Unknown" : "Exact";
            if (row.Id is null || !ReportValues.IsCanonicalGuid(row.ApplicationId) || !ReportValues.HasText(row.Name)) Partial(row, "Object identity/name or application ID is missing, zero or malformed.");
            var remaining = ApplicationReportRegistry.MaximumCredentials;
            foreach (var (key, credentialKind) in new[] { ("passwordCredentials", "password"), ("keyCredentials", "key") })
            {
                if (n[key] is not JsonArray list) { row.CredentialsReadStatus = ReportReadState.Partial; row.CredentialReadReason = "A credential list is missing or unreadable; it is not an empty reported list."; continue; }
                foreach (var item in list.Take(remaining))
                {
                    var value = item as JsonObject;
                    row.Credentials.Add(new() { Kind = credentialKind, KeyId = Reference(value, "keyId"), DisplayName = Text(value, "displayName"),
                        Type = credentialKind == "key" ? Text(value, "type", 256) : null, Usage = credentialKind == "key" ? Text(value, "usage", 256) : null,
                        Start = Text(value, "startDateTime", 128), End = Text(value, "endDateTime", 128) });
                }
                if (list.Count > remaining) { row.CredentialsReadStatus = ReportReadState.Partial; row.CredentialReadReason = "Credential metadata cap reached; omitted credentials are unknown."; }
                remaining -= Math.Min(list.Count, remaining);
            }
            return row;
        }
        private static ApplicationOwnerMetadata Owner(JsonObject n)
        {
            var id = Canonical(Text(n, "id", 256)); var name = Text(n, "displayName"); var complete = id is not null && ReportValues.HasText(name);
            return new() { Id = id, Name = name, ReadStatus = complete ? ReportReadState.Collected : ReportReadState.Partial,
                Reason = complete ? null : "Owner identity/name is missing or limited information; ownership remains unresolved." };
        }
        private static ApplicationPrincipalReportRow Principal(JsonObject n)
        {
            var row = new ApplicationPrincipalReportRow { Id = Canonical(Text(n, "id", 256)), Name = Text(n, "displayName"),
                ApplicationId = Reference(n, "appId"), ReadStatus = ReportReadState.Collected, AppRolesReadStatus = ReportReadState.Collected };
            row.IdentityStatus = row.Id is null ? "Unknown" : "Exact";
            if (row.Id is null || !ReportValues.IsCanonicalGuid(row.ApplicationId) || !ReportValues.HasText(row.Name)) Partial(row, "Principal object/application identity or name is missing, zero or malformed.");
            if (n["appRoles"] is not JsonArray roles) { row.AppRolesReadStatus = ReportReadState.Partial; row.AppRolesReadReason = "App-role definitions are missing/unreadable, not an empty returned list."; }
            else
            {
                row.AppRoles = roles.Take(ApplicationReportRegistry.MaximumAppRoles).Select(n => n as JsonObject).Select(n => new ApplicationRoleMetadata
                { Id = Reference(n, "id"), DisplayName = Text(n, "displayName"), Value = Text(n, "value"), IsEnabled = Boolean(n, "isEnabled") }).ToList();
                if (roles.Count > ApplicationReportRegistry.MaximumAppRoles || row.AppRoles.Any(r => !ReportValues.IsCanonicalGuid(r.Id) || !ReportValues.HasText(r.DisplayName) || !ReportValues.HasText(r.Value) || r.IsEnabled is null)
                    || row.AppRoles.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != row.AppRoles.Count)
                { row.AppRolesReadStatus = ReportReadState.Partial; row.AppRolesReadReason = "Role metadata is capped, missing or ambiguous; no role-name guess is made."; }
            }
            if (row.AppRolesReadStatus != ReportReadState.Collected) Partial(row, row.AppRolesReadReason!); return row;
        }
        private DelegatedGrantReportRow Grant(JsonObject n)
        {
            var row = new DelegatedGrantReportRow { Id = ExactTextId(n), ClientId = Reference(n, "clientId"), ResourceId = Reference(n, "resourceId"),
                ConsentType = Text(n, "consentType", 128), PrincipalId = Reference(n, "principalId"), Scope = Text(n, "scope"), ReadStatus = ReportReadState.Collected };
            row.ClientName = Name(row.ClientId); row.ResourceName = Name(row.ResourceId);
            var resolved = row.Id is not null && row.ClientName is not null && row.ResourceName is not null && ReportValues.HasText(row.Scope)
                && (row.ConsentType == "AllPrincipals" && row.PrincipalId is null || row.ConsentType == "Principal" && ReportValues.IsCanonicalGuid(row.PrincipalId));
            row.ResolutionStatus = resolved ? "Resolved" : "Unresolved";
            row.ResolutionReason = resolved ? null : "Grant identity, consent/subject/scope or exact client/resource resolution is missing/ambiguous; observed text does not prove token contents or use.";
            if (!resolved) Partial(row, row.ResolutionReason!); row.Name = resolved ? row.ClientName + " → " + row.ResourceName : null; return row;
        }
        private ApplicationRoleAssignmentReportRow Assignment(JsonObject n, string queried)
        {
            var row = new ApplicationRoleAssignmentReportRow { Id = ExactTextId(n), QueriedPrincipalId = queried, PrincipalId = Reference(n, "principalId"),
                ResourceId = Reference(n, "resourceId"), AppRoleId = Reference(n, "appRoleId"), ReadStatus = ReportReadState.Collected };
            row.PrincipalName = Name(row.PrincipalId); row.ResourceName = Name(row.ResourceId);
            if (row.ResourceId is not null && _principals.TryGetValue(row.ResourceId, out var resource) && resource.AppRolesReadStatus == ReportReadState.Collected
                && ReportValues.IsCanonicalGuid(row.AppRoleId))
            {
                var role = resource.AppRoles.SingleOrDefault(r => r.Id == row.AppRoleId);
                if (role is not null) { row.RoleName = role.DisplayName; row.RoleValue = role.Value; }
            }
            var resolved = row.Id is not null && row.PrincipalId == queried && row.PrincipalName is not null && row.ResourceName is not null
                && row.RoleName is not null && row.RoleValue is not null;
            row.ResolutionStatus = resolved ? "Resolved" : "Unresolved";
            row.ResolutionReason = resolved ? null : "Assignment identity/relationships or exact unique resource-role metadata is unresolved; zero IDs and names never establish a join.";
            if (!resolved) Partial(row, row.ResolutionReason!); row.Name = row.PrincipalName; return row;
        }
        private string? Name(string? id) => id is not null && _principals.TryGetValue(id, out var p) && ReportValues.HasText(p.Name) ? p.Name : null;
        internal void Finish(DateTimeOffset ended)
        {
            for (var i = 0; i < _rows.Length; i++)
            {
                foreach (var row in _rows[i])
                {
                    if (row is ApplicationExpiryRow expiry)
                    {
                        foreach (var credential in expiry.Credentials)
                        { var result = ApplicationCredentialClassification.Classify(credential, ended, ((ApplicationExpiryEvidence)report).Parameters.HorizonDays); credential.Classification = result.State; credential.Reason = result.Reason; }
                        if (expiry.Credentials.Any(c => c.Classification == "Unknown") || expiry.Credentials.Where(c => c.KeyId is not null).Select(c => c.Kind + ":" + c.KeyId).Distinct(StringComparer.Ordinal).Count() != expiry.Credentials.Count(c => c.KeyId is not null))
                        { expiry.CredentialsReadStatus = ReportReadState.Partial; expiry.CredentialReadReason ??= "Credential identities/dates are unknown or duplicated; no absence/expiry claim is inferred."; }
                        if (expiry.CredentialsReadStatus != ReportReadState.Collected) Partial(expiry, expiry.CredentialReadReason!);
                        if (expiry.OwnersReadStatus != ReportReadState.Collected) Partial(expiry, expiry.OwnerReadReason!);
                    }
                    report.Sections[i].Rows.Add(ApplicationReportEvidenceSchema.Row(row));
                }
                var section = report.Sections[i];
                if (section.Status == ReportReadState.Collected && _rows[i].Any(r => r.ReadStatus != ReportReadState.Collected))
                { section.Status = ReportReadState.Partial; section.Error = "Some returned metadata/child reads are incomplete; inspect their explicit states and reasons."; }
                if (i < 2 && report.Sources[i].Status == ReportReadState.Collected && section.Status != ReportReadState.Collected)
                { report.Sources[i].Status = section.Status; report.Sources[i].Error = section.Error; }
                if (section.Status == ReportReadState.Collected && report is ApplicationConsentEvidence && i == 2 && report.Sections[0].Status != ReportReadState.Collected)
                { section.Status = ReportReadState.Partial; section.Error = "Principal inventory is incomplete; omitted principals received no assignment read."; }
            }
        }
        private static int Footprint(GraphReportRow row)
        {
            var node = ApplicationReportEvidenceSchema.Row(row);
            static int Nodes(JsonNode? n) => n is JsonObject o ? 1 + o.Sum(p => Nodes(p.Value)) : n is JsonArray a ? 1 + a.Sum(Nodes) : 1;
            return Encoding.UTF8.GetByteCount(node.ToJsonString()) + Nodes(node) * 24 + (row is ApplicationExpiryRow e ? e.Credentials.Count * 128 : 0) + 1024;
        }
    }
    private static void Partial(GraphReportRow row, string reason) { row.ReadStatus = ReportReadState.Partial; row.Error ??= reason; }
    private static string? Text(JsonObject? node, string key, int maximum = 4096) => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length <= maximum ? text : null;
    private static bool? Boolean(JsonObject? node, string key) => node?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    private static string? Canonical(string? value) => ReportValues.TryGuid(value, out var id) ? id : null;
    private static string? Reference(JsonObject? node, string key)
    {
        var value = Text(node, key, 128);
        return value is { Length: 36 } && Guid.TryParseExact(value, "D", out var id) ? id.ToString("D") : null;
    }
    private static string? ExactTextId(JsonObject node) { var text = Text(node, "id", 256); return ApplicationReportEvidenceSchema.TextIdentity(text) ? text : null; }
}

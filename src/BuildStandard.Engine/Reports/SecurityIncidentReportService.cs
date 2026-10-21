using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

/// <summary>Bounded incident metadata; never response authority or evidence of tenant safety.</summary>
public sealed class SecurityIncidentReportService(IClock clock, string toolkitVersion)
{
    private const string DuplicateReason = "Duplicate incident identity; original observations remain partial and were not deduplicated.";
    public async Task<OperationalContextReport> CaptureAsync(ISecurityIncidentReportSource source, TenantSession session,
        Action requireCurrent, CancellationToken cancellation)
    {
        var tenant = session.TenantId; var account = session.AccountObjectId; var client = session.ClientId;
        var mode = session.Mode; var scopes = ScopeKey(session.Scopes); var window = source.IncidentSelection;
        void AssertCurrent()
        {
            requireCurrent();
            if (!session.TenantVerified || !session.OperatorVerified || !ReportValues.IsCanonicalGuid(tenant)
                || !ReportValues.IsCanonicalGuid(account) || !ReportValues.IsCanonicalGuid(client)
                || session.TenantId != tenant || source.TenantId != tenant || session.AccountObjectId != account
                || session.OperatorObjectId != account || session.ClientId != client || session.Mode != mode
                || ScopeKey(session.Scopes) != scopes || source.IncidentSelection != window)
                throw new TenantMismatchException("The verified incident tenant, account, registration, access or interval changed. No result was admitted for the current client.");
        }
        AssertCurrent();
        if (!session.Scopes.Any(scope => scope is "SecurityIncident.Read.All" or "https://graph.microsoft.com/SecurityIncident.Read.All"))
            throw new ConfigurationException("Choose incident-context access deliberately and reconnect with its returned read permission before checking this report.");
        if (string.IsNullOrWhiteSpace(toolkitVersion) || toolkitVersion.Length > 128 || toolkitVersion.Any(char.IsControl))
            throw new ConfigurationException("Incident capture requires bounded toolkit-version provenance.");
        var started = clock.UtcNow;
        _ = new SecurityIncidentReadSelection(window.StartUtc, window.EndUtc, started);
        cancellation.ThrowIfCancellationRequested();
        var stamp = Timestamps.Format(started);
        var section = new ReportSection { Id = "security-incidents", Status = ReportReadState.Collected, Error = null,
            Limitations = [OperationalContextSchema.IncidentNotice] };
        var report = new OperationalContextReport
        {
            ReportId = "security-incidents", AccessIntent = "security-incidents", TenantId = tenant, AccountObjectId = account,
            ClientId = client, PermissionMode = mode.ToString(), SourceMode = "live", AsOfUtc = stamp,
            StartedAt = stamp, EndedAt = stamp, ToolkitVersion = toolkitVersion,
            IncidentSelection = new() { StartUtc = Timestamps.Format(window.StartUtc), EndUtc = Timestamps.Format(window.EndUtc) },
            Sources = OperationalContextSchema.SourceDeclarations("security-incidents").ToList(), Sections = [section]
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var calls = 0; var rowBytes = 0; var more = true;
        try
        {
            while (more)
            {
                AssertCurrent(); deadline.Token.ThrowIfCancellationRequested();
                if (section.Rows.Count >= OperationalReportRegistry.MaximumRows)
                { Incomplete(section, "The 5,000-row budget ended; later pages were not requested."); break; }
                if (calls >= OperationalReportRegistry.MaximumCalls)
                { Incomplete(section, "The 100-request budget ended; later pages were not read."); break; }
                calls++;
                var page = await source.ReadIncidentsNextAsync(deadline.Token).ConfigureAwait(false);
                AssertCurrent(); deadline.Token.ThrowIfCancellationRequested();
                if (page?.Items is null || page.Items.Count > 100) throw new ConfigurationException("Invalid or oversized incident page.");
                more = page.HasMore;
                await Task.Run(() =>
                {
                    foreach (var raw in page.Items)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        if (raw is null) throw new ConfigurationException("Invalid incident metadata object.");
                        if (section.Rows.Count >= OperationalReportRegistry.MaximumRows)
                        { Incomplete(section, "The 5,000-row budget ended; later observations were not retained."); more = false; break; }
                        var row = Project(raw, window, clock.UtcNow);
                        var encoded = JsonSerializer.SerializeToNode(row, RegisteredReportJson.Strict)!.AsObject();
                        var diagnostic = (JsonObject)encoded.DeepClone(); diagnostic["readStatus"] = ReportReadState.Partial; diagnostic["error"] = DuplicateReason;
                        var bytes = Math.Max(JsonSerializer.SerializeToUtf8Bytes(encoded, OperationalContextSchema.CompactOptions).Length,
                            JsonSerializer.SerializeToUtf8Bytes(diagnostic, OperationalContextSchema.CompactOptions).Length);
                        if (rowBytes + bytes > OperationalReportRegistry.MaximumBytes - 128 * 1024)
                        { Incomplete(section, "The 16 MiB evidence budget ended; earlier observations remain without truncation."); more = false; break; }
                        rowBytes += bytes; section.Rows.Add(encoded);
                        if (row.ReadStatus == ReportReadState.Partial) Incomplete(section, "Some incident metadata is missing, malformed or unclassified; unknown is not a safe state.");
                    }
                }, deadline.Token).ConfigureAwait(false);
                AssertCurrent(); deadline.Token.ThrowIfCancellationRequested();
            }
        }
        catch (TenantMismatchException) { throw; }
        catch (OperationCanceledException)
        {
            AssertCurrent(); section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : cancellation.IsCancellationRequested ? ReportReadState.Cancelled : ReportReadState.Failed;
            section.Error = cancellation.IsCancellationRequested ? "The engineer stopped the incident read; earlier observations remain."
                : "The five-minute incident read budget ended; earlier observations remain.";
        }
        catch (ToolkitException)
        { AssertCurrent(); section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : ReportReadState.Failed; section.Error = "The registered incident source could not be read completely. No empty-success result or interactive retry was substituted."; }
        foreach (var group in section.Rows.Where(row => row["id"] is not null).GroupBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            foreach (var row in group) { row["readStatus"] = ReportReadState.Partial; row["error"] = DuplicateReason; }
            Incomplete(section, DuplicateReason);
        }
        AssertCurrent(); report.EndedAt = Timestamps.Format(clock.UtcNow);
        report.Status = ReportEvidenceSchema.Overall(report.Sections.Select(value => value.Status));
        await Task.Run(() => OperationalContextSchema.Seal(report), CancellationToken.None).ConfigureAwait(false);
        AssertCurrent(); return report;
    }
    private static string ScopeKey(IEnumerable<string> scopes) => string.Join('\n', scopes.OrderBy(value => value, StringComparer.Ordinal));
    private static void Incomplete(ReportSection section, string reason) { section.Status = ReportReadState.Partial; section.Error = reason; }
    private static SecurityIncidentRow Project(JsonObject raw, SecurityIncidentReadSelection window, DateTimeOffset observedAt)
    {
        var partial = false;
        string? Text(string key, bool required = false, bool identity = false)
        {
            if (raw[key] is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || text.Length > (identity ? 512 : 4096)
                || identity && string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsControl(c) && (identity || c is not ('\r' or '\n' or '\t'))))
            { if (required || raw[key] is not null) partial = true; return null; }
            if (required && string.IsNullOrWhiteSpace(text)) { partial = true; return null; }
            return text;
        }
        DateTimeOffset? Time(string key)
        {
            var text = Text(key, true);
            if (text is not null && DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)) return value;
            partial = true; return null;
        }
        var created = Time("createdDateTime"); var updated = Time("lastUpdateDateTime");
        int? priority = null;
        if (raw["priorityScore"] is not null)
        {
            if (raw["priorityScore"] is JsonValue score && score.TryGetValue<int>(out var value) && value is >= 0 and <= 100) priority = value;
            else partial = true;
        }
        var row = new SecurityIncidentRow
        {
            Id = Text("id", true, true), Name = Text("displayName", true),
            CreatedAt = created is { } c ? Timestamps.Format(c) : null, ModifiedAt = updated is { } u ? Timestamps.Format(u) : null,
            Severity = Text("severity", true), Status = Text("status", true), Classification = Text("classification", true),
            Determination = Text("determination", true), PriorityScore = priority,
            RedirectIncidentId = Text("redirectIncidentId", identity: true), IncidentWebUrl = Text("incidentWebUrl")
        };
        partial |= created is null || updated is null || created < window.StartUtc || created >= window.EndUtc || updated < created || updated > observedAt
            || !OperationalContextSchema.KnownIncidentValue("severity", row.Severity) || !OperationalContextSchema.KnownIncidentValue("status", row.Status)
            || !OperationalContextSchema.KnownIncidentValue("classification", row.Classification) || !OperationalContextSchema.KnownIncidentValue("determination", row.Determination)
            || row.Status == "redirected" && row.RedirectIncidentId is null;
        row.ReadStatus = partial ? ReportReadState.Partial : ReportReadState.Collected;
        row.Error = partial ? "Missing, malformed or unclassified incident metadata; no safe-state inference was made." : null;
        return row;
    }
}

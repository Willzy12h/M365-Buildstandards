using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

/// <summary>Tenant-scoped service observations, never a tenant-availability or root-cause assurance.</summary>
public sealed class ServiceHealthReportService(IClock clock, string toolkitVersion)
{
    public async Task<OperationalContextReport> CaptureAsync(IOperationalReportSource source, TenantSession session,
        Action requireCurrent, CancellationToken cancellation)
    {
        var tenant = session.TenantId;
        var account = session.AccountObjectId;
        var client = session.ClientId;
        var mode = session.Mode;
        var scopes = ScopeKey(session.Scopes);
        void AssertCurrent()
        {
            requireCurrent();
            if (!session.TenantVerified || !session.OperatorVerified || !ReportValues.IsCanonicalGuid(tenant)
                || !ReportValues.IsCanonicalGuid(account) || !ReportValues.IsCanonicalGuid(client)
                || session.TenantId != tenant || source.TenantId != tenant || session.AccountObjectId != account
                || session.OperatorObjectId != account || session.ClientId != client || session.Mode != mode
                || ScopeKey(session.Scopes) != scopes)
                throw new TenantMismatchException("The verified service-health tenant, account, registration or access changed. No result was admitted for the current client.");
        }
        AssertCurrent();
        if (source.ReportId != "service-health" || !session.Scopes.Any(scope => scope is "ServiceHealth.Read.All" or "https://graph.microsoft.com/ServiceHealth.Read.All"))
            throw new ConfigurationException("Choose service-health access deliberately and reconnect with its returned read permission before checking this report.");
        if (string.IsNullOrWhiteSpace(toolkitVersion) || toolkitVersion.Length > 128 || toolkitVersion.Any(char.IsControl))
            throw new ConfigurationException("Service-health capture requires bounded toolkit-version provenance.");
        cancellation.ThrowIfCancellationRequested();
        var stamp = Timestamps.Format(clock.UtcNow);
        var report = new OperationalContextReport
        {
            ReportId = "service-health", AccessIntent = "service-health", TenantId = tenant, AccountObjectId = account,
            ClientId = client, PermissionMode = mode.ToString(), SourceMode = "live", AsOfUtc = stamp,
            StartedAt = stamp, EndedAt = stamp, ToolkitVersion = toolkitVersion,
            Sources = OperationalContextSchema.SourceDeclarations("service-health").ToList(),
            Sections = [Section("health-overviews"), Section("health-issues")]
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var calls = 0;
        var rows = 0;
        var rowBytes = 0;
        var stop = false;
        for (var index = 0; index < report.Sections.Count && !stop; index++)
        {
            var section = report.Sections[index];
            var kind = index == 0 ? OperationalReadKind.HealthOverviews : OperationalReadKind.HealthIssues;
            section.Error = null;
            section.Status = ReportReadState.Collected;
            try
            {
                var more = true;
                while (more)
                {
                    AssertCurrent(); deadline.Token.ThrowIfCancellationRequested();
                    if (calls >= OperationalReportRegistry.MaximumCalls)
                    { Incomplete(section, "The shared 100-request budget ended. Later pages and sources were not read."); stop = true; break; }
                    calls++;
                    var page = await source.ReadNextAsync(kind, null, deadline.Token).ConfigureAwait(false);
                    AssertCurrent(); deadline.Token.ThrowIfCancellationRequested();
                    if (page is null || page.Items is null) throw new ConfigurationException("The source did not return an identifiable page.");
                    // Mapping and bounded encoding can be sizeable even when a fake/cached source completes synchronously.
                    // Keep it off the WPF dispatcher; only the owned capture task can mutate this report.
                    await Task.Run(() =>
                    {
                    foreach (var item in page.Items)
                    {
                        if (item is null) throw new ConfigurationException("The source returned an invalid service object.");
                        if (rows >= OperationalReportRegistry.MaximumRows)
                        { Incomplete(section, "The shared 5,000-row budget ended. Later observations were not retained or inferred."); stop = true; break; }
                        var projected = Project(item, kind);
                        var encoded = JsonSerializer.SerializeToNode(projected, projected.GetType(), RegisteredReportJson.Strict)!.AsObject();
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(encoded, RegisteredReportJson.Strict).Length;
                        // Keep bounded finalisation space for source states, diagnostics and the digest.
                        if (rowBytes + bytes > OperationalReportRegistry.MaximumBytes - 128 * 1024)
                        { Incomplete(section, "The combined 16 MiB evidence budget ended. Earlier admitted observations remain; no row was truncated."); stop = true; break; }
                        section.Rows.Add(encoded); rows++; rowBytes += bytes;
                        if (projected.ReadStatus == ReportReadState.Partial)
                            Incomplete(section, "At least one returned object has missing, malformed or unsupported fields. Unknown values were not converted to healthy states.");
                    }
                    }, deadline.Token).ConfigureAwait(false);
                    AssertCurrent(); deadline.Token.ThrowIfCancellationRequested();
                    more = page.HasMore && !stop;
                }
            }
            catch (TenantMismatchException) { throw; }
            catch (OperationCanceledException)
            {
                AssertCurrent();
                section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : cancellation.IsCancellationRequested ? ReportReadState.Cancelled : ReportReadState.Failed;
                section.Error = cancellation.IsCancellationRequested
                    ? "The engineer stopped the read. Earlier admitted observations remain; later pages and sources were not attempted."
                    : "The shared five-minute read budget ended. Earlier observations remain; later sources were not attempted.";
                stop = true;
            }
            catch (AuthenticationRequiredException)
            { AssertCurrent(); Fail(section, "A deliberate reconnect is required. No interactive retry occurred; later sources were not attempted."); stop = true; }
            catch (ToolkitException)
            { AssertCurrent(); Fail(section, "This registered source could not be read or admitted completely. Earlier observations remain; no empty-success result was substituted."); }
            MarkDuplicates(section);
        }
        AssertCurrent();
        report.EndedAt = Timestamps.Format(clock.UtcNow);
        report.Status = ReportEvidenceSchema.Overall(report.Sections.Select(section => section.Status));
        await Task.Run(() => OperationalContextSchema.Seal(report), CancellationToken.None).ConfigureAwait(false);
        AssertCurrent();
        return report;
    }
    private static ReportSection Section(string id) => new()
    {
        Id = id, Error = "This selected source was not attempted.",
        Limitations = ["Microsoft service advisories are contextual observations, not proof of this tenant's availability or the cause of an incident. Missing fields remain unknown."]
    };
    private static string ScopeKey(IEnumerable<string> scopes) => string.Join('\n', scopes.OrderBy(value => value, StringComparer.Ordinal));
    private static void Incomplete(ReportSection section, string reason) { section.Status = ReportReadState.Partial; section.Error = reason; }
    private static void Fail(ReportSection section, string reason)
    { section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : ReportReadState.Failed; section.Error = reason; }
    private static void MarkDuplicates(ReportSection section)
    {
        foreach (var group in section.Rows.Where(row => row["id"] is not null).GroupBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            foreach (var row in group) { row["readStatus"] = ReportReadState.Partial; row["error"] = "The source returned a duplicate identity; every original observation remains explicitly partial."; }
            Incomplete(section, "Duplicate identities were observed. No complete or deduplicated result was inferred.");
        }
    }
    private static GraphReportRow Project(JsonObject raw, OperationalReadKind kind)
    {
        var partial = false;
        string? Text(string property, bool required = false, bool identity = false)
        {
            if (raw[property] is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length > (identity ? 512 : 4096)
                || identity && string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsControl(c) && (identity || c is not ('\r' or '\n' or '\t'))))
            { if (required || raw[property] is not null) partial = true; return null; }
            if (required && string.IsNullOrWhiteSpace(text)) { partial = true; return null; }
            return text;
        }
        string? Time(string property)
        {
            var text = Text(property);
            if (text is null) return null;
            if (DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) return Timestamps.Format(parsed);
            partial = true; return null;
        }
        var id = Text("id", true, true);
        var name = Text(kind == OperationalReadKind.HealthOverviews ? "service" : "title", true);
        var status = Text("status", true);
        if (!OperationalContextSchema.IsKnownHealthStatus(status)) partial = true;
        GraphReportRow row = kind == OperationalReadKind.HealthOverviews
            ? new ServiceHealthOverviewRow { Status = status }
            : new ServiceHealthIssueRow
            {
                Service = Text("service", true), Feature = Text("feature"), Classification = Text("classification"), Status = status,
                ImpactSummary = Text("impactDescription"), StartedAt = Time("startDateTime"), EndedAt = Time("endDateTime"), ModifiedAt = Time("lastModifiedDateTime")
            };
        if (row is ServiceHealthIssueRow issue && issue.Classification is not ("advisory" or "incident")) partial = true;
        row.Id = id; row.Name = name; row.ReadStatus = partial ? ReportReadState.Partial : ReportReadState.Collected;
        row.Error = partial ? "This returned object contains missing, malformed or unsupported values. Unknown fields remain unknown." : null;
        return row;
    }
}

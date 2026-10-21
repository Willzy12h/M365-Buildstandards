using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
/// <summary>In-memory collection, not sealed/importable evidence or deployment proof.</summary>
public sealed record CollaborationCapture(CollaborationReadSelection Selection, IReadOnlyList<CollaborationSourceCapture> Sources);
public sealed class CollaborationSourceCapture(CollaborationReadKind kind, string target)
{
    public CollaborationReadKind Kind { get; } = kind;
    public string Target { get; } = target;
    public string Status { get; internal set; } = ReportReadState.NotAttempted;
    public string? Error { get; internal set; } = "This selected source was not attempted.";
    public int? ReturnedCount { get; internal set; }
    public List<GraphReportRow> Rows { get; } = [];
}
public sealed class CollaborationInventoryCapture
{
    private const string Duplicate = "Repeated source identity: observations are retained, but uniqueness could not be established.";
    public async Task<CollaborationCapture> CaptureAsync(ICollaborationReportSource source, TenantSession session, Action requireCurrent, CancellationToken cancellation)
    {
        var selection = source.Selection;
        var tenant = session.TenantId; var account = session.AccountObjectId; var client = session.ClientId; var mode = session.Mode;
        static string ScopeKey(TenantSession value) => string.Join('\n', value.Scopes.OrderBy(s => s, StringComparer.Ordinal));
        var scopeKey = ScopeKey(session);
        void Current()
        {
            requireCurrent();
            if (!session.TenantVerified || !session.OperatorVerified || !ReportValues.IsCanonicalGuid(tenant)
                || !ReportValues.IsCanonicalGuid(account) || !ReportValues.IsCanonicalGuid(client)
                || source.TenantId != tenant || session.TenantId != tenant || session.AccountObjectId != account
                || session.OperatorObjectId != account || session.ClientId != client || session.Mode != mode
                || ScopeKey(session) != scopeKey || !ReferenceEquals(source.Selection, selection))
                throw new TenantMismatchException("The collaboration source, selection, tenant, account or access changed. No current-client result was admitted.");
        }
        Current();
        if (selection.Scopes().Any(scope => !session.Scopes.Contains(scope, StringComparer.Ordinal)
            && !session.Scopes.Contains("https://graph.microsoft.com/" + scope, StringComparer.Ordinal)))
            throw new ConfigurationException("The verified connection lacks a deliberately selected collaboration read permission.");
        cancellation.ThrowIfCancellationRequested();
        var sources = new List<CollaborationSourceCapture>();
        void Add(CollaborationReadKind kind, string target) => sources.Add(new(kind, target));
        if (selection.Search is not null) Add(CollaborationReadKind.SiteSearch, selection.Search);
        foreach (var site in selection.Sites)
        { Add(CollaborationReadKind.SiteMetadata, site); if (selection.Libraries) Add(CollaborationReadKind.SiteLibraries, site); }
        foreach (var group in selection.Groups)
        {
            Add(CollaborationReadKind.GroupMetadata, group);
            if (selection.Owners) Add(CollaborationReadKind.GroupOwners, group);
            if (selection.Members) Add(CollaborationReadKind.TeamMembers, group);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var admittedSites = new HashSet<string>(StringComparer.Ordinal);
        var admittedGroups = new Dictionary<string, CollaborationGroupRow>(StringComparer.Ordinal);
        var calls = 0; var rows = sources.Count; // reserve independent diagnostics in the final envelope
        var bytes = 0; var stopped = false;
        foreach (var section in sources)
        {
            if (stopped) break;
            Current();
            if (section.Kind == CollaborationReadKind.SiteLibraries && !admittedSites.Contains(section.Target))
            { section.Error = "Selected site metadata was not admitted; its libraries were not attempted."; continue; }
            if (section.Kind is CollaborationReadKind.GroupOwners or CollaborationReadKind.TeamMembers)
            {
                if (!admittedGroups.TryGetValue(section.Target, out var parent))
                { section.Error = "Selected group metadata was not admitted; this child source was not attempted."; continue; }
                if (section.Kind == CollaborationReadKind.GroupOwners && parent.OwnerReadDisposition == "BlockedUnsupportedPopulation")
                { section.Error = "Owner enumeration is unsupported for this observed population. " + CollaborationObservations.OwnerCoverage; continue; }
                if (section.Kind == CollaborationReadKind.TeamMembers && parent.TeamProvisioned != true)
                { section.Error = "Returned group metadata does not establish Team provisioning. Membership was not attempted."; continue; }
            }
            section.Status = ReportReadState.Collected; section.Error = null;
            try
            {
                var more = true;
                while (more)
                {
                    Current(); deadline.Token.ThrowIfCancellationRequested();
                    if (calls >= OperationalReportRegistry.MaximumCalls)
                        throw new OperationalReadLimitException("The shared request limit ended.");
                    calls++;
                    var page = await source.ReadCollaborationNextAsync(section.Kind, section.Target, deadline.Token).ConfigureAwait(false);
                    Current(); deadline.Token.ThrowIfCancellationRequested();
                    if (page?.Items is null) throw new ConfigurationException("The source did not return an identifiable page.");
                    var singleton = section.Kind is CollaborationReadKind.SiteMetadata or CollaborationReadKind.GroupMetadata;
                    if (singleton && (page.HasMore || page.Items.Count != 1))
                        throw new ConfigurationException("Selected parent metadata must be one exact returned object.");
                    await Task.Run(() =>
                    {
                        foreach (var item in page.Items)
                        {
                            deadline.Token.ThrowIfCancellationRequested();
                            if (item is null) throw new ConfigurationException("A returned collaboration object was missing.");
                            if (rows >= OperationalReportRegistry.MaximumRows)
                                throw new OperationalReadLimitException("The shared row limit ended.");
                            var projected = Project(section.Kind, section.Target, item);
                            if (singleton && projected.Id != section.Target)
                                throw new TenantMismatchException("Returned parent identity differs from the exact selection.");
                            var encoded = JsonSerializer.SerializeToNode(projected, projected.GetType(), RegisteredReportJson.Strict)!.AsObject();
                            var size = JsonSerializer.SerializeToUtf8Bytes(encoded, OperationalContextSchema.CompactOptions).Length;
                            encoded["readStatus"] = ReportReadState.Partial; encoded["error"] = Duplicate;
                            size = Math.Max(size, JsonSerializer.SerializeToUtf8Bytes(encoded, OperationalContextSchema.CompactOptions).Length);
                            if (bytes + size > OperationalReportRegistry.MaximumBytes - 256 * 1024)
                                throw new OperationalReadLimitException("The shared evidence byte limit ended.");
                            section.Rows.Add(projected); rows++; bytes += size;
                            if (projected.ReadStatus != ReportReadState.Collected)
                            { section.Status = ReportReadState.Partial; section.Error = "Returned metadata is incomplete or has documented population limitations. Unknown is not empty or effective access."; }
                        }
                    }, deadline.Token).ConfigureAwait(false);
                    Current(); deadline.Token.ThrowIfCancellationRequested();
                    more = page.HasMore;
                }
                section.ReturnedCount = section.Rows.Count;
                if (section.Kind == CollaborationReadKind.GroupOwners)
                { section.Status = ReportReadState.Partial; section.Error = CollaborationObservations.OwnerCoverage; }
                if (section.Kind == CollaborationReadKind.SiteMetadata) admittedSites.Add(section.Target);
                if (section.Kind == CollaborationReadKind.GroupMetadata) admittedGroups.Add(section.Target, (CollaborationGroupRow)section.Rows.Single());
            }
            catch (TenantMismatchException) { throw; }
            catch (OperationCanceledException)
            {
                Current(); stopped = true;
                section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : cancellation.IsCancellationRequested ? ReportReadState.Cancelled : ReportReadState.Failed;
                section.Error = cancellation.IsCancellationRequested ? "Stopped by the engineer. Earlier observations remain; later sources were not attempted." : "The five-minute deadline ended. Earlier observations remain; later sources were not attempted.";
            }
            catch (OperationalReadLimitException)
            { Current(); stopped = true; Fail(section, "A shared request, row, byte or time limit ended. Earlier observations remain; later sources were not attempted."); }
            catch (AuthenticationRequiredException)
            { Current(); stopped = true; Fail(section, "Reconnect deliberately before another read. No interactive retry occurred; later sources were not attempted."); }
            catch (ToolkitException)
            { Current(); Fail(section, "This selected source could not be read completely. Earlier observations remain; no empty-success substitute or retry occurred."); }
            foreach (var repeated in section.Rows.Where(row => row.Id is not null).GroupBy(row => row.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                foreach (var row in repeated) { row.ReadStatus = ReportReadState.Partial; row.Error = Duplicate; }
                section.Status = ReportReadState.Partial; section.Error = Duplicate;
            }
        }
        Current();
        return new(selection, sources.AsReadOnly());
    }
    private static void Fail(CollaborationSourceCapture section, string reason)
    { section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : ReportReadState.Failed; section.Error = reason; section.ReturnedCount = null; }
    private static GraphReportRow Project(CollaborationReadKind kind, string target, JsonObject item) => kind switch
    {
        CollaborationReadKind.SiteSearch => CollaborationObservations.Site(item),
        CollaborationReadKind.SiteMetadata => CollaborationObservations.Site(item, target),
        CollaborationReadKind.SiteLibraries => CollaborationObservations.Library(item, target),
        CollaborationReadKind.GroupMetadata => CollaborationObservations.Group(item, target),
        CollaborationReadKind.GroupOwners => CollaborationObservations.Owner(item, target),
        CollaborationReadKind.TeamMembers => CollaborationObservations.Member(item, target),
        _ => throw new ConfigurationException("This collaboration source is not registered.")
    };
}

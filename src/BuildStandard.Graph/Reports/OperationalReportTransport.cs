using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
namespace BuildStandard.Graph.Reports;

/// <summary>Fixed, bounded Graph GET pages. No generic URL, action or write interface.</summary>
public sealed class OperationalReportTransport : IOperationalReportSource, IDisposable
{
    private readonly HttpClient _client;
    private readonly IAccessTokenProvider _tokens;
    private readonly TenantSession _session;
    private readonly Action _requireCurrent;
    private readonly string _binding;
    private readonly bool _ownsClient;
    private readonly CancellationTokenSource _lifetime;
    private readonly Dictionary<string, Uri?> _next = [];
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private int _calls, _reading, _parentRows;
    private readonly string? _customerTenantId;
    private readonly CollaborationReadSelection? _collaboration;
    private readonly HashSet<string> _admittedSites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool Owners, bool Members)> _admittedGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _parentCandidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _children = new(StringComparer.Ordinal);
    private bool _parentsComplete;
    private bool _disposed;
    public string TenantId { get; }
    public string ReportId { get; }
    public string? CustomerTenantId => _customerTenantId;
    internal OperationalReportTransport(HttpClient client, IAccessTokenProvider tokens, TenantSession session,
        string reportId, Action requireCurrent, bool ownsClient = false, string? customerTenantId = null, bool partnerDeclared = false, CollaborationReadSelection? collaboration = null)
    {
        _client = client; _tokens = tokens; _session = session; _requireCurrent = requireCurrent; _ownsClient = ownsClient;
        TenantId = session.TenantId; ReportId = reportId;
        if (reportId == "collaboration-inventory")
            _collaboration = collaboration ?? throw Refuse("An exact frozen collaboration selection is required.");
        else
        {
            if (collaboration is not null) throw Refuse("Collaboration selection is not admitted for this report.");
            _ = OperationalReportRegistry.Scope(reportId);
        }
        if (reportId == "partner-relationships")
        {
            if (!partnerDeclared || !ReportValues.IsCanonicalGuid(customerTenantId) || customerTenantId == TenantId)
                throw Refuse("Declare the connected source as the partner organisation and select a different exact customer tenant. This does not prove effective customer access.");
            _customerTenantId = customerTenantId;
        }
        else if (customerTenantId is not null || partnerDeclared)
            throw Refuse("Health reads do not accept partner/customer parameters.");
        RequireVerified(); _binding = Binding(session); AssertCurrent(default);
        _lifetime = new(TimeSpan.FromMinutes(5));
    }
    internal static OperationalReportTransport Create(IAccessTokenProvider tokens, TenantSession session, string reportId, Action requireCurrent, string? customerTenantId = null, bool partnerDeclared = false, CollaborationReadSelection? collaboration = null)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { Timeout = Timeout.InfiniteTimeSpan };
        try { return new(client, tokens, session, reportId, requireCurrent, ownsClient: true, customerTenantId: customerTenantId, partnerDeclared: partnerDeclared, collaboration: collaboration); }
        catch { client.Dispose(); throw; }
    }
    public Task<OperationalReportPage> ReadCollaborationNextAsync(CollaborationReadKind kind, string value, CancellationToken cancellation)
    {
        var selection = _collaboration ?? throw Refuse("This source has no collaboration selection.");
        var registered = selection.RequirePath(kind, value);
        if (kind == CollaborationReadKind.SiteLibraries && !_admittedSites.Contains(value)
            || kind == CollaborationReadKind.GroupOwners && (!_admittedGroups.TryGetValue(value, out var owners) || !owners.Owners)
            || kind == CollaborationReadKind.TeamMembers && (!_admittedGroups.TryGetValue(value, out var members) || !members.Members))
            throw Refuse("The exact selected parent has not admitted this child section. Unsupported or unknown parent facts cannot authorise child requests.");
        return ReadCoreAsync(null, registered, kind, value, cancellation);
    }
    public Task<OperationalReportPage> ReadNextAsync(OperationalReadKind kind, string? relationshipId, CancellationToken cancellation)
        => ReadCoreAsync(kind, OperationalReportRegistry.Path(ReportId, kind, relationshipId), null, relationshipId, cancellation);
    private async Task<OperationalReportPage> ReadCoreAsync(OperationalReadKind? kind, string registered, CollaborationReadKind? collaborationKind, string? relationshipId, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0) throw Refuse("An operational read is already active. No concurrent cursor or authentication operation was started.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        var ct = deadline.Token;
        try
        {
            AssertCurrent(ct);
            var first = new Uri(GraphClient.Host + "/v1.0" + registered);
            if (kind == OperationalReadKind.PartnerAssignments)
            {
                if (!_parentsComplete || !_parentCandidates.TryGetValue(relationshipId!, out var selectedUnique) || !selectedUnique)
                    throw Refuse("This child is not bound to a unique selected-customer parent from a complete bounded parent scan. No child request was sent.");
                if (!_next.ContainsKey(registered) && !_children.Contains(relationshipId!))
                {
                    if (_children.Count >= OperationalReportRegistry.MaximumRelationships)
                        throw new OperationalReadLimitException("The 50 selected relationship inventory budget ended. No additional child was requested.");
                    _children.Add(relationshipId!);
                }
            }
            if (!_next.TryGetValue(registered, out var uri)) uri = first;
            if (uri is null) throw Refuse("This registered source is finished or was refused. Start a deliberate new report; this cursor cannot be replayed.");
            _next[registered] = null; // Any failure consumes this cursor; there is no repeat/fallback in this operation.
            if (!_visited.Add(uri.AbsoluteUri)) throw Refuse("The source repeated a pagination cursor. No repeated page was sent or admitted.");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                AssertCurrent(ct);
                if (_calls >= OperationalReportRegistry.MaximumCalls) throw new OperationalReadLimitException("The shared 100-request budget ended. No further request was sent.");
                var token = await _tokens.GetAccessTokenAsync(attempt == 1, ct).ConfigureAwait(false);
                AssertCurrent(ct);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _calls++;
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                AssertCurrent(ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new AuthenticationRequiredException("The operational read was refused after one silent renewal. Reconnect deliberately; no interactive retry occurred.");
                if (response.StatusCode != HttpStatusCode.OK) throw Refuse("The fixed operational source failed or redirected. No fallback, empty-success result or response body was retained.");
                if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                    throw Refuse("The operational source did not return admitted JSON.");
                var length = response.Content.Headers.ContentLength;
                if (length is < 0 or > OperationalReportRegistry.MaximumBytes) throw Refuse("The operational response exceeds its 16 MiB stream bound.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                AssertCurrent(ct);
                using var bytes = new MemoryStream(length is { } known ? (int)known : 0);
                var buffer = new byte[81920];
                while (true)
                {
                    AssertCurrent(ct);
                    var read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    AssertCurrent(ct);
                    if (read == 0) break;
                    if (read > OperationalReportRegistry.MaximumBytes - bytes.Length) throw Refuse("The operational response exceeds its 16 MiB stream bound. No truncated page was admitted.");
                    bytes.Write(buffer, 0, read);
                }
                if (length is not null && length != bytes.Length) throw Refuse("The operational response was incomplete. No empty or truncated success was admitted.");
                using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 48, AllowDuplicateProperties = false });
                var root = document.RootElement;
                var singleton = collaborationKind is CollaborationReadKind.SiteMetadata or CollaborationReadKind.GroupMetadata;
                var result = new List<JsonObject>();
                JsonElement items = default;
                if (root.ValueKind != JsonValueKind.Object) throw Refuse("The registered source returned no JSON object.");
                if (singleton)
                {
                    if (root.TryGetProperty("@odata.nextLink", out _) || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != relationshipId)
                        throw Refuse("Returned parent metadata does not identify the exact selected resource. No child was admitted.");
                    result.Add(JsonNode.Parse(root.GetRawText())!.AsObject());
                }
                else
                {
                    if (!root.TryGetProperty("value", out items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > PageLimit(kind))
                        throw Refuse("The operational source did not return a bounded collection page.");
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) throw Refuse("An operational collection item is not an object.");
                        result.Add(JsonNode.Parse(item.GetRawText())!.AsObject());
                    }
                }
                Uri? next = null;
                if (root.TryGetProperty("@odata.nextLink", out var nextValue))
                {
                    if (nextValue.ValueKind != JsonValueKind.String) throw Refuse("The operational continuation is not an admitted address.");
                    next = RequireContinuation(nextValue.GetString()!, first);
                    if (_visited.Contains(next.AbsoluteUri)) throw Refuse("The operational source repeated a pagination cursor. No repeated page was admitted.");
                }
                AssertCurrent(ct);
                if (kind == OperationalReadKind.PartnerRelationships)
                {
                    if (items.GetArrayLength() > OperationalReportRegistry.MaximumRows - _parentRows)
                        throw new OperationalReadLimitException("The 5,000 parent-row scan budget ended. The parent scan cannot authorise children.");
                    _parentRows += items.GetArrayLength();
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("id", out var identity) || identity.ValueKind != JsonValueKind.String) continue;
                        var id = identity.GetString()!;
                        try { _ = OperationalReportRegistry.Path(ReportId, OperationalReadKind.PartnerAssignments, id); }
                        catch (ConfigurationException) { continue; }
                        var selected = item.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.Object
                            && customer.TryGetProperty("tenantId", out var customerId) && customerId.ValueKind == JsonValueKind.String
                            && customerId.GetString() == _customerTenantId;
                        // All repeated IDs are ambiguous, including repeats attributed to another/missing customer.
                        if (!_parentCandidates.TryAdd(id, selected)) _parentCandidates[id] = false;
                    }
                    _parentsComplete = next is null;
                }
                if (collaborationKind == CollaborationReadKind.SiteMetadata) _admittedSites.Add(relationshipId!);
                if (collaborationKind == CollaborationReadKind.GroupMetadata)
                    _admittedGroups.Add(relationshipId!, (!CollaborationPopulation.OwnersUnavailable(result[0]), CollaborationPopulation.HasTeam(result[0]) == true));
                _next[registered] = next;
                return new(result, next is not null);
            }
            throw Refuse("No operational page was admitted.");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && _lifetime.IsCancellationRequested)
        { throw new OperationalReadLimitException("The combined five-minute operational read budget ended. No partial page was admitted."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new AuthenticationRequiredException("The operational read was interrupted before a complete page was admitted. Reconnect deliberately; no interactive retry occurred."); }
        catch (JsonException) { throw Refuse("The operational response contains invalid, duplicate or overly nested JSON. No page was admitted."); }
        catch (FormatException) { throw Refuse("The operational response contains malformed headers or continuation data. No page was admitted."); }
        catch (HttpRequestException) { throw Refuse("The operational transport failed. No raw response or continuation was retained in the error."); }
        catch (IOException) { throw Refuse("The operational stream could not be read completely. No partial page was admitted."); }
        finally { Volatile.Write(ref _reading, 0); }
    }
    private static int PageLimit(OperationalReadKind? kind) => kind switch
    { OperationalReadKind.PartnerRelationships => 300, OperationalReadKind.HealthIssues => 1000, null => 1000, _ => OperationalReportRegistry.MaximumRows };
    private static Uri RequireContinuation(string address, Uri first)
    {
        var prefix = first.GetLeftPart(UriPartial.Path) + "?";
        if (address.Length > 8192 || address.Any(char.IsControl) || !address.StartsWith(prefix, StringComparison.Ordinal)
            || !Uri.TryCreate(address, UriKind.Absolute, out var next) || next.Fragment.Length != 0 || next.UserInfo.Length != 0
            || next.Scheme != Uri.UriSchemeHttps || next.Port != 443 || next.Host != "graph.microsoft.com" || next.AbsolutePath != first.AbsolutePath)
            throw Refuse("The operational continuation is not the exact registered public Graph path.");
        var original = Query(first.Query);
        var query = Query(next.Query);
        if (!query.ContainsKey("$skiptoken") && !query.ContainsKey("$skip")) throw Refuse("The operational continuation has no admitted page cursor.");
        foreach (var (key, value) in query)
        {
            if (key == "$skiptoken") { if (string.IsNullOrWhiteSpace(value)) throw Refuse("The operational page cursor is empty."); }
            else if (key == "$skip") { if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var skip) || skip < 0) throw Refuse("The operational skip cursor is invalid."); }
            else if (key == "$top") { if (!int.TryParse(value, out var top) || top < 1 || top > (first.AbsolutePath == "/v1.0/tenantRelationships/delegatedAdminRelationships" ? 300 : 1000)) throw Refuse("The operational page size exceeds its admitted bound."); }
            else if (key is not ("$select" or "search") || !original.TryGetValue(key, out var expected) || value != expected)
                throw Refuse("The operational continuation changed the registered query or requested an unsupported option.");
        }
        if (original.TryGetValue("search", out var search) && (!query.TryGetValue("search", out var continuedSearch) || search != continuedSearch))
            throw Refuse("Search continuation changed or omitted the frozen search scope.");
        return next;
    }
    private static Dictionary<string, string> Query(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split('=', 2);
            if (pair.Length != 2) throw Refuse("The operational continuation query is malformed.");
            var key = Uri.UnescapeDataString(pair[0]); var value = Uri.UnescapeDataString(pair[1]);
            if (key.Any(char.IsControl) || value.Any(char.IsControl) || !values.TryAdd(key, value)) throw Refuse("The operational continuation has duplicate or invalid query values.");
        }
        return values;
    }
    private void RequireVerified()
    {
        var scopes = _collaboration?.Scopes() ?? new[] { OperationalReportRegistry.Scope(ReportId) };
        if (!_session.TenantVerified || !_session.OperatorVerified || !ReportValues.IsCanonicalGuid(_session.TenantId)
            || !ReportValues.IsCanonicalGuid(_session.AccountObjectId) || _session.OperatorObjectId != _session.AccountObjectId
            || !ReportValues.IsCanonicalGuid(_session.ClientId) || string.IsNullOrWhiteSpace(_session.Account) || !Enum.IsDefined(_session.Mode)
            || _session.Scopes is null || !scopes.All(scope => _session.Scopes.Any(value => value == scope || value == "https://graph.microsoft.com/" + scope)))
            throw Refuse("The operational source requires its verified delegated account, registration and actually returned optional read scope. No consent or privilege fallback occurs.");
    }
    private void AssertCurrent(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); _requireCurrent(); RequireVerified();
        if (_binding != Binding(_session)) throw new TenantMismatchException("The operational tenant, account, registration, mode or returned scopes changed. No page was admitted.");
    }
    private static string Binding(TenantSession session) => JsonSerializer.Serialize(new { session.TenantId, session.AccountObjectId, session.Account, session.ClientId, session.Mode,
        Scopes = session.Scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray(), Resource = "https://graph.microsoft.com" });
    private static ToolkitException Refuse(string reason) => new(reason);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        if (_ownsClient) _client.Dispose();
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Strict, bounded report reader. Registered row schemas prevent raw secret-bearing API output imports.</summary>
public static class ReportEvidenceSchema
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Strict = MakeOptions();
    private static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions(ToolkitJson.Options)
        {
            PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never, MaxDepth = 48
        };
        options.Converters.Clear(); options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static JsonObject Row<T>(T row) where T : GraphReportRow =>
        JsonSerializer.SerializeToNode(row, Strict)!.AsObject();
    public static string Serialize(ReportEvidence evidence) => JsonSerializer.Serialize(evidence, Strict);
    public static ReportEvidence Read(string json, string expectedTenantId)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Bad("Report evidence exceeds the 32 MiB reader limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.Ordinal);
            var evidence = JsonSerializer.Deserialize<ReportEvidence>(json, Strict) ?? throw Bad("Report evidence is empty.");
            Validate(evidence, expectedTenantId);
            return evidence;
        }
        catch (JsonException ex) { throw Bad("Invalid report evidence JSON: " + ex.Message); }
    }

    public static void Seal(ReportEvidence evidence)
    {
        evidence.IntegrityDigest = EvidenceIntegrity.Compute(evidence);
        Validate(evidence, evidence.TenantId);
    }

    public static string Overall(IEnumerable<string> states)
    {
        var values = states.ToList();
        if (values.Count == 0 || values.All(s => s == ReportReadState.NotAttempted)) return ReportReadState.NotAttempted;
        if (values.Contains(ReportReadState.Cancelled)) return ReportReadState.Cancelled;
        if (values.All(s => s == ReportReadState.Collected)) return ReportReadState.Collected;
        if (values.Any(s => s is ReportReadState.Collected or ReportReadState.Partial)) return ReportReadState.Partial;
        return ReportReadState.Failed;
    }

    public static void Validate(ReportEvidence e, string expectedTenantId)
    {
        if (e.SchemaVersion != 1 || e.Kind != "reportEvidence" || e.ReportSchemaVersion != 1 || e.Resource != "Graph")
            throw Bad("Unsupported report evidence kind, resource or schema.");
        GuidValue(e.Id); GuidValue(e.TenantId); GuidValue(expectedTenantId);
        if (!string.Equals(e.TenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("Report evidence belongs to another tenant.");
        if (e.SourceMode == "live") GuidValue(e.AccountObjectId);
        else if (e.SourceMode == "historical") { if (e.AccountObjectId is not null) GuidValue(e.AccountObjectId); }
        else throw Bad("Unknown report source mode.");
        var definition = GraphReportRegistry.Find(e.ReportId);
        if (!Utc(e.StartedAt, out var started) || !Utc(e.EndedAt, out var ended) || ended < started)
            throw Bad("The report collection interval is invalid.");
        if (string.IsNullOrWhiteSpace(e.ToolkitVersion) || string.IsNullOrWhiteSpace(e.ModuleVersion)
            || e.ToolkitVersion.Length > 128 || e.ModuleVersion.Length > 128) throw Bad("Missing report tool/adapter provenance.");
        ValidateParameters(definition, e.Parameters);
        Strings(e.Limitations);
        if (e.Sources is null || e.Sources.Count != definition.Routes.Count
            || e.Sources.Any(s => s is null || s.Api != "v1.0" || s.Reference != definition.Reference)
            || !e.Sources.Select(s => s.RegisteredRoute).SequenceEqual(definition.Routes.Select(r => r.Path)))
            throw Bad("Report source routes do not match the registered adapter.");
        if (e.Sections is null || e.Sections.Count != definition.Sections.Count || e.Sections.Any(s => s is null)
            || !e.Sections.Select(s => s.Id).SequenceEqual(definition.Sections.Select(s => s.Id)))
            throw Bad("Missing, duplicated or unrelated report sections.");
        foreach (var section in e.Sections)
        {
            State(section.Status); Strings(section.Limitations);
            if (section.Rows is null || section.Rows.Count > GraphReportRegistry.MaximumRows || section.Rows.Any(r => r is null))
                throw Bad("Invalid or excessive report rows.");
            if (section.Status != ReportReadState.Collected && string.IsNullOrWhiteSpace(section.Error))
                throw Bad("Unsuccessful/partial report sections require an explicit reason.");
            if (section.Status == ReportReadState.Collected && section.Error is not null)
                throw Bad("A successful report section cannot conceal a read error.");
            if (section.Status is ReportReadState.Failed or ReportReadState.NotAttempted && section.Rows.Count != 0)
                throw Bad("Failed/unattempted sections cannot claim collected rows.");
            var schema = definition.Sections.Single(s => s.Id == section.Id);
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in section.Rows)
            {
                GraphReportRow row;
                try { row = (GraphReportRow)(JsonSerializer.Deserialize(node.ToJsonString(), schema.RowType, Strict) ?? throw Bad("Empty report row.")); }
                catch (JsonException ex) { throw Bad("Invalid registered report row: " + ex.Message); }
                State(row.ReadStatus);
                if (row.Id is not null && (!identities.Add(row.Id) || !(schema.GuidIdentity ? ReportValues.IsCanonicalGuid(row.Id) : ReportValues.IsTextIdentity(row.Id))))
                    throw Bad("Duplicate, malformed or empty report identity.");
                if (row.ReadStatus == ReportReadState.Collected && (row.Id is null || row.Error is not null)) throw Bad("A successful row needs exact identity and no hidden error.");
                if (row.ReadStatus != ReportReadState.Collected && string.IsNullOrWhiteSpace(row.Error)) throw Bad("An unsuccessful/partial row needs a reason.");
                if (section.Status == ReportReadState.Collected && row.ReadStatus != ReportReadState.Collected) throw Bad("Incomplete rows cannot become a successful section.");
                if (row is SubscriptionReportRow subscription && (subscription.SkuId is not null && !ReportValues.IsCanonicalGuid(subscription.SkuId)
                    || subscription.SkuId is null && subscription.ReadStatus == ReportReadState.Collected))
                    throw Bad("Subscription rows need an exact SKU identity before they can be successful.");
                if (row is UserLicenceReportRow user)
                {
                    State(user.ProductsReadStatus);
                    if (user.Products is null || user.Products.Count > 256 || user.Products.Any(p => p is null || p.ServicePlans is null || p.ServicePlans.Count > 4096 || p.ServicePlans.Any(s => s is null)))
                        throw Bad("Invalid assigned product/service-plan rows.");
                    if (user.ProductsReadStatus != ReportReadState.Collected && user.ReadStatus == ReportReadState.Collected)
                        throw Bad("Incomplete licence reads cannot become a successful user row.");
                    if (user.ProductsReadStatus == ReportReadState.Collected && user.Products.Select(p => p.SkuId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != user.Products.Count)
                        throw Bad("Collected products cannot contain duplicate SKU identities.");
                    foreach (var product in user.Products)
                    {
                        if (product.SkuId is not null) CanonicalGuid(product.SkuId);
                        if (user.ProductsReadStatus == ReportReadState.Collected && product.SkuId is null) throw Bad("Collected products require exact SKU identity.");
                        if (user.ProductsReadStatus == ReportReadState.Collected && (!ReportValues.HasText(product.SkuPartNumber)
                            || product.ServicePlans.Any(s => s.ServicePlanId is null || !ReportValues.HasText(s.ServicePlanName) || !ReportValues.HasText(s.ProvisioningStatus))
                            || product.ServicePlans.Select(s => s.ServicePlanId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != product.ServicePlans.Count))
                            throw Bad("Collected product/service-plan details need complete, unique reported identities and states.");
                        foreach (var plan in product.ServicePlans) if (plan.ServicePlanId is not null) CanonicalGuid(plan.ServicePlanId);
                    }
                }
            }
        }
        State(e.Status);
        if (e.Status != Overall(e.Sections.Select(s => s.Status))) throw Bad("The report cannot claim a better state than its sections.");
        if (e.IntegrityDigest is not { Length: 64 } || !e.IntegrityDigest.All(Uri.IsHexDigit) || !EvidenceIntegrity.Verify(e, e.IntegrityDigest))
            throw Bad("Report evidence failed its integrity check. Digests do not authenticate source claims.");
    }

    public static void ValidateParameters(GraphReportRegistry.Definition definition, ReportParameters? parameters)
    {
        if (parameters is null) throw Bad("Report parameters are missing.");
        if (definition.DateRange)
        {
            if (!Utc(parameters.Start, out var start) || !Utc(parameters.End, out var end) || end <= start || end - start > TimeSpan.FromDays(31))
                throw Bad("Log reports require a UTC start/end range of at most 31 days. This bound does not establish available retention.");
        }
        else if (parameters.Start is not null || parameters.End is not null) throw Bad("This report has no date-range parameters.");
    }
    private static bool Utc(string? value, out DateTimeOffset at) => Timestamps.TryParse(value, out at) && value!.EndsWith('Z');
    private static void GuidValue(string? value) { if (!ReportValues.TryGuid(value, out _)) throw Bad("Invalid report identity."); }
    private static void CanonicalGuid(string value) { if (!ReportValues.IsCanonicalGuid(value)) throw Bad("Invalid report identity."); }
    private static void State(string state) { if (!ReportReadState.All.Contains(state, StringComparer.Ordinal)) throw Bad("Unknown report read state."); }
    private static void Strings(List<string>? values) { if (values is null || values.Count > 64 || values.Any(s => s is null || s.Length > 4096)) throw Bad("Invalid report limitations."); }
    private static ConfigurationException Bad(string message) => new(message);
}

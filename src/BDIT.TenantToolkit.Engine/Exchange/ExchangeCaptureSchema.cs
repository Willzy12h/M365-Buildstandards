using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Exchange;

public enum CaptureValueKind { Text, Boolean, Number, TextList }
public sealed record ExchangeCaptureDefinition(string Command, bool Purview, IReadOnlyDictionary<string, CaptureValueKind> Fields);

/// <summary>The capture script and importer share the same narrow projection; arbitrary cmdlet output is never imported.</summary>
public static class ExchangeCaptureSchema
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public const string Source = "M365 BuildStandard delegated read-only Exchange export v1";
    private static readonly JsonSerializerOptions StrictJson = new(ToolkitJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 20
    };

    private static ExchangeCaptureDefinition Definition(string command, string fields, bool purview = false) => new(command, purview,
        fields.Split(' ').ToDictionary(f => f.Split(':')[0], f => f.Split(':')[1] switch
        { "b" => CaptureValueKind.Boolean, "n" => CaptureValueKind.Number, "a" => CaptureValueKind.TextList, _ => CaptureValueKind.Text }, StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, ExchangeCaptureDefinition> Definitions { get; } = new Dictionary<string, ExchangeCaptureDefinition>(StringComparer.Ordinal)
    {
        ["acceptedDomains"] = Definition("Get-AcceptedDomain", "DomainName:t"),
        ["auditConfig"] = Definition("Get-AdminAuditLogConfig", "UnifiedAuditLogIngestionEnabled:b"),
        ["auditRetention"] = Definition("Get-UnifiedAuditLogRetentionPolicy", "Name:t Priority:n RetentionDuration:t RecordTypes:a Operations:a UserIds:a", purview: true),
        ["transportRules"] = Definition("Get-TransportRule", "Name:t State:t Mode:t SenderAddressLocation:t FromAddressMatchesPatterns:a HeaderMatchesMessageHeader:t HeaderMatchesPatterns:a SetSCL:n Conditions:a Exceptions:a Actions:a"),
        ["presetEop"] = Definition("Get-EOPProtectionPolicyRule", "Name:t State:t SentTo:a SentToMemberOf:a RecipientDomainIs:a ExceptIfSentTo:a ExceptIfSentToMemberOf:a ExceptIfRecipientDomainIs:a"),
        ["presetAtp"] = Definition("Get-ATPProtectionPolicyRule", "Name:t State:t SentTo:a SentToMemberOf:a RecipientDomainIs:a ExceptIfSentTo:a ExceptIfSentToMemberOf:a ExceptIfRecipientDomainIs:a"),
        ["outboundPolicies"] = Definition("Get-HostedOutboundSpamFilterPolicy", "Identity:t IsDefault:b AutoForwardingMode:t"),
        ["transportConfig"] = Definition("Get-TransportConfig", "SmtpClientAuthenticationDisabled:b"),
        ["externalTags"] = Definition("Get-ExternalInOutlook", "Enabled:b AllowList:a"),
        ["organisation"] = Definition("Get-OrganizationConfig", "AuditDisabled:b"),
        ["dkim"] = Definition("Get-DkimSigningConfig", "Name:t Enabled:b Status:t Selector1CNAME:t Selector2CNAME:t")
    };

    public static ExchangeCapture Parse(string json, string expectedTenant, DateTimeOffset now)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new ConfigurationException("Exchange capture exceeds the 2 MiB limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
            RejectDuplicates(document.RootElement);
            var capture = JsonSerializer.Deserialize<ExchangeCapture>(json, StrictJson) ?? throw new ConfigurationException("Exchange capture is empty.");
            Validate(capture, expectedTenant, now);
            if (capture.Dns.Count != 0) throw new ConfigurationException("Imported captures cannot supply DNS answers; run the separate DNS check.");
            return capture;
        }
        catch (JsonException ex) { throw new ConfigurationException("Exchange capture does not match the supported schema.", ex); }
    }

    public static void Validate(ExchangeCapture capture, string expectedTenant, DateTimeOffset now)
    {
        if (capture.SchemaVersion != 1 || capture.Source != Source || !capture.Delegated || !Guid.TryParse(capture.Id, out _)
            || !Guid.TryParse(capture.TenantId, out _) || !Version.TryParse(capture.ModuleVersion, out var version) || version < new Version(3, 2))
            throw new ConfigurationException("Unsupported or incomplete delegated Exchange capture provenance.");
        if (!string.Equals(capture.TenantId, expectedTenant, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(capture.ExchangeTenantId, expectedTenant, StringComparison.OrdinalIgnoreCase)
            || (capture.PurviewTenantId is not null && !string.Equals(capture.PurviewTenantId, expectedTenant, StringComparison.OrdinalIgnoreCase)))
            throw new TenantMismatchException("Exchange/Purview capture belongs to a different tenant.");
        if (!Timestamps.TryParse(capture.CapturedAt, out var time) || time > now.AddMinutes(5) || time.Year < 2000)
            throw new ConfigurationException("Exchange capture time is missing or invalid.");
        if (MailDomain.Validate(capture.Domain) != capture.Domain) throw new ConfigurationException("Exchange capture domain is not canonical.");
        if (capture.Collections is null || capture.Dns is null || capture.Collections.Count > Definitions.Count)
            throw new ConfigurationException("Exchange capture collections are invalid.");
        foreach (var (key, collection) in capture.Collections)
        {
            if (!Definitions.TryGetValue(key, out var def) || collection is null || collection.Command != def.Command
                || collection.Status is not (CaptureStatus.Collected or CaptureStatus.Error or CaptureStatus.NotAttempted)
                || collection.Items is null || collection.Items.Count > 10000 || collection.Error?.Length > 500)
                throw new ConfigurationException("Exchange capture contains an unsupported collection.");
            if (def.Purview && collection.Status == CaptureStatus.Collected && capture.PurviewTenantId is null)
                throw new ConfigurationException("Collected Purview evidence has no observed Purview tenant identity.");
            if (collection.Status != CaptureStatus.Collected && collection.Items.Count != 0)
                throw new ConfigurationException("A failed Exchange collection must not contain apparently successful items.");
            foreach (var item in collection.Items)
            {
                if (item is null) throw new ConfigurationException("Exchange collection contains a null item.");
                foreach (var (field, value) in item)
                    if (!def.Fields.TryGetValue(field, out var kind) || (value is not null && !ValidValue(value, kind)))
                        throw new ConfigurationException($"Exchange collection {key} contains an unsupported field or value: {field}.");
            }
        }
        if (capture.Dns.Count > 3) throw new ConfigurationException("Too many DNS observations.");
        foreach (var dns in capture.Dns)
        {
            if (dns is null || !Enum.IsDefined(dns.Kind) || !Enum.IsDefined(dns.Status) || dns.Records is null || dns.Records.Count > 100
                || dns.Records.Any(r => r is null || r.Length > 4096 || r.Any(char.IsControl)) || dns.Error?.Length > 500
                || !Timestamps.TryParse(dns.QueriedAt, out var queried) || queried > now.AddMinutes(5)
                || !AllowedDnsName(capture.Domain, dns.Name, dns.Kind)
                || (dns.Status == DnsResultStatus.Present ? dns.Records.Count == 0 : dns.Records.Count > 0))
                throw new ConfigurationException("DNS observation is invalid or belongs to another domain.");
        }
    }

    public static bool AllowedDnsName(string domain, string name, DnsRecordKind kind) => kind == DnsRecordKind.Txt
        ? name == "_dmarc." + domain : name == "selector1._domainkey." + domain || name == "selector2._domainkey." + domain;

    public static bool Complete(ExchangeCapture capture, string key, out IReadOnlyList<JsonObject> items)
    {
        items = Array.Empty<JsonObject>();
        if (!capture.Collections.TryGetValue(key, out var collection) || collection.Status != CaptureStatus.Collected) return false;
        var fields = Definitions[key].Fields;
        if (collection.Items.Any(i => fields.Any(f => !i.ContainsKey(f.Key) || (i[f.Key] is null
            && !(key == "transportRules" && f.Key is "SetSCL" or "HeaderMatchesMessageHeader" or "SenderAddressLocation"))))) return false;
        items = collection.Items; return true;
    }

    private static bool ValidValue(JsonNode value, CaptureValueKind kind) => kind switch
    {
        CaptureValueKind.Boolean => value is JsonValue b && b.TryGetValue<bool>(out _),
        CaptureValueKind.Number => value is JsonValue n && n.TryGetValue<int>(out _),
        CaptureValueKind.Text => value is JsonValue t && t.TryGetValue<string>(out var s) && s.Length <= 4096 && !s.Any(char.IsControl),
        CaptureValueKind.TextList => value is JsonArray a && a.Count <= 2048 && a.All(v => v is not null && ValidValue(v, CaptureValueKind.Text)),
        _ => false
    };

    private static void RejectDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            { if (!names.Add(property.Name)) throw new ConfigurationException("Exchange capture contains duplicate JSON properties."); RejectDuplicates(property.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) RejectDuplicates(item);
    }
}

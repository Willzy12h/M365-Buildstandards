using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Invented observations only. Never reads a client capture or calls PowerShell, Graph or DNS.</summary>
internal static class ExchangeTestData
{
    public static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    public const string Domain = "example.invalid";
    public static StandardCatalogue Standard() => StandardsLoader.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../../standards/2026.09.12.json"))), "2026.09.12.json");

    public static ExchangeCapture Capture()
    {
        var capture = new ExchangeCapture { SchemaVersion = 1, Id = "88888888-8888-4888-8888-888888888888", TenantId = TestData.TenantA,
            ExchangeTenantId = TestData.TenantA, PurviewTenantId = TestData.TenantA, Delegated = true, Domain = Domain,
            CapturedAt = Timestamps.Format(Now), ModuleVersion = "3.9.2", Source = ExchangeCaptureSchema.Source };
        foreach (var (key, definition) in ExchangeCaptureSchema.Definitions)
        {
            var row = new JsonObject();
            foreach (var (field, kind) in definition.Fields)
                row[field] = kind switch { CaptureValueKind.Text => JsonValue.Create(""), CaptureValueKind.Boolean => JsonValue.Create(false),
                    CaptureValueKind.Number => JsonValue.Create(0), _ => new JsonArray() };
            capture.Collections[key] = new ExchangeCollectionCapture { Command = definition.Command, Status = CaptureStatus.Collected, Items = new() { row } };
        }
        capture.Collections["acceptedDomains"].Items[0]["DomainName"] = Domain;
        capture.Collections["auditConfig"].Items[0]["UnifiedAuditLogIngestionEnabled"] = true;
        capture.Collections["auditRetention"].Items.Clear();
        capture.Collections["transportRules"].Items.Clear();
        foreach (var key in new[] { "presetEop", "presetAtp" })
        { capture.Collections[key].Items[0]["Name"] = "Standard Preset Security Policy"; capture.Collections[key].Items[0]["State"] = "Enabled"; }
        var outbound = capture.Collections["outboundPolicies"].Items[0]; outbound["Identity"] = "Default"; outbound["IsDefault"] = true; outbound["AutoForwardingMode"] = "Off";
        capture.Collections["transportConfig"].Items[0]["SmtpClientAuthenticationDisabled"] = true;
        capture.Collections["externalTags"].Items[0]["Enabled"] = true;
        var dkim = capture.Collections["dkim"].Items[0];
        dkim["Name"] = Domain; dkim["Enabled"] = true; dkim["Status"] = "Valid";
        dkim["Selector1CNAME"] = "selector1-synthetic._domainkey.example.invalid";
        dkim["Selector2CNAME"] = "selector2-synthetic._domainkey.example.invalid";
        return capture;
    }

    public static DnsObservation Answer(string name, DnsRecordKind kind, params string[] records) => new()
    { Name = name, Kind = kind, Status = records.Length == 0 ? DnsResultStatus.NoRecords : DnsResultStatus.Present, Records = records.ToList(), QueriedAt = Timestamps.Format(Now) };

    public static void AddDns(ExchangeCapture capture)
    {
        capture.Dns.Add(Answer("selector1._domainkey." + Domain, DnsRecordKind.Cname, "selector1-synthetic._domainkey.example.invalid."));
        capture.Dns.Add(Answer("selector2._domainkey." + Domain, DnsRecordKind.Cname, "selector2-synthetic._domainkey.example.invalid"));
        capture.Dns.Add(Answer("_dmarc." + Domain, DnsRecordKind.Txt, "v=DMARC1; p=reject;"));
    }
}

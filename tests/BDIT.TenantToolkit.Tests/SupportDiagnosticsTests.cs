using System.IO.Compression;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// CLA-20261006-10: the support bundle gains opt-in, previewed sections. The default output is unchanged, the exported
/// text is the previewed text, and seeded tenant, account, token and object identifiers never appear in any section.
/// </summary>
public sealed class SupportDiagnosticsTests
{
    private const string Upn = "seeded.engineer@contoso.example";
    private const string Token = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.seeded-token";
    private const string ObjectId = "0b5f1c2d-3e4f-4a5b-8c6d-7e8f9a0b1c2d";

    private static SupportContext SeededContext()
    {
        var snapshot = TestData.Snapshot(TestData.Standard());
        snapshot.TenantName = "Seeded Client Ltd"; snapshot.ClientLabel = "Seeded Client"; snapshot.PrimaryDomain = "contoso.example"; snapshot.CapturedBy = Upn;
        snapshot.Collections.First().Value.Status = CaptureStatus.Error;
        snapshot.Collections.First().Value.Error = $"403 for {Upn} on {ObjectId} with {Token}";
        snapshot.Collections["/groups/" + ObjectId] = new CollectionCapture { Status = CaptureStatus.Error };
        var errors = new RecentGraphErrors();
        errors.Record(DateTimeOffset.UtcNow, "GET", "/identity/conditionalAccess/policies", 403, "Authorization_RequestDenied", "11111111-2222-4333-8444-555555555555", null);
        errors.Record(DateTimeOffset.UtcNow, "PATCH", "/groups/" + ObjectId, 400, Upn, Token, Upn);
        return new SupportContext(150, snapshot, new ToolkitSettings(), errors.Snapshot());
    }

    private static readonly SupportSections All = new(true, true, true, true);

    [Fact]
    public void Default_preview_is_unchanged_and_names_no_optional_file()
    {
        var standard = TestData.Standard();
        var preview = SupportBundle.Preview(standard);
        Assert.StartsWith("Files: README.txt, technical-metadata.txt, SHA256SUMS.txt\n\n" + SupportBundle.Guide + "\n\nProduct version: ", preview);
        Assert.Equal(preview, SupportBundle.Preview(standard, SupportSections.None, SeededContext()));
        Assert.DoesNotContain("diagnostics.txt", preview);
    }

    [Fact]
    public void Optional_sections_never_contain_seeded_identifiers_and_export_exactly_what_was_previewed()
    {
        using var root = new TempRoot();
        var standard = TestData.Standard(); var context = SeededContext();
        var preview = SupportBundle.Preview(standard, All, context);
        Assert.Contains("diagnostics.txt", preview);
        Assert.Contains("Display scale: 150%", preview);
        Assert.Contains("request-id=11111111-2222-4333-8444-555555555555", preview);
        Assert.Contains("(route withheld)", preview);
        Assert.Contains("Graph read timeout (seconds): 120", preview);

        var zip = SupportBundle.Export(root.Paths, standard, All, context);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "README.txt", "SHA256SUMS.txt", "diagnostics.txt", "technical-metadata.txt" }, archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        var seeded = new[] { Upn, Token, ObjectId, TestData.TenantA, "Seeded Client", "contoso.example", root.Root };
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            foreach (var value in seeded) Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            if (entry.FullName is "diagnostics.txt" or "technical-metadata.txt") Assert.Contains(text, preview);
        }
        foreach (var value in seeded) Assert.DoesNotContain(value, preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Error_record_accepts_only_strictly_shaped_fields_and_keeps_the_most_recent()
    {
        var errors = new RecentGraphErrors(capacity: 2);
        errors.Record(DateTimeOffset.UtcNow, "GET", "/a", 500, "code one", "not-a-guid", null);
        errors.Record(DateTimeOffset.UtcNow, "TRACE", "/b?filter=" + Upn, 404, "NotFound", null, null);
        errors.Record(DateTimeOffset.UtcNow, "GET", "/c", 429, "TooManyRequests", null, "AAAAAAAA-0000-4000-8000-000000000001");
        var records = errors.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.Equal(("OTHER", "(route withheld)", "NotFound"), (records[0].Method, records[0].Route, records[0].ErrorCode));
        Assert.Equal("aaaaaaaa-0000-4000-8000-000000000001", records[1].ClientRequestId);
    }
}

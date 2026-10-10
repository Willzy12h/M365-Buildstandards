using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class GraphReportEvidenceBrowserTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_path_during_enumeration_is_incomplete_and_retains_already_listed_records(bool directoryMissing)
    {
        using var f = new ConnectedReportsFixture(); var saved = await f.Read();
        IEnumerable<string> VanishingEnumeration(string folder)
        {
            Assert.Equal(Path.GetDirectoryName(saved.File), folder);
            yield return saved.File;
            if (directoryMissing) throw new DirectoryNotFoundException("Synthetic folder vanished during listing.");
            throw new FileNotFoundException("Synthetic file vanished during listing.");
        }
        var browser = new GraphReportEvidenceBrowser(f.Workspace.Evidence);
        typeof(GraphReportEvidenceBrowser).GetProperty("EnumerateEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(browser, (Func<string, IEnumerable<string>>)VanishingEnumeration);
        var listing = browser.List(TestData.TenantA);
        Assert.False(listing.Complete);
        var retained = Assert.Single(listing.Entries); Assert.Equal(saved.Evidence.Id, retained.Id); Assert.True(retained.CanOpen);
        Assert.Contains("Listing incomplete:", listing.Detail); Assert.Contains("vanished during listing", listing.Detail);
        Assert.DoesNotContain("No report-evidence folder", listing.Detail);
        Assert.Equal(1, f.Handler.Calls); Assert.Equal(1, f.Tokens.Calls);
    }

    [Fact]
    public async Task Saved_report_browser_lists_strict_valid_and_damaged_records_as_separate_findings()
    {
        using var f = new ConnectedReportsFixture(); var saved = await f.Read();
        var folder = Path.GetDirectoryName(saved.File)!;
        File.WriteAllText(Path.Combine(folder, "unexpected.json"), "{}");
        var damaged = Guid.NewGuid().ToString(); File.WriteAllText(Path.Combine(folder, damaged + ".json"), "{}");
        var browser = new GraphReportEvidenceBrowser(f.Workspace.Evidence); var listing = browser.List(TestData.TenantA);
        Assert.True(listing.Complete); Assert.Equal(3, listing.Entries.Count);
        Assert.Single(listing.Entries, e => e.CanOpen);
        Assert.All(listing.Entries.Where(e => !e.CanOpen), e => Assert.NotEmpty(e.Problem));
        Assert.Equal(saved.Evidence.IntegrityDigest, browser.OpenStored(TestData.TenantA, saved.Evidence.Id).IntegrityDigest);
        Assert.Equal(1, f.Handler.Calls); Assert.Equal(1, f.Tokens.Calls);
    }

    [Fact]
    public void Missing_directory_is_distinct_from_invalid_or_inaccessible_listing()
    {
        using var f = new ConnectedReportsFixture(false); var browser = new GraphReportEvidenceBrowser(f.Workspace.Evidence);
        var enumerated = false;
        Func<string, IEnumerable<string>> unexpectedEnumeration = _ => { enumerated = true; throw new InvalidOperationException("Initial absence must not enumerate."); };
        typeof(GraphReportEvidenceBrowser).GetProperty("EnumerateEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(browser, unexpectedEnumeration);
        var missing = browser.List(TestData.TenantA); Assert.True(missing.Complete); Assert.Empty(missing.Entries); Assert.Contains("No report-evidence folder", missing.Detail);
        Assert.False(enumerated);
        var folder = Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence");
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!); File.WriteAllText(folder, "synthetic obstruction");
        var obstructed = browser.List(TestData.TenantA); Assert.False(obstructed.Complete); Assert.Contains("incomplete", obstructed.Detail);
        Assert.False(enumerated);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }

    [Fact]
    public void Entry_bound_stops_listing_and_shows_incomplete_reason()
    {
        using var f = new ConnectedReportsFixture(false);
        var folder = Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "report-evidence"); Directory.CreateDirectory(folder);
        for (var i = 0; i <= GraphReportEvidenceBrowser.MaximumEntries; i++) File.WriteAllText(Path.Combine(folder, "synthetic-" + i + ".json"), "{}");
        var listing = new GraphReportEvidenceBrowser(f.Workspace.Evidence).List(TestData.TenantA);
        Assert.False(listing.Complete); Assert.Equal(GraphReportEvidenceBrowser.MaximumEntries, listing.Entries.Count); Assert.Contains("200", listing.Detail);
    }

    [Theory]
    [InlineData("wrongTenant")]
    [InlineData("schema2")]
    [InlineData("exchange")]
    [InlineData("snapshot")]
    [InlineData("digest")]
    [InlineData("unknownMember")]
    [InlineData("duplicateMember")]
    public async Task Supplied_evidence_strictly_refuses_wrong_identity_kind_schema_and_integrity(string fault)
    {
        using var f = new ConnectedReportsFixture(); var saved = await f.Read();
        var text = ReportEvidenceSchema.Serialize(saved.Evidence); var json = JsonNode.Parse(text)!.AsObject();
        if (fault == "wrongTenant") json["tenantId"] = TestData.TenantB;
        if (fault == "schema2") json["schemaVersion"] = 2;
        if (fault == "exchange") json["resource"] = "Exchange";
        if (fault == "snapshot") json["kind"] = "tenantSnapshot";
        if (fault == "digest") json["integrityDigest"] = new string('a', 64);
        if (fault == "unknownMember") json["unregisteredFutureField"] = true;
        text = json.ToJsonString();
        if (fault == "duplicateMember") text = "{\"kind\":\"reportEvidence\"," + text[1..];
        var supplied = Path.Combine(f.Root.Root, "supplied.json"); File.WriteAllText(supplied, text);
        var original = File.ReadAllBytes(supplied);
        Assert.ThrowsAny<ToolkitException>(() => new GraphReportEvidenceBrowser(f.Workspace.Evidence).OpenSupplied(supplied, TestData.TenantA));
        Assert.Equal(original, File.ReadAllBytes(supplied)); Assert.Equal(1, f.Handler.Calls);
    }

    [Fact]
    public async Task Readers_refuse_invalid_utf8_oversize_record_identity_and_path_traversal()
    {
        using var f = new ConnectedReportsFixture(); var saved = await f.Read(); var browser = new GraphReportEvidenceBrowser(f.Workspace.Evidence);
        var supplied = Path.Combine(f.Root.Root, "supplied.json"); File.WriteAllBytes(supplied, [0xff, 0xfe, 0xfd]);
        Assert.Throws<DecoderFallbackException>(() => browser.OpenSupplied(supplied, TestData.TenantA));
        using (var stream = new FileStream(supplied, FileMode.Create)) stream.SetLength(GraphReportEvidenceBrowser.MaximumFileBytes + 1L);
        Assert.Throws<ConfigurationException>(() => browser.OpenSupplied(supplied, TestData.TenantA));
        Assert.Equal(ReportEvidenceSchema.MaximumBytes, GraphReportEvidenceBrowser.MaximumFileBytes);
        Assert.Throws<ConfigurationException>(() => browser.OpenStored(TestData.TenantA, "../supplied"));
        var different = Guid.NewGuid().ToString(); File.Copy(saved.File, Path.Combine(Path.GetDirectoryName(saved.File)!, different + ".json"));
        Assert.Throws<IntegrityException>(() => browser.OpenStored(TestData.TenantA, different));
        var storedOversize = Guid.NewGuid().ToString();
        using (var stream = new FileStream(Path.Combine(Path.GetDirectoryName(saved.File)!, storedOversize + ".json"), FileMode.Create)) stream.SetLength(ReportEvidenceSchema.MaximumBytes + 1L);
        Assert.Throws<ConfigurationException>(() => browser.OpenStored(TestData.TenantA, storedOversize));
        Assert.Contains(browser.List(TestData.TenantA).Entries, e => e.Id == storedOversize && !e.CanOpen);
    }

    [Fact]
    public async Task Page_failed_import_clears_prior_report_without_persisting_or_relabelling_it()
    {
        using var f = new ConnectedReportsFixture(); var saved = await f.Read(); var page = f.Page();
        page.OpenSupplied(saved.File); Assert.True(page.ExportJsonCommand.CanExecute(null));
        var supplied = Path.Combine(f.Root.Root, "invalid.json"); File.WriteAllText(supplied, "{}");
        Assert.ThrowsAny<ToolkitException>(() => page.OpenSupplied(supplied));
        Assert.False(page.ExportJsonCommand.CanExecute(null)); Assert.Contains("No report accepted", page.Outcome);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(saved.File)!)); Assert.Equal(1, f.Handler.Calls);
    }

    [Fact]
    public async Task Windows_directory_junctions_are_refused_in_browse_and_open_without_reading_target()
    {
        // Junctions need no administrator/developer-mode privilege, unlike Windows symbolic links.
        using var f = new ConnectedReportsFixture(); var saved = await f.Read();
        var target = Path.GetDirectoryName(saved.File)!;
        var junction = Path.Combine(f.Root.Root, "report-junction");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c mklink /J \"" + junction + "\" \"" + target + "\"", UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        })!;
        await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Throws<ConfigurationException>(() => new GraphReportEvidenceBrowser(f.Workspace.Evidence).OpenSupplied(Path.Combine(junction, saved.Evidence.Id + ".json"), TestData.TenantA));
            var tenantFolder = f.Root.Paths.TenantDirectory(TestData.TenantB); Directory.CreateDirectory(tenantFolder);
            var linkedListing = Path.Combine(tenantFolder, "report-evidence");
            using var second = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe", Arguments = "/c mklink /J \"" + linkedListing + "\" \"" + target + "\"", UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            })!;
            await second.WaitForExitAsync(); Assert.Equal(0, second.ExitCode);
            try
            {
                var listing = new GraphReportEvidenceBrowser(f.Workspace.Evidence).List(TestData.TenantB);
                Assert.False(listing.Complete); Assert.Contains("links and reparse", listing.Detail);
                Assert.Throws<ConfigurationException>(() => new GraphReportEvidenceBrowser(f.Workspace.Evidence).OpenStored(TestData.TenantB, saved.Evidence.Id));
            }
            finally { Directory.Delete(linkedListing); }
        }
        finally { Directory.Delete(junction); }
    }
}

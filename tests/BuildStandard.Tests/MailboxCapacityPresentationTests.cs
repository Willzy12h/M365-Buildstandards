using System.IO.Compression;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class MailboxCapacityPresentationTests
{
    private static string Fixture(string family) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../build/test-fixtures/mailbox-capacity-" + family + ".json"));
    private static ExchangeReportEvidence Mailboxes() => ExchangeReportEvidenceSchema.Read(File.ReadAllText(Fixture("mailboxes")), TestData.TenantA);
    private static ReportEvidence Licences() => ReportEvidenceSchema.Read(File.ReadAllText(Fixture("licences")), TestData.TenantA);
    [Fact]
    public void Derived_review_freezes_both_original_sources_and_cannot_be_imported_as_evidence()
    {
        var m = Mailboxes(); var l = Licences(); var beforeM = ExchangeReportEvidenceSchema.Serialize(m); var beforeL = ReportEvidenceSchema.Serialize(l);
        var d = MailboxCapacityReviewDocuments.Create(m, l, TestData.TenantA);
        Assert.Equal(beforeM, d.MailboxJson); Assert.Equal(beforeL, d.LicenceJson);
        m.Sections[0].Rows.Clear(); l.Sections[1].Rows.Clear();
        Assert.Equal(2, d.Review.Mailboxes.Count); Assert.True(d.Review.Mailboxes[0].Capacity.Configured100GB);
        Assert.Equal(MailboxCapacity.Eligible, d.Review.Mailboxes[0].Capacity.Eligibility);
        Assert.Equal("Unlimited", d.Review.Mailboxes[1].Capacity.SendReceiveQuota.State);
        Assert.Equal(MailboxCapacity.UnableToCheck, d.Review.Mailboxes[1].Capacity.Eligibility);
        Assert.ThrowsAny<ToolkitException>(() => ReportEnvelopeDispatcher.Read(MailboxCapacityReviewDocuments.Json(d), TestData.TenantA));
        Assert.ThrowsAny<ToolkitException>(() => ExchangeReportEvidenceSchema.Read(MailboxCapacityReviewDocuments.Json(d), TestData.TenantA));
    }
    [Theory]
    [InlineData(ExportFormat.Html)] [InlineData(ExportFormat.Json)] [InlineData(ExportFormat.Csv)] [InlineData(ExportFormat.Xlsx)]
    public void Every_export_keeps_all_rows_raw_values_source_status_times_digests_and_safe_text(ExportFormat format)
    {
        using var root = new TempRoot(); var m = Mailboxes(); var l = Licences();
        m.Sections[0].Rows[0]["name"] = "=HYPERLINK(<script>synthetic</script>)"; ExchangeReportEvidenceSchema.Seal(m);
        var beforeM = ExchangeReportEvidenceSchema.Serialize(m); var beforeL = ReportEvidenceSchema.Serialize(l);
        var d = MailboxCapacityReviewDocuments.Create(m, l, TestData.TenantA);
        var path = new ReportExporter(root.Paths, "Synthetic").ExportMailboxCapacity(d, format);
        string text;
        if (format is ExportFormat.Csv or ExportFormat.Xlsx)
        { using var archive = ZipFile.OpenRead(path); text = string.Join("\n", archive.Entries.Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); })); }
        else text = File.ReadAllText(path);
        foreach (var value in new[] { m.Id, l.Id, m.IntegrityDigest, l.IntegrityDigest, m.StartedAt, m.EndedAt, l.StartedAt, l.EndedAt, "Unlimited", "Collected", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "100 GB (107,374,182,400 bytes)", "314,572,800" })
            Assert.Contains(value, text);
        if (format == ExportFormat.Json)
        {
            var node = JsonNode.Parse(text)!; Assert.Equal("mailboxCapacityDerivedReview", node["kind"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(beforeM), node["originalMailboxSource"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(beforeL), node["originalLicenceSource"]));
        }
        if (format == ExportFormat.Html) { Assert.DoesNotContain("<script>", text); Assert.Contains("&lt;script&gt;", text); }
        if (format == ExportFormat.Csv) Assert.Contains("'=HYPERLINK", text);
        if (format == ExportFormat.Xlsx) Assert.DoesNotContain("<f>", text);
        Assert.Equal(beforeM, ExchangeReportEvidenceSchema.Serialize(m)); Assert.Equal(beforeL, ReportEvidenceSchema.Serialize(l));
        Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData(ReportReadState.Failed)] [InlineData(ReportReadState.Cancelled)] [InlineData(ReportReadState.NotAttempted)] [InlineData(ReportReadState.Partial)] [InlineData(ReportReadState.Collected)]
    public void Empty_read_failure_is_never_presented_as_checked_zero(string state)
    {
        var m = Mailboxes(); m.Status = state; m.Sections[0].Status = state; m.Sections[0].Rows.Clear();
        m.Sections[0].Error = state == ReportReadState.Collected ? null : "Synthetic incomplete or unavailable mailbox source.";
        ExchangeReportEvidenceSchema.Seal(m);
        var d = MailboxCapacityReviewDocuments.Create(m, Licences(), TestData.TenantA);
        Assert.Empty(d.Review.Mailboxes); var summary = MailboxCapacityReviewDocuments.Summary(d);
        Assert.Contains(state, summary);
        if (state == ReportReadState.Collected) Assert.Contains("Checked source returned zero", summary);
        else { Assert.Contains("not an empty successful check", summary); Assert.DoesNotContain("Checked source returned zero", summary); }
    }
    [Fact]
    public void Partial_quota_and_failed_licences_keep_unknown_separate_from_not_eligible()
    {
        var m = Mailboxes(); var l = Licences();
        var row = m.Sections[0].Rows[0]; row["quotaReadStatus"] = ReportReadState.Failed;
        foreach (var field in new[] { "issueWarningQuotaRaw", "prohibitSendQuotaRaw", "prohibitSendReceiveQuotaRaw" }) row[field] = null;
        row["readStatus"] = ReportReadState.Partial; row["error"] = "Synthetic quota failure.";
        m.Status = m.Sections[0].Status = ReportReadState.Partial; m.Sections[0].Error = "Incomplete quota reads."; ExchangeReportEvidenceSchema.Seal(m);
        l.Status = ReportReadState.Failed; foreach (var section in l.Sections) { section.Status = ReportReadState.Failed; section.Error = "Synthetic licences unavailable."; section.Rows.Clear(); } ReportEvidenceSchema.Seal(l);
        var d = MailboxCapacityReviewDocuments.Create(m, l, TestData.TenantA);
        Assert.Null(d.Review.Mailboxes[0].Capacity.Configured100GB); Assert.Equal(MailboxCapacity.UnableToCheck, d.Review.Mailboxes[0].Capacity.Eligibility);
        Assert.Contains("Failed", MailboxCapacityReviewDocuments.Summary(d)); Assert.Contains("Synthetic licences unavailable", MailboxCapacityReviewDocuments.Html(d));
        l = Licences(); var plan = l.Sections[1].Rows[0]["products"]![0]!["servicePlans"]![0]!;
        plan["servicePlanId"] = MailboxCapacity.Plan1Id; plan["servicePlanName"] = "EXCHANGE_S_STANDARD"; ReportEvidenceSchema.Seal(l);
        var notEligible = MailboxCapacityReviewDocuments.Create(Mailboxes(), l, TestData.TenantA);
        Assert.Equal("Configured100GBButNotEligible", notEligible.Review.Mailboxes[0].Capacity.Finding);
    }
    [Fact]
    public void Both_sources_require_the_selected_tenant_and_wrong_family_or_tamper_never_creates_output()
    {
        using var root = new TempRoot(); var reader = new MailboxCapacityEvidenceReader(new EvidenceStore(root.Paths, NullLog.Instance));
        Assert.Throws<TenantMismatchException>(() => MailboxCapacityReviewDocuments.Create(Mailboxes(), Licences(), TestData.TenantB));
        var file = Path.Combine(root.Root, "supplied.json"); File.WriteAllText(file, File.ReadAllText(Fixture("mailboxes")));
        Assert.ThrowsAny<ToolkitException>(() => reader.OpenLicences(file, TestData.TenantA));
        Assert.Throws<TenantMismatchException>(() => reader.OpenMailboxes(file, TestData.TenantB));
        File.WriteAllText(file, File.ReadAllText(file).Replace("Synthetic user", "Changed without resealing", StringComparison.Ordinal));
        Assert.ThrowsAny<ToolkitException>(() => reader.OpenMailboxes(file, TestData.TenantA));
        File.WriteAllText(file, File.ReadAllText(Fixture("licences"))); Assert.ThrowsAny<ToolkitException>(() => reader.OpenMailboxes(file, TestData.TenantA));
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
    [Fact]
    public void Bounded_reader_refuses_invalid_utf8_oversize_wrong_filename_and_traversal()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance); var reader = new MailboxCapacityEvidenceReader(store);
        var file = Path.Combine(root.Root, "supplied.json"); File.WriteAllBytes(file, [0xff,0xfe]);
        Assert.Throws<ConfigurationException>(() => reader.OpenMailboxes(file, TestData.TenantA));
        using (var stream = new FileStream(file, FileMode.Create)) stream.SetLength(ExchangeReportEvidenceSchema.MaximumBytes + 1L);
        Assert.Throws<ConfigurationException>(() => reader.OpenMailboxes(file, TestData.TenantA));
        Assert.Throws<ConfigurationException>(() => reader.OpenStoredMailboxes(TestData.TenantA, "../supplied"));
        var path = store.SaveExchangeReport(Mailboxes()); var other = Guid.NewGuid().ToString("D");
        File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, other + ".json"));
        Assert.Throws<IntegrityException>(() => reader.OpenStoredMailboxes(TestData.TenantA, other));
        var listing = reader.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes);
        Assert.True(listing.Complete); Assert.Single(listing.Entries, e => e.CanOpen); Assert.Single(listing.Entries, e => !e.CanOpen && e.Problem.Length > 0);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Listing_failure_after_admission_retains_rows_and_is_incomplete(bool missingDirectory)
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance); var path = store.SaveExchangeReport(Mailboxes());
        IEnumerable<string> Failed(string _) { yield return path; if (missingDirectory) throw new DirectoryNotFoundException("Synthetic source vanished."); throw new UnauthorizedAccessException("Synthetic access denied."); }
        var reader = new MailboxCapacityEvidenceReader(store) { EnumerateEntries = Failed };
        var listing = reader.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes);
        Assert.False(listing.Complete); Assert.Single(listing.Entries); Assert.Contains("incomplete", listing.Detail);
    }
    [Fact]
    public void Initial_missing_folder_and_obstructed_folder_have_distinct_meaning()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance); var reader = new MailboxCapacityEvidenceReader(store);
        var empty = reader.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes); Assert.True(empty.Complete); Assert.Empty(empty.Entries);
        var folder = Path.Combine(store.TenantDirectory(TestData.TenantA), EvidenceStore.ExchangeReportEvidenceFolder); Directory.CreateDirectory(Path.GetDirectoryName(folder)!); File.WriteAllText(folder, "Synthetic obstruction");
        var bad = reader.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes); Assert.False(bad.Complete); Assert.Contains("not a directory", bad.Detail);
    }
    [Fact]
    public void Entry_and_inspection_budgets_report_incomplete_instead_of_empty_success()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var reader = new MailboxCapacityEvidenceReader(store); var folder = Path.Combine(store.TenantDirectory(TestData.TenantA), EvidenceStore.ExchangeReportEvidenceFolder);
        Directory.CreateDirectory(folder);
        for (var i = 0; i <= MailboxCapacityEvidenceReader.MaximumEntries; i++) File.WriteAllText(Path.Combine(folder, "unexpected-" + i + ".json"), "{}");
        var capped = reader.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes);
        Assert.False(capped.Complete); Assert.Equal(200, capped.Entries.Count); Assert.Contains("200 entries", capped.Detail);
        var files = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var report = Mailboxes(); report.Id = Guid.NewGuid().ToString("D"); ExchangeReportEvidenceSchema.Seal(report);
            var file = store.SaveExchangeReport(report); files.Add(file);
            using var stream = new FileStream(file, FileMode.Append); var padding = Enumerable.Repeat((byte)' ', 64 * 1024).ToArray();
            const long target = 24L * 1024 * 1024;
            while (stream.Length < target) stream.Write(padding, 0, (int)Math.Min(padding.Length, target - stream.Length));
        }
        var bounded = new MailboxCapacityEvidenceReader(store) { EnumerateEntries = _ => files }.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes);
        Assert.False(bounded.Complete); Assert.Equal(2, bounded.Entries.Count); Assert.All(bounded.Entries, e => Assert.True(e.CanOpen)); Assert.Contains("64 MiB", bounded.Detail);
    }
    [Fact]
    public async Task Both_readers_refuse_linked_parents_and_listing_refuses_linked_source_folder()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance); var reader = new MailboxCapacityEvidenceReader(store);
        var real = Path.Combine(root.Root, "real-input"); Directory.CreateDirectory(real);
        File.Copy(Fixture("mailboxes"), Path.Combine(real, "mailbox.json")); File.Copy(Fixture("licences"), Path.Combine(real, "licence.json"));
        var link = Path.Combine(store.TenantDirectory(TestData.TenantA), EvidenceStore.ExchangeReportEvidenceFolder); Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (OperatingSystem.IsWindows())
        {
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, real }) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        else Directory.CreateSymbolicLink(link, real);
        Assert.Throws<ConfigurationException>(() => reader.OpenMailboxes(Path.Combine(link, "mailbox.json"), TestData.TenantA));
        Assert.Throws<ConfigurationException>(() => reader.OpenLicences(Path.Combine(link, "licence.json"), TestData.TenantA));
        var listing = reader.List(TestData.TenantA, MailboxCapacityEvidenceReader.Family.Mailboxes);
        Assert.False(listing.Complete); Assert.Contains("links", listing.Detail); Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }

    [Fact]
    public void Native_partial_archive_fixture_retains_unknown_read_and_unlimited_primary_values()
    {
        var m = Mailboxes(); var row = m.Sections[0].Rows[1];
        row["archiveReadStatus"] = ReportReadState.Failed; row["hasArchive"] = null;
        row["readStatus"] = ReportReadState.Partial; row["error"] = "Synthetic archive existence could not be checked.";
        m.Status = m.Sections[0].Status = ReportReadState.Partial; m.Sections[0].Error = "Synthetic partial inventory; archive state stays unknown.";
        ExchangeReportEvidenceSchema.Seal(m);
        var d = MailboxCapacityReviewDocuments.Create(m, Licences(), TestData.TenantA);
        Assert.Contains("Partial", MailboxCapacityReviewDocuments.Summary(d));
        Assert.Null(d.Review.Mailboxes[1].HasArchive); Assert.Equal(ReportReadState.Failed, d.Review.Mailboxes[1].Capacity.Observed.ArchiveReadStatus);
        Assert.Equal("Unlimited", d.Review.Mailboxes[1].Capacity.SendReceiveQuota.Raw); Assert.Equal(MailboxCapacity.UnableToCheck, d.Review.Mailboxes[1].Capacity.Eligibility);
    }

}

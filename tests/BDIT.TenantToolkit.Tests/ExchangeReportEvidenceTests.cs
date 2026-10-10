using System.IO.Compression;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// INT-088 slice 1: the strict <c>exchangeReportEvidence</c> reader and its store. Every record here is synthetic; no
/// module, tenant or account is touched. The reader is the gate the runner (slice 2) and import must pass through, so these
/// tests pin what it refuses: anything outside the registration, any value that hides a failed read, and any record that
/// claims a better state than its sections.
/// </summary>
public sealed class ExchangeReportEvidenceTests
{
    private const string UserMailbox = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string SharedMailbox = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private const string UserObject = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";

    private static MailboxReportRow User(bool includeArchive = true) => new()
    {
        Id = UserMailbox, Name = "Synthetic user", ReadStatus = ReportReadState.Collected, Error = null,
        ExternalDirectoryObjectId = UserObject, PrimarySmtpAddress = "user@synthetic.example", MailboxType = "UserMailbox",
        PrimarySizeReadStatus = ReportReadState.Collected, PrimarySizeRaw = "1.2 GB (1,288,490,189 bytes)",
        QuotaReadStatus = ReportReadState.Collected, IssueWarningQuotaRaw = "49 GB (52,613,349,376 bytes)",
        ProhibitSendQuotaRaw = "49.5 GB (53,150,220,288 bytes)", ProhibitSendReceiveQuotaRaw = "50 GB (53,687,091,200 bytes)",
        ArchiveReadStatus = includeArchive ? ReportReadState.Collected : ReportReadState.NotAttempted,
        HasArchive = includeArchive ? true : null,
        ArchiveSizeReadStatus = includeArchive ? ReportReadState.Collected : ReportReadState.NotAttempted,
        ArchiveSizeRaw = includeArchive ? "300 MB (314,572,800 bytes)" : null,
        ArchiveQuotaRaw = includeArchive ? "100 GB (107,374,182,400 bytes)" : null
    };

    /// <summary>A shared mailbox has no joinable user and no licence, but is still a mailbox with exact identity.</summary>
    private static MailboxReportRow Shared(bool includeArchive = true) => new()
    {
        Id = SharedMailbox, Name = "Synthetic shared", ReadStatus = ReportReadState.Collected, Error = null,
        ExternalDirectoryObjectId = null, PrimarySmtpAddress = "shared@synthetic.example", MailboxType = "SharedMailbox",
        PrimarySizeReadStatus = ReportReadState.Collected, PrimarySizeRaw = "0 B (0 bytes)",
        QuotaReadStatus = ReportReadState.Collected, IssueWarningQuotaRaw = "Unlimited", ProhibitSendQuotaRaw = "Unlimited",
        ProhibitSendReceiveQuotaRaw = "Unlimited",
        ArchiveReadStatus = includeArchive ? ReportReadState.Collected : ReportReadState.NotAttempted,
        HasArchive = includeArchive ? false : null,
        ArchiveSizeReadStatus = ReportReadState.NotAttempted, ArchiveSizeRaw = null, ArchiveQuotaRaw = null
    };

    private static ExchangeReportEvidence Report(bool includeArchive = true, params MailboxReportRow[] rows)
    {
        var definition = ExchangeReportRegistry.Find("exo-mailbox-inventory");
        if (rows.Length == 0) rows = [User(includeArchive), Shared(includeArchive)];
        var status = rows.All(r => r.ReadStatus == ReportReadState.Collected) ? ReportReadState.Collected : ReportReadState.Partial;
        return new ExchangeReportEvidence
        {
            Id = Guid.NewGuid().ToString("D"), RunId = Guid.NewGuid().ToString("D"), ReportId = definition.Id,
            TenantId = TestData.TenantA, InitiatingAccountObjectId = TestData.Operator, ObservedAccountUpn = "operator@synthetic.example",
            Resource = definition.Resource, SourceMode = "live", StartedAt = "2026-10-10T01:00:00Z", EndedAt = "2026-10-10T01:05:00Z",
            ToolkitVersion = "0.0.0-test", AdapterVersion = ExchangeReportRegistry.AdapterVersion, ModuleVersion = "3.9.0",
            RuntimeVersion = "7.4.6", ScriptSha256 = new string('a', 64), ManifestSha256 = new string('b', 64),
            RunnerTemplateSha256 = new string('c', 64),
            Parameters = [new() { Name = "IncludeArchive", Type = ExchangeReportParameterType.Boolean, Value = includeArchive ? "true" : "false" }],
            SourceCommands = [.. definition.SourceCommands], Limitations = [definition.Limitations],
            Sections =
            [
                new ReportSection
                {
                    Id = "mailboxes", Status = status, Limitations = [],
                    Error = status == ReportReadState.Collected ? null : "Some mailbox values could not be read.",
                    Rows = [.. rows.Select(ExchangeReportEvidenceSchema.Row)]
                }
            ],
            Status = status
        };
    }

    private static ExchangeReportEvidence Sealed(ExchangeReportEvidence report) { ExchangeReportEvidenceSchema.Seal(report); return report; }

    /// <summary>Changes a sealed record, then re-seals it so only the rule under test can refuse it, not the digest.</summary>
    private static void RefusedAfter(Action<ExchangeReportEvidence> change, bool includeArchive = true)
    {
        var report = Report(includeArchive);
        change(report);
        report.IntegrityDigest = EvidenceIntegrity.Compute(report);
        Assert.ThrowsAny<ToolkitException>(() => ExchangeReportEvidenceSchema.Read(ExchangeReportEvidenceSchema.Serialize(report), TestData.TenantA));
    }

    private static void RowRefused(MailboxReportRow row, bool includeArchive = true) =>
        RefusedAfter(r => r.Sections[0].Rows = [ExchangeReportEvidenceSchema.Row(row)], includeArchive);

    private static ExchangeReportEvidence RoundTrip(ExchangeReportEvidence report) =>
        ExchangeReportEvidenceSchema.Read(ExchangeReportEvidenceSchema.Serialize(Sealed(report)), TestData.TenantA);

    [Fact]
    public void A_valid_live_record_round_trips_through_the_reader_and_the_store()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var report = Sealed(Report());

        var read = ExchangeReportEvidenceSchema.Read(ExchangeReportEvidenceSchema.Serialize(report), TestData.TenantA);
        Assert.Equal(report.IntegrityDigest, read.IntegrityDigest);
        var file = store.SaveExchangeReport(report);
        Assert.Equal(EvidenceStore.ExchangeReportEvidenceFolder, Path.GetFileName(Path.GetDirectoryName(file)));
        var loaded = Assert.IsType<ExchangeReportEvidence>(store.LoadExchangeReport(TestData.TenantA, report.Id));
        Assert.Equal(report.Id, loaded.Id);
        Assert.Equal(ReportReadState.Collected, loaded.Status);
        // Unlimited stays Unlimited and the shared mailbox keeps its exact identity without an Entra object.
        var shared = loaded.Sections[0].Rows.Single(r => r["id"]!.GetValue<string>() == SharedMailbox);
        Assert.Equal("Unlimited", shared["prohibitSendQuotaRaw"]!.GetValue<string>());
        Assert.Null(shared["externalDirectoryObjectId"]);
        Assert.True(shared.ContainsKey("externalDirectoryObjectId"));
        Assert.Null(store.LoadExchangeReport(TestData.TenantA, Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public void The_store_never_replaces_a_record_and_refuses_a_file_holding_another_id()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var report = Sealed(Report());
        var file = store.SaveExchangeReport(report);
        var original = File.ReadAllText(file);

        var replacement = Report(); replacement.Id = report.Id; Sealed(replacement);
        Assert.ThrowsAny<Exception>(() => store.SaveExchangeReport(replacement));
        Assert.Equal(original, File.ReadAllText(file));

        var other = Sealed(Report());
        var moved = Path.Combine(Path.GetDirectoryName(file)!, other.Id + ".json");
        File.WriteAllText(moved, original);
        Assert.Throws<IntegrityException>(() => store.LoadExchangeReport(TestData.TenantA, other.Id));
        Assert.Throws<ConfigurationException>(() => store.LoadExchangeReport(TestData.TenantA, "not-a-guid"));
    }

    [Fact]
    public void A_record_is_refused_for_another_tenant()
    {
        var json = ExchangeReportEvidenceSchema.Serialize(Sealed(Report()));
        Assert.Throws<TenantMismatchException>(() => ExchangeReportEvidenceSchema.Read(json, TestData.TenantB));

        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var report = Sealed(Report());
        var file = store.SaveExchangeReport(report);
        var otherTenant = Path.Combine(store.TenantDirectory(TestData.TenantB), EvidenceStore.ExchangeReportEvidenceFolder);
        Directory.CreateDirectory(otherTenant);
        File.Copy(file, Path.Combine(otherTenant, report.Id + ".json"));
        Assert.Throws<TenantMismatchException>(() => store.LoadExchangeReport(TestData.TenantB, report.Id));
    }

    /// <summary>Graph reports and Exchange reports are separate kinds; neither reader accepts the other's records.</summary>
    [Fact]
    public void Neither_report_reader_accepts_the_other_kind()
    {
        var exchange = ExchangeReportEvidenceSchema.Serialize(Sealed(Report()));
        Assert.ThrowsAny<ToolkitException>(() => ReportEvidenceSchema.Read(exchange, TestData.TenantA));

        var graph = new ReportEvidence
        {
            Id = Guid.NewGuid().ToString("D"), ReportId = "intune-devices", TenantId = TestData.TenantA, AccountObjectId = TestData.Operator,
            StartedAt = "2026-10-10T01:00:00Z", EndedAt = "2026-10-10T01:05:00Z", ToolkitVersion = "0.0.0-test", ModuleVersion = "test"
        };
        Assert.ThrowsAny<ToolkitException>(() => ExchangeReportEvidenceSchema.Read(ReportEvidenceSchema.Serialize(graph), TestData.TenantA));
    }

    [Fact]
    public void Anything_outside_the_registration_is_refused()
    {
        RefusedAfter(r => r.ReportId = "exo-unregistered");
        RefusedAfter(r => r.Resource = "Purview");
        RefusedAfter(r => r.Resource = "exchangeonline");
        RefusedAfter(r => r.AdapterVersion = "registered-exchange-report/2");
        RefusedAfter(r => r.SourceCommands = ["Get-EXOMailbox"]);
        RefusedAfter(r => r.SourceCommands = ["Get-EXOMailboxStatistics", "Get-EXOMailbox"]);
        RefusedAfter(r => r.SourceCommands.Add("Set-Mailbox"));
        RefusedAfter(r => r.SourceCommands = ["get-exomailbox", "Get-EXOMailboxStatistics"]);
        RefusedAfter(r => r.Parameters.Add(new() { Name = "ResultSize", Type = ExchangeReportParameterType.Integer, Value = "10" }));
        RefusedAfter(r => r.Parameters.Clear());
        RefusedAfter(r => r.Parameters[0].Type = ExchangeReportParameterType.Text);
        RefusedAfter(r => r.Parameters[0].Name = "includeArchive");
        RefusedAfter(r => r.Sections.Add(new ReportSection { Id = "extra", Status = ReportReadState.Collected, Limitations = [], Rows = [] }));
        RefusedAfter(r => r.Sections[0].Id = "Mailboxes");
        RefusedAfter(r => r.Kind = "reportEvidence");
        RefusedAfter(r => r.SchemaVersion = 2);
        RefusedAfter(r => r.ReportSchemaVersion = 2);
        RefusedAfter(r => r.SourceMode = "Live");
    }

    /// <summary>
    /// Run without archives so that no archive rule can refuse the record first: a non-normalised value reads as "not
    /// true", which that record already satisfies, so only the parameter rule is left to refuse it.
    /// </summary>
    [Theory]
    [InlineData("True")]
    [InlineData("False")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData(" false")]
    [InlineData("")]
    public void Parameters_must_be_in_their_single_normalised_form(string value) =>
        RefusedAfter(r => r.Parameters[0].Value = value, includeArchive: false);

    [Theory]
    [InlineData(ExchangeReportParameterType.Integer, "42", true)]
    [InlineData(ExchangeReportParameterType.Integer, "-7", true)]
    [InlineData(ExchangeReportParameterType.Integer, "042", false)]
    [InlineData(ExchangeReportParameterType.Integer, "+7", false)]
    [InlineData(ExchangeReportParameterType.Integer, "1,000", false)]
    [InlineData(ExchangeReportParameterType.Date, "2026-10-10", true)]
    [InlineData(ExchangeReportParameterType.Date, "2026-10-10T00:00:00Z", false)]
    [InlineData(ExchangeReportParameterType.Date, "10/10/2026", false)]
    [InlineData(ExchangeReportParameterType.Text, "Synthetic text", true)]
    [InlineData(ExchangeReportParameterType.Text, "line\nbreak", false)]
    [InlineData("Script", "Get-Mailbox", false)]
    public void Each_parameter_type_has_one_normalised_text_form(string type, string value, bool expected) =>
        Assert.Equal(expected, ExchangeReportEvidenceSchema.IsNormalised(type, value));

    [Fact]
    public void Provenance_must_be_complete_and_exact()
    {
        RefusedAfter(r => r.Id = r.Id.ToUpperInvariant());
        RefusedAfter(r => r.RunId = "");
        RefusedAfter(r => r.ScriptSha256 = new string('A', 64));
        RefusedAfter(r => r.ManifestSha256 = new string('b', 63));
        RefusedAfter(r => r.RunnerTemplateSha256 = "");
        RefusedAfter(r => r.ModuleVersion = " ");
        RefusedAfter(r => r.RuntimeVersion = "7.4\n6");
        RefusedAfter(r => r.EndedAt = "2026-10-10T00:59:59Z");
        RefusedAfter(r => r.StartedAt = "2026-10-10T01:00:00+01:00");
    }

    /// <summary>A live run records who the toolkit verified and who the module observed; history keeps only its own claims.</summary>
    [Fact]
    public void Live_runs_need_both_accounts_and_historical_imports_may_lack_them()
    {
        RefusedAfter(r => r.InitiatingAccountObjectId = null);
        RefusedAfter(r => r.ObservedAccountUpn = null);
        RefusedAfter(r => r.ObservedAccountUpn = "operator");
        RefusedAfter(r => r.ObservedAccountUpn = "operator @synthetic.example");
        RefusedAfter(r => r.InitiatingAccountObjectId = "operator@synthetic.example");

        var historical = Report(); historical.SourceMode = "historical";
        historical.InitiatingAccountObjectId = null; historical.ObservedAccountUpn = null;
        Assert.Equal("historical", RoundTrip(historical).SourceMode);
        RefusedAfter(r => { r.SourceMode = "historical"; r.ObservedAccountUpn = "not-an-account"; });
    }

    [Fact]
    public void Raw_json_is_read_strictly()
    {
        var json = ExchangeReportEvidenceSchema.Serialize(Sealed(Report()));

        var unknown = JsonNode.Parse(json)!.AsObject(); unknown["script"] = "Get-Mailbox | Remove-Mailbox";
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Read(unknown.ToJsonString(), TestData.TenantA));

        var rawRow = JsonNode.Parse(json)!.AsObject();
        rawRow["sections"]![0]!["rows"]![0]!["totalItemSize"] = "1.2 GB";
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Read(rawRow.ToJsonString(), TestData.TenantA));

        var missing = JsonNode.Parse(json)!.AsObject(); missing.Remove("observedAccountUpn");
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Read(missing.ToJsonString(), TestData.TenantA));

        var duplicate = "{\"status\":\"Failed\"," + json[1..];
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Read(duplicate, TestData.TenantA));

        var tampered = JsonNode.Parse(json)!.AsObject();
        tampered["sections"]![0]!["rows"]![1]!["prohibitSendQuotaRaw"] = "50 GB (53,687,091,200 bytes)";
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Read(tampered.ToJsonString(), TestData.TenantA));

        var oversize = json[..^1] + new string(' ', ExchangeReportEvidenceSchema.MaximumBytes) + "}";
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Read(oversize, TestData.TenantA));
    }

    [Fact]
    public void A_record_cannot_claim_a_better_state_than_its_sections()
    {
        RefusedAfter(r => r.Status = ReportReadState.Partial);
        RefusedAfter(r => { r.Sections[0].Status = ReportReadState.Partial; r.Sections[0].Error = "Some values could not be read."; });
        RefusedAfter(r => { r.Sections[0].Status = ReportReadState.Failed; r.Sections[0].Error = "Denied."; r.Status = ReportReadState.Failed; });
        RefusedAfter(r => r.Sections[0].Error = "A hidden error.");
        RefusedAfter(r => { r.Sections[0].Status = ReportReadState.Partial; r.Status = ReportReadState.Partial; });

        // A failed section is recorded honestly, with its reason and no rows.
        var failed = Report(); failed.Sections[0].Rows = []; failed.Sections[0].Status = ReportReadState.Failed;
        failed.Sections[0].Error = "The module refused the read."; failed.Status = ReportReadState.Failed;
        Assert.Equal(ReportReadState.Failed, RoundTrip(failed).Status);
        // A genuine empty read is collected.
        var empty = Report(); empty.Sections[0].Rows = [];
        Assert.Equal(ReportReadState.Collected, RoundTrip(empty).Status);
    }

    [Fact]
    public void Rows_keep_exact_unique_identity_and_a_reason_when_incomplete()
    {
        var upper = User(); upper.Id = UserMailbox.ToUpperInvariant(); RowRefused(upper);
        var braces = User(); braces.Id = "{" + UserMailbox + "}"; RowRefused(braces);
        var noId = User(); noId.Id = null; RowRefused(noId);
        var hidden = User(); hidden.Error = "Hidden."; RowRefused(hidden);
        var badObject = User(); badObject.ExternalDirectoryObjectId = "user@synthetic.example"; RowRefused(badObject);
        RefusedAfter(r => r.Sections[0].Rows = [ExchangeReportEvidenceSchema.Row(User()), ExchangeReportEvidenceSchema.Row(User())]);

        var noReason = User(); noReason.ReadStatus = ReportReadState.Partial;
        RefusedAfter(r =>
        {
            r.Sections[0].Rows = [ExchangeReportEvidenceSchema.Row(noReason)];
            r.Sections[0].Status = ReportReadState.Partial; r.Sections[0].Error = "Some values could not be read."; r.Status = ReportReadState.Partial;
        });
        // A partial row cannot sit inside a section that claims success.
        var partial = User(); partial.ReadStatus = ReportReadState.Partial; partial.Error = "Quota read failed.";
        RowRefused(partial);
    }

    /// <summary>A failed read must never look like zero or like a known quota.</summary>
    [Fact]
    public void A_value_group_that_was_not_read_records_no_values_and_a_read_group_records_them_all()
    {
        var failedQuota = User(); failedQuota.QuotaReadStatus = ReportReadState.Failed; failedQuota.ReadStatus = ReportReadState.Partial;
        failedQuota.Error = "Quota read failed.";
        RefusedAfter(r =>
        {
            r.Sections[0].Rows = [ExchangeReportEvidenceSchema.Row(failedQuota)];
            r.Sections[0].Status = ReportReadState.Partial; r.Sections[0].Error = "Some values could not be read."; r.Status = ReportReadState.Partial;
        });

        var missingValue = User(); missingValue.ProhibitSendQuotaRaw = null; RowRefused(missingValue);
        var zeroSize = User(); zeroSize.PrimarySizeReadStatus = ReportReadState.Failed; RowRefused(zeroSize);
        var noType = User(); noType.MailboxType = null; RowRefused(noType);

        // The honest partial row: quotas unknown, with the reason, inside a partial section.
        var honest = User(); honest.QuotaReadStatus = ReportReadState.Failed; honest.IssueWarningQuotaRaw = null;
        honest.ProhibitSendQuotaRaw = null; honest.ProhibitSendReceiveQuotaRaw = null;
        honest.ReadStatus = ReportReadState.Partial; honest.Error = "Quota read failed.";
        var report = RoundTrip(Report(true, honest, Shared()));
        Assert.Equal(ReportReadState.Partial, report.Status);
        var row = report.Sections[0].Rows.Single(r => r["id"]!.GetValue<string>() == UserMailbox);
        Assert.Null(row["prohibitSendQuotaRaw"]);
        Assert.Equal(ReportReadState.Failed, row["quotaReadStatus"]!.GetValue<string>());
    }

    [Fact]
    public void Archive_absence_and_archive_values_come_only_from_a_successful_archive_read()
    {
        var guessed = Shared(); guessed.ArchiveReadStatus = ReportReadState.Failed; guessed.ReadStatus = ReportReadState.Partial;
        guessed.Error = "Archive state unknown.";
        RefusedAfter(r =>
        {
            r.Sections[0].Rows = [ExchangeReportEvidenceSchema.Row(guessed)];
            r.Sections[0].Status = ReportReadState.Partial; r.Sections[0].Error = "Some values could not be read."; r.Status = ReportReadState.Partial;
        });

        var unknown = User(); unknown.HasArchive = null; RowRefused(unknown);
        var sizeWithoutArchive = Shared(); sizeWithoutArchive.ArchiveSizeReadStatus = ReportReadState.Collected;
        sizeWithoutArchive.ArchiveSizeRaw = "0 B (0 bytes)"; RowRefused(sizeWithoutArchive);
        var quotaWithoutArchive = Shared(); quotaWithoutArchive.ArchiveQuotaRaw = "100 GB (107,374,182,400 bytes)"; RowRefused(quotaWithoutArchive);
        var noArchiveSize = User(); noArchiveSize.ArchiveSizeReadStatus = ReportReadState.NotAttempted; noArchiveSize.ArchiveSizeRaw = null;
        RowRefused(noArchiveSize);

        // A run without archives records no archive state at all.
        RowRefused(User(includeArchive: true), includeArchive: false);
        RowRefused(Shared(includeArchive: true), includeArchive: false);
        Assert.Equal(ReportReadState.Collected, RoundTrip(Report(includeArchive: false)).Status);
    }

    [Fact]
    public void Values_are_bounded_and_free_of_control_characters()
    {
        var longName = User(); longName.Name = new string('n', 257); RowRefused(longName);
        var control = User(); control.PrimarySizeRaw = "1 GB\u0007"; RowRefused(control);
        RefusedAfter(r => r.Limitations = [.. Enumerable.Repeat("x", 65)]);
    }

    /// <summary>
    /// AST-20261010-01: a blank value is unreadable, not an observed zero, a known quota or a known type, so a row that
    /// records one cannot be sealed. Readable raw values, including zero and Unlimited, are kept exactly as returned.
    /// </summary>
    [Theory]
    [InlineData("mailboxType", "")]
    [InlineData("mailboxType", "   ")]
    [InlineData("primarySizeRaw", "")]
    [InlineData("primarySizeRaw", "   ")]
    [InlineData("issueWarningQuotaRaw", "")]
    [InlineData("prohibitSendQuotaRaw", "   ")]
    [InlineData("prohibitSendReceiveQuotaRaw", "")]
    [InlineData("prohibitSendReceiveQuotaRaw", "   ")]
    [InlineData("archiveSizeRaw", "")]
    [InlineData("archiveSizeRaw", "   ")]
    [InlineData("archiveQuotaRaw", "")]
    [InlineData("primarySmtpAddress", "   ")]
    public void A_blank_value_is_unreadable_and_cannot_be_recorded(string property, string value) =>
        RefusedAfter(r => r.Sections[0].Rows[0][property] = value);

    [Fact]
    public void A_blank_value_cannot_be_recorded_in_a_partial_row_either()
    {
        var partial = User(); partial.QuotaReadStatus = ReportReadState.Failed; partial.IssueWarningQuotaRaw = null;
        partial.ProhibitSendQuotaRaw = null; partial.ProhibitSendReceiveQuotaRaw = null;
        partial.ReadStatus = ReportReadState.Partial; partial.Error = "Quota read failed."; partial.PrimarySizeRaw = " ";
        RefusedAfter(r =>
        {
            r.Sections[0].Rows = [ExchangeReportEvidenceSchema.Row(partial)];
            r.Sections[0].Status = ReportReadState.Partial; r.Sections[0].Error = "Some values could not be read."; r.Status = ReportReadState.Partial;
        });
    }

    [Fact]
    public void Readable_zero_and_unlimited_values_are_kept_exactly_as_returned()
    {
        var zero = User(); zero.PrimarySizeRaw = "0"; zero.ArchiveSizeRaw = "0 B (0 bytes)"; zero.IssueWarningQuotaRaw = "Unlimited";
        zero.ArchiveQuotaRaw = "Unlimited";
        var report = RoundTrip(Report(true, zero, Shared()));
        Assert.Equal(ReportReadState.Collected, report.Status);
        var row = report.Sections[0].Rows.Single(r => r["id"]!.GetValue<string>() == UserMailbox);
        Assert.Equal("0", row["primarySizeRaw"]!.GetValue<string>());
        Assert.Equal("0 B (0 bytes)", row["archiveSizeRaw"]!.GetValue<string>());
        Assert.Equal("Unlimited", row["issueWarningQuotaRaw"]!.GetValue<string>());
        Assert.Equal("Unlimited", row["archiveQuotaRaw"]!.GetValue<string>());
        Assert.Equal("UserMailbox", row["mailboxType"]!.GetValue<string>());
    }

    /// <summary>The support bundle is allowlisted metadata; Exchange report rows name mailboxes and must never reach it.</summary>
    [Fact]
    public void The_support_bundle_never_carries_exchange_report_evidence()
    {
        using var root = new TempRoot();
        var report = Sealed(Report());
        new EvidenceStore(root.Paths, NullLog.Instance).SaveExchangeReport(report);

        var zip = SupportBundle.Export(root.Paths, TestData.Standard());
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            Assert.DoesNotContain(EvidenceStore.ExchangeReportEvidenceFolder, entry.FullName, StringComparison.OrdinalIgnoreCase);
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            foreach (var value in new[] { report.Id, UserMailbox, SharedMailbox, "user@synthetic.example", "operator@synthetic.example" })
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}

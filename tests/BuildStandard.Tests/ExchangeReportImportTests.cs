using System.Text;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ExchangeReportImportTests
{
    [Fact]
    public void Editing_a_returned_projection_cannot_change_the_original_or_later_exports()
    {
        using var root = new TempRoot();
        var report = InboxRuleEvidenceV2Tests.Read(InboxRuleEvidenceV2Tests.Report());
        var file = Path.Combine(root.Root, "synthetic.json");
        File.WriteAllText(file, ExchangeReportEvidenceSchema.Serialize(report));
        var admitted = ExchangeReportImport.ReadFile(file, TestData.TenantA);
        var projection = admitted.ReadEvidence();
        projection.Sections[1].Rows.Clear();
        projection.TenantId = TestData.TenantB;
        Assert.Equal(TestData.TenantA, admitted.ReadEvidence().TenantId);
        Assert.Single(admitted.ReadEvidence().Sections[1].Rows);
    }

    [Fact]
    public void Oversized_file_is_refused_before_loading_its_bytes()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Root, "oversized-synthetic.json");
        using (var stream = File.Create(file)) stream.SetLength(ExchangeReportEvidenceSchema.MaximumBytes + 1L);
        Assert.Contains("32 MiB", Assert.Throws<ConfigurationException>(() => ExchangeReportImport.ReadFile(file, TestData.TenantA)).Message);
    }

    [Fact]
    public void Invalid_utf8_is_not_replaced_to_form_otherwise_valid_digest_consistent_evidence()
    {
        using var root = new TempRoot();
        var report = InboxRuleEvidenceV2Tests.Report();
        report.ToolkitVersion = "synthetic-\uFFFD";
        InboxRuleEvidenceV2Tests.Read(report);
        var json = ExchangeReportEvidenceSchema.Serialize(report).Replace("\\uFFFD", "\uFFFD", StringComparison.OrdinalIgnoreCase);
        var bytes = Encoding.UTF8.GetBytes(json);
        var index = Array.IndexOf(bytes, (byte)0xef);
        Assert.True(index >= 0);
        var invalid = bytes[..index].Concat(new byte[] { 0xff }).Concat(bytes[(index + 3)..]).ToArray();
        Assert.Equal(report.Id, ExchangeReportEvidenceSchema.Read(Encoding.UTF8.GetString(invalid), TestData.TenantA).Id);
        var file = Path.Combine(root.Root, "invalid-synthetic.json");
        File.WriteAllBytes(file, invalid);
        Assert.Contains("not valid UTF-8", Assert.Throws<ConfigurationException>(() => ExchangeReportImport.ReadFile(file, TestData.TenantA)).Message);
    }
}

using System.Text;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ExchangeEncodedBudgetTests
{
    [Theory]
    [InlineData("Plain synthetic")]
    [InlineData("Quotes \" and <& plus +")]
    [InlineData("é-🍵-日本語")]
    public void The_budget_counts_exact_encoded_bytes_including_parent_keys_and_unicode(string name)
    {
        var report = InboxRuleEvidenceV2Tests.Report();
        report.Sections[1].Rows[0]["name"] = name;
        InboxRuleEvidenceV2Tests.Read(report);
        var bytes = Encoding.UTF8.GetByteCount(ExchangeReportEvidenceSchema.Serialize(report));
        ExchangeReportEvidenceSchema.RequireEncodedSize(report, bytes);
        Assert.ThrowsAny<ToolkitException>(() => ExchangeReportEvidenceSchema.RequireEncodedSize(report, bytes - 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ExchangeReportEvidenceSchema.MaximumBytes + 1)]
    public void An_internal_budget_cannot_disable_or_raise_the_hard_reader_cap(int budget) =>
        Assert.ThrowsAny<ToolkitException>(() => ExchangeReportEvidenceSchema.RequireEncodedSize(InboxRuleEvidenceV2Tests.Report(), budget));
}

using System.Xml;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Tenant-supplied text in exported files: it must survive intact where it can, and never break the file.</summary>
public class ExportTextTests
{
    // Code points rather than strings: a lone surrogate in theory data does not survive test discovery intact.
    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    [InlineData(0x0001)]
    [InlineData(0x0008)]
    public void A_character_XML_cannot_hold_is_dropped_rather_than_breaking_the_workbook(int code)
    {
        var hostile = ((char)code).ToString();
        var xml = XlsxWriter.Xml("before" + hostile + "after");

        Assert.Null(Record.Exception(() => XmlConvert.VerifyXmlChars(xml)));
        Assert.StartsWith("before", xml, StringComparison.Ordinal);
        Assert.EndsWith("after", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_surrogate_pair_is_kept()
    {
        Assert.Equal("tenant \U0001F600", XlsxWriter.Xml("tenant \U0001F600"));
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        // 31939 characters then a pair: the 31940-character cut would otherwise fall between its two halves.
        var text = new string('x', 31939) + "\U0001F600" + new string('y', 100);

        var xml = XlsxWriter.Xml(text);

        Assert.Null(Record.Exception(() => XmlConvert.VerifyXmlChars(xml)));
        Assert.Contains("[TRUNCATED FOR EXCEL", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_path_in_a_markdown_code_span_is_shown_literally()
    {
        var result = new AssessmentResult { TenantName = "Contoso & Partners" };
        result.Findings.Add(new ControlFinding
        {
            ControlId = "CA-001", Name = "Require MFA", Status = FindingStatus.PartialMatch, Severity = "High",
            Candidates = { new CandidateMatch { Name = "Policy", ObjectId = "id", Differences = { new PropertyDifference { Setting = "settings.A&B<value>", Current = "<b>x</b>", Standard = "y" } } } }
        });

        var md = MarkdownReports.Engineer(result);

        Assert.Contains("`settings.A&B<value>`", md, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", md, StringComparison.Ordinal);
        Assert.Contains("Contoso &amp; Partners", md, StringComparison.Ordinal);
    }
}

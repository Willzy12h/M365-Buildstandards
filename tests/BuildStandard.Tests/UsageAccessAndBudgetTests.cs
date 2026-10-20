using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageAccessAndBudgetTests
{
    [Theory][InlineData("Reports.Read.All")][InlineData("https://graph.microsoft.com/Reports.Read.All")]
    public void The_approved_optional_report_read_scope_is_classified_without_requesting_access(string scope)
    {Assert.Equal(TokenCapability.ReadOnlyVerified,TokenCapabilityClassifier.Classify(["User.Read",scope]).Classification);}
    [Fact] public void Staged_size_reserves_space_for_final_provenance_and_integrity_without_parsing_or_admitting_it()
    {
        var candidate=UsageReviewSchemaTests.Report();candidate.Sources[0].OriginalCsvBase64="";
        var overhead=Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(candidate,RegisteredReportJson.Strict));
        // Size-only fixture, intentionally not schema-admitted CSV: the guard must reserve finalisation space first.
        candidate.Sources[0].OriginalCsvBase64=new string('A',UsageReviewSchema.MaximumBytes-32-overhead);
        Assert.ThrowsAny<ToolkitException>(()=>UsageReviewSchema.RequireCandidateSize(candidate));
    }
}

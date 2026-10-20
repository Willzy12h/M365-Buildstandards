using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExchangeFamilyReadabilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]{CalendarPermissionEvidenceV2Tests.Report(),"permissions","Principal","Rights"};
        yield return new object[]{MailboxPermissionEvidenceV2Tests.Report(),"permissions","Principal","Rights"};
        yield return new object[]{DistributionGroupEvidenceV2Tests.Report(),"members","Name","Source identity"};
    }
    [Theory][MemberData(nameof(Cases))]
    public void Ordinary_engineer_fields_lead_the_shared_table_without_dropping_source_keys_or_changing_evidence(ExchangeReportEvidence report,string section,string first,string second)
    {
        var admitted=InboxRuleEvidenceV2Tests.Read(report);var before=ExchangeReportEvidenceSchema.Serialize(admitted);
        var sheet=RegisteredReportDocuments.Sheets(admitted).Single(s=>s.Name==section);var header=sheet.Rows[0];
        Assert.Equal(first,header[0]);Assert.Equal(second,header[1]);
        Assert.Contains("Local observation key (not a service ID)",header);Assert.Equal(header.Length,header.Distinct().Count());
        Assert.All(sheet.Rows.Skip(1),row=>Assert.Equal(header.Length,row.Length));
        Assert.Equal(before,ExchangeReportEvidenceSchema.Serialize(admitted));
    }
}

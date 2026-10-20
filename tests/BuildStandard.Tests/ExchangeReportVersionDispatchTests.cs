using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExchangeReportVersionDispatchTests
{
    [Theory] [InlineData("exo-mailbox-inventory")] [InlineData("exo-mailbox-protocols")] [InlineData("exo-mailbox-forwarding")] [InlineData("exo-mailbox-holds")]
    public void Existing_registrations_keep_the_exact_old_pair_and_same_definition(string id)
    {var original=ExchangeReportRegistry.Find(id);Assert.Equal(1,original.ReportSchemaVersion);Assert.Equal("registered-exchange-report/1",original.AdapterVersion);Assert.Same(original,ExchangeReportRegistry.Find(id,1,"registered-exchange-report/1"));}
    [Theory] [InlineData(2,"registered-exchange-report/1")] [InlineData(1,"registered-exchange-report/2")] [InlineData(2,"registered-exchange-report/2")] [InlineData(0,"registered-exchange-report/1")] [InlineData(1,"REGISTERED-exchange-report/1")]
    public void A_resealed_old_report_cannot_choose_another_adapter_or_schema(int version,string adapter)
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeReportRegistry.Find("exo-mailbox-inventory",version,adapter));}
    [Fact] public void Unknown_id_cannot_use_an_otherwise_known_version_pair()
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeReportRegistry.Find("not-a-registration",1,"registered-exchange-report/1"));}
}

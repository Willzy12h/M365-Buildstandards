using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageCsvTests
{
    private static byte[] Csv(string source,Dictionary<string,string>? values=null,string? extra=null)
    {
        var columns=UsageReportRegistry.Find(source).Columns;
        string Cell(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        return Encoding.UTF8.GetBytes(string.Join(',',columns)+(extra is null?"":",Future column")+"\r\n"+
            string.Join(',',columns.Select(c=>Cell(values?.GetValueOrDefault(c)??(c=="Report Refresh Date"?"2026-10-18":""))))+(extra is null?"":","+Cell(extra))+"\r\n");
    }
    [Theory][InlineData("active-users")][InlineData("email-activity")][InlineData("teams-activity")][InlineData("onedrive-activity")]
    public void Documented_columns_parse_without_claiming_identity_or_licence_join(string source)
    {
        var result=UsageCsv.Parse(source,"D30",Csv(source,new(){["User Principal Name"]="looks.real@synthetic.example",["User Id"]="11111111-1111-4111-8111-111111111111"}));
        Assert.Single(result.Rows);Assert.Equal("Unknown",result.PrivacyProvenance);Assert.False(result.CanJoinIdentities);
        Assert.Equal("looks.real@synthetic.example",result.Rows[0].Cells["User Principal Name"].Value);
        Assert.Equal(64,result.Sha256.Length);
    }
    [Fact] public void Missing_activity_is_not_zero_and_actual_zero_is_preserved_with_old_activity_date()
    {
        var result=UsageCsv.Parse("email-activity","D7",Csv("email-activity",new(){["Send Count"]="0",["Receive Count"]="",["Last Activity Date"]="2020-01-02",["Is Deleted"]="False"}));
        var cells=Assert.Single(result.Rows).Cells;
        Assert.Equal(new UsageCell("Count","Value","0"),cells["Send Count"]);
        Assert.Equal(new UsageCell("Count","NotReported",null),cells["Receive Count"]);
        Assert.Equal("2020-01-02",cells["Last Activity Date"].Value);Assert.Equal("false",cells["Is Deleted"].Value);
    }
    [Fact] public void Extra_column_names_are_diagnostic_but_their_values_are_not_retained()
    {
        var result=UsageCsv.Parse("email-activity","D30",Csv("email-activity",extra:"do not retain this"));
        Assert.Equal(new[]{"Future column"},result.ExtraColumns);Assert.DoesNotContain("Future column",result.Rows[0].Cells.Keys);
    }
    [Theory][InlineData("negative")][InlineData("fraction")][InlineData("date")][InlineData("bool")][InlineData("bool-padding")][InlineData("duplicate")][InlineData("missing")][InlineData("utf8")][InlineData("quote")][InlineData("width")][InlineData("period")][InlineData("source")]
    public void Malformed_data_is_refused_instead_of_coerced_or_empty(string fault)
    {
        var source="email-activity";var period="D30";var values=new Dictionary<string,string>();
        switch(fault){case "negative":values["Send Count"]="-1";break;case "fraction":values["Send Count"]="1.1";break;case "date":values["Last Activity Date"]="10/11/2026";break;case "bool":values["Is Deleted"]="maybe";break;case "bool-padding":values["Is Deleted"]=" True ";break;case "period":period="D31";break;case "source":source="arbitrary";break;}
        var bytes=Csv("email-activity",values);var text=Encoding.UTF8.GetString(bytes);
        if(fault=="duplicate")bytes=Encoding.UTF8.GetBytes(text.Replace("Display Name","User Principal Name"));
        if(fault=="missing")bytes=Encoding.UTF8.GetBytes(text.Replace("User Principal Name","Renamed"));
        if(fault=="utf8")bytes=[0xff,0xfe,0x00];if(fault=="quote")bytes=Encoding.UTF8.GetBytes(text+"bare\"quote");
        if(fault=="width")bytes=Encoding.UTF8.GetBytes(text+"only-one-cell\r\n");
        Assert.ThrowsAny<ConfigurationException>(()=>UsageCsv.Parse(source,period,bytes));
    }
    [Fact] public void Empty_valid_csv_and_bom_are_not_a_failed_read()
    {
        var bytes=Encoding.UTF8.GetBytes("\uFEFF"+string.Join(',',UsageReportRegistry.Find("active-users").Columns)+"\r\n");
        Assert.Empty(UsageCsv.Parse("active-users","D30",bytes).Rows);
    }
    [Fact] public void Input_byte_bound_is_checked_before_decoding()
    { Assert.ThrowsAny<ConfigurationException>(()=>UsageCsv.Parse("active-users","D30",new byte[UsageCsv.MaximumBytes+1])); }
    [Fact] public void Exact_functions_are_fixed_and_period_cannot_inject_a_query()
    {
        Assert.Equal("/reports/getEmailActivityUserDetail(period='D30')",UsageReportRegistry.Find("email-activity").Route("D30"));
        Assert.Throws<ConfigurationException>(()=>UsageReportRegistry.Find("email-activity").Route("D30')/users"));
    }
    [Fact] public void Row_bound_refuses_the_whole_oversized_source_without_silently_dropping_rows()
    {
        var bytes=Csv("email-activity");var text=Encoding.UTF8.GetString(bytes);var parts=text.Split("\r\n");
        var oversized=Encoding.UTF8.GetBytes(parts[0]+"\r\n"+string.Concat(Enumerable.Repeat(parts[1]+"\r\n",UsageCsv.MaximumRows+1)));
        Assert.True(oversized.Length<UsageCsv.MaximumBytes);
        Assert.Throws<ConfigurationException>(()=>UsageCsv.Parse("email-activity","D30",oversized));
    }
    [Fact] public void Excessive_columns_duplicate_case_and_unclosed_quotes_are_refused()
    {
        var columns=UsageReportRegistry.Find("email-activity").Columns.Concat(Enumerable.Range(1,256).Select(i=>"Future"+i));
        Assert.Throws<ConfigurationException>(()=>UsageCsv.Parse("email-activity","D30",Encoding.UTF8.GetBytes(string.Join(',',columns))));
        Assert.Throws<ConfigurationException>(()=>UsageCsv.Parse("email-activity","D30",Encoding.UTF8.GetBytes("a,A\r\n")));
        Assert.Throws<ConfigurationException>(()=>UsageCsv.Parse("email-activity","D30",Encoding.UTF8.GetBytes("\"unfinished")));
    }
    [Fact] public void Quoted_commas_newlines_and_escaped_quotes_remain_report_text_without_becoming_a_join()
    {
        var result=UsageCsv.Parse("email-activity","D30",Csv("email-activity",new(){["Display Name"]="Name, \"quoted\"\nsecond line"}));
        Assert.Equal("Name, \"quoted\"\nsecond line",result.Rows[0].Cells["Display Name"].Value);
        Assert.False(result.CanJoinIdentities);
    }
}

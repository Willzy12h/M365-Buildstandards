using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExchangeTraceQueryTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-20T12:30:00Z");
    [Fact] public void Selection_is_frozen_into_at_most_nine_inclusive_boundary_windows()
    {var q=ExchangeTraceQuery.Prepare(new(2026,7,23),new(2026,10,20),Now,sender:"sender@synthetic.example");Assert.Equal(9,q.Windows.Count);Assert.Equal(new DateTimeOffset(2026,7,23,0,0,0,TimeSpan.Zero),q.Windows[0].Start);Assert.Equal(Now,q.Windows[^1].End);Assert.All(q.Windows,w=>Assert.InRange(w.End-w.Start,TimeSpan.FromTicks(1),TimeSpan.FromDays(10)));for(var i=1;i<q.Windows.Count;i++)Assert.Equal(q.Windows[i-1].End,q.Windows[i].Start);Assert.True(q.IsCurrentDayIncomplete);Assert.False(q.IsExactTraceQuery);}
    [Fact] public void Earlier_end_date_includes_that_day_without_querying_beyond_it()
    {var q=ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,2),Now,recipient:"recipient@synthetic.example");Assert.Single(q.Windows);Assert.Equal(new DateTimeOffset(2026,10,3,0,0,0,TimeSpan.Zero),q.Windows[0].End);Assert.False(q.IsCurrentDayIncomplete);}
    [Fact] public void Exact_trace_guid_is_distinct_from_Internet_message_id_and_default_subject_is_off()
    {var q=ExchangeTraceQuery.Prepare(new(2026,10,19),new(2026,10,19),Now,messageTraceId:TestData.TenantA);Assert.True(q.IsExactTraceQuery);Assert.Equal(TestData.TenantA,q.MessageTraceId);Assert.Null(q.InternetMessageId);Assert.False(q.IncludeSubject);var other=ExchangeTraceQuery.Prepare(new(2026,10,19),new(2026,10,19),Now,internetMessageId:TestData.TenantA);Assert.False(other.IsExactTraceQuery);Assert.NotEqual(q.FilterDigest,other.FilterDigest);}
    [Fact] public void Frozen_filters_preserve_case_and_subject_choice_in_the_digest()
    {var a=ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now,sender:"Sender@synthetic.example");var b=ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now,sender:"sender@synthetic.example");var c=ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now,sender:"Sender@synthetic.example",includeSubject:true);Assert.NotEqual(a.FilterDigest,b.FilterDigest);Assert.NotEqual(a.FilterDigest,c.FilterDigest);Assert.Equal(a.FilterDigest,ExchangeTraceQuery.Prepare(new(2026,10,2),new(2026,10,2),Now,sender:"Sender@synthetic.example").FilterDigest);}
    [Theory] [InlineData(2026,7,22,2026,10,19)] [InlineData(2026,10,19,2026,10,18)] [InlineData(2026,10,20,2026,10,21)]
    public void Out_of_retention_reversed_and_future_dates_are_refused(int sy,int sm,int sd,int ey,int em,int ed)
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeTraceQuery.Prepare(new(sy,sm,sd),new(ey,em,ed),Now,sender:"sender@synthetic.example"));}
    [Theory] [InlineData(0)] [InlineData(5001)]
    public void Global_row_budget_cannot_be_zero_or_raised(int maximum)
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now,sender:"sender@synthetic.example",maximumRows:maximum));}
    [Fact] public void Unfiltered_scan_and_invalid_or_ambiguous_filters_are_refused()
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now));Assert.ThrowsAny<ToolkitException>(()=>ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now,sender:"bad\naddress"));Assert.ThrowsAny<ToolkitException>(()=>ExchangeTraceQuery.Prepare(new(2026,10,1),new(2026,10,1),Now,messageTraceId:"<internet@id>"));}
    [Fact] public void Offset_clock_is_frozen_as_UTC_without_adding_future_time()
    {var q=ExchangeTraceQuery.Prepare(new(2026,10,20),new(2026,10,20),Now.ToOffset(TimeSpan.FromHours(4)),messageTraceId:TestData.TenantA);Assert.Equal(TimeSpan.Zero,q.FrozenNow.Offset);Assert.Equal(Now,q.Windows[^1].End);}
}

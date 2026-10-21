using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

/// <summary>Separate read-only usage evidence. No identity joins, privacy changes, licence advice or deployment authority.</summary>
public sealed class UsageReportService(IClock clock,string toolkitVersion)
{
    public async Task<UsageReviewReport> CaptureAsync(IUsageReportSource source,TenantSession session,
        IReadOnlyList<string> selected,string period,Action requireCurrent,CancellationToken cancellation)
    {
        if(selected is null||selected.Count is <1 or >4||selected.Distinct(StringComparer.Ordinal).Count()!=selected.Count)
            throw new ConfigurationException("Select one to four different registered usage reports.");
        var ids=selected.ToArray();
        foreach(var id in ids)_=UsageReportRegistry.Find(id).Route(period);
        if(!session.TenantVerified||!session.OperatorVerified||!ReportValues.IsCanonicalGuid(session.TenantId)
            ||!ReportValues.IsCanonicalGuid(session.AccountObjectId)||session.OperatorObjectId!=session.AccountObjectId||source.TenantId!=session.TenantId)
            throw new TenantMismatchException("Usage capture requires the current verified tenant and account; the source belongs to that same tenant.");
        if(string.IsNullOrWhiteSpace(toolkitVersion)||toolkitVersion.Length>128||toolkitVersion.Any(char.IsControl))
            throw new ConfigurationException("Usage capture requires bounded toolkit-version provenance.");
        var tenant=session.TenantId;var account=session.AccountObjectId;
        void AssertCurrent()
        {
            requireCurrent();
            if(!session.TenantVerified||!session.OperatorVerified||session.TenantId!=tenant||session.AccountObjectId!=account
                ||session.OperatorObjectId!=account||source.TenantId!=tenant)
                throw new TenantMismatchException("The usage tenant or account changed. No report was admitted for the current client.");
        }
        AssertCurrent();cancellation.ThrowIfCancellationRequested();
        var report=new UsageReviewReport
        {
            Id=Guid.NewGuid().ToString("D"),TenantId=tenant,AccountObjectId=account,SourceMode="live",StartedAt=Timestamps.Format(clock.UtcNow),
            ToolkitVersion=toolkitVersion,Sources=ids.Select(id=>new UsageReviewSource{SourceId=id,Period=period,Status=ReportReadState.NotAttempted,
                Reason="This selected source was not attempted."}).ToList()
        };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        foreach(var item in report.Sources)
        {
            try
            {
                AssertCurrent();deadline.Token.ThrowIfCancellationRequested();
                var csv=await source.DownloadAsync(item.SourceId,period,deadline.Token).ConfigureAwait(false);
                AssertCurrent();deadline.Token.ThrowIfCancellationRequested();
                var parsed=await Task.Run(()=>UsageCsv.Parse(item.SourceId,period,csv),deadline.Token).ConfigureAwait(false);
                AssertCurrent();deadline.Token.ThrowIfCancellationRequested();
                item.OriginalCsvBase64=Convert.ToBase64String(csv);item.CsvSha256=parsed.Sha256;item.DownloadHost="reports.office.com";
                item.Status=ReportReadState.Collected;item.Reason=null;
                try{UsageReviewSchema.RequireCandidateSize(report);}
                catch(ConfigurationException)
                {
                    item.OriginalCsvBase64=null;item.CsvSha256=null;item.Status=ReportReadState.Failed;
                    item.Reason="The combined40MiB evidence budget was reached. This source was not retained or truncated; earlier admitted sources remain.";
                    break;
                }
            }
            catch(TenantMismatchException){throw;}
            catch(OperationCanceledException)
            {
                AssertCurrent();item.Status=cancellation.IsCancellationRequested?ReportReadState.Cancelled:ReportReadState.Failed;
                item.Reason=cancellation.IsCancellationRequested?"The read was stopped; no partial CSV was admitted. Later sources were not attempted.":"The combined five-minute capture budget ended; no partial CSV was admitted. Later sources were not attempted.";
                break;
            }
            catch(AuthenticationRequiredException)
            {
                AssertCurrent();item.Status=ReportReadState.Failed;item.Reason="Microsoft requires a deliberate reconnect. No interactive retry occurred; later sources were not attempted.";break;
            }
            catch(ToolkitException)
            {
                AssertCurrent();item.Status=ReportReadState.Failed;item.Reason="This registered source could not be downloaded or admitted as complete CSV. No empty-success or identity/privacy inference was substituted.";
            }
        }
        AssertCurrent();report.EndedAt=Timestamps.Format(clock.UtcNow);report.Status=ReportEvidenceSchema.Overall(report.Sources.Select(item=>item.Status));
        // Pure validation/hashing is bounded but potentially sizeable. Never block the WPF dispatcher or discard a
        // completed cancelled-source record just because its operation token is cancelled.
        await Task.Run(()=>UsageReviewSchema.Seal(report),CancellationToken.None).ConfigureAwait(false);
        AssertCurrent();return report;
    }
}

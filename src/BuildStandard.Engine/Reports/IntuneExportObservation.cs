using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;
namespace BuildStandard.Engine.Reports;

/// <summary>Bound original downloaded report. Its raw observations do not prove effective deployment or establish identity joins.</summary>
public sealed class IntuneExportObservation
{
    public string TenantId {get;}
    public string AccountObjectId {get;}
    public string ClientId {get;}
    public PreparedIntuneExportRequest Request {get;}
    public string JobId {get;}
    public string StartedAt {get;}
    public string CompletedAt {get;}
    public IntuneExportArchive Archive {get;}
    public bool IsHistorical {get;}
    private IntuneExportObservation(string tenant,string account,string client,PreparedIntuneExportRequest request,string jobId,string startedAt,string completedAt,IntuneExportArchive archive,bool historical)
    {TenantId=tenant;AccountObjectId=account;ClientId=client;Request=request;JobId=jobId;StartedAt=startedAt;CompletedAt=completedAt;Archive=archive;IsHistorical=historical;}
    public static IntuneExportObservation Create(DeploymentCoverageSelection selection,IntuneExportReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(selection);ArgumentNullException.ThrowIfNull(receipt);
        var request=IntuneExportRequest.Prepare(selection.Family,selection.ObjectId);
        if(selection.PermissionMode!=SessionMode.Deployment||!selection.Scopes.Any(s=>Same(s,request.RequiredScope)||Same(s,"https://graph.microsoft.com/"+request.RequiredScope)))
            throw new SafetyViolationException("Modern report evidence requires its deliberately selected original deployment access and fixed existing reporting scope. Supplied bytes grant no access.");
        if(!Same(selection.TenantId,receipt.TenantId)||!Same(selection.AccountObjectId,receipt.AccountObjectId)||!Same(selection.ClientId,receipt.ClientId)||request.Sha256!=receipt.RequestSha256)
            throw new TenantMismatchException("The returned export actor or exact report request differs from the selected coverage scope. No foreign observation is admitted.");
        ValidateJob(receipt.JobId,receipt.StartedAt,receipt.CompletedAt);
        return new(selection.TenantId,selection.AccountObjectId,selection.ClientId,request,receipt.JobId,receipt.StartedAt,receipt.CompletedAt,IntuneExportArchive.Read(receipt.OriginalArchiveBytes,request),false);
    }
    internal static IntuneExportObservation ReadHistorical(DeploymentCoverageReport report,CoverageReportSource source,byte[] archive)
    {
        var original=source.ModernExport!;var request=IntuneExportRequest.Prepare(report.Selection.Family,report.Selection.ObjectId);
        ValidateJob(original.JobId,source.StartedAt,source.CompletedAt);
        return new(report.TenantId,report.AccountObjectId,report.ClientId,request,original.JobId,source.StartedAt,source.CompletedAt,IntuneExportArchive.Read(archive,request),true);
    }
    internal static void ValidateJob(string id,string startedAt,string completedAt)
    {
        if(id is not {Length:>0 and <=512}||id is "." or ".."||id.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||"/\\?#%".Contains(c)))throw new ConfigurationException("Original export job identity is unsupported; no source value is echoed.");
        if(!Timestamps.TryParse(startedAt,out var started)||!Timestamps.TryParse(completedAt,out var completed)||completed<started||completed-started>TimeSpan.FromMinutes(5))
            throw new ConfigurationException("The original export interval is malformed or exceeds its fixed budget.");
    }
    private static bool Same(string? left,string right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
}

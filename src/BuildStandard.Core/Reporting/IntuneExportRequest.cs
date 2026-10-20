using System.Text.Json;
using BuildStandard.Core.Json;
namespace BuildStandard.Core.Reporting;

/// <summary>Fixed request preview only. It grants no scope, access-mode switch, consent or authority to create a report job.</summary>
public sealed class PreparedIntuneExportRequest
{
    private readonly byte[] _bytes;
    public string Path=>"/deviceManagement/reports/exportJobs";
    public string ReportName {get;}
    public string RequiredScope {get;}
    public string SelectedObjectId {get;}
    public IReadOnlyList<string> Columns {get;}
    public byte[] Bytes=>(byte[])_bytes.Clone();
    public string Sha256 {get;}
    internal PreparedIntuneExportRequest(string name,string scope,string objectId,string filterColumn,string[] columns)
    {
        ReportName=name;RequiredScope=scope;SelectedObjectId=objectId;Columns=Array.AsReadOnly((string[])columns.Clone());
        _bytes=JsonSerializer.SerializeToUtf8Bytes(new{reportName=name,filter=filterColumn+" eq '"+objectId+"'",select=columns,format="csv",localizationType="localizedValuesAsAdditionalColumn"},ToolkitJson.Compact);
        Sha256=CanonicalJson.Sha256Hex(_bytes);
    }
}
public static class IntuneExportRequest
{
    public static PreparedIntuneExportRequest Prepare(CoverageFamily family,string objectId)
    {
        if(!ReportValues.TryGuid(objectId,out var id))throw new ConfigurationException("Select the exact non-empty object GUID. Names, URLs and arbitrary report queries are not admitted.");
        return family switch
        {
            CoverageFamily.Application=>new("DeviceInstallStatusByApp","DeviceManagementApps.ReadWrite.All",id,"ApplicationId",
                ["ApplicationId","DeviceId","UserId","AppInstallState","InstallState","InstallStateDetail","ErrorCode","LastModifiedDateTime","Platform"]),
            CoverageFamily.ClassicConfiguration=>new("DeviceAssignmentStatusByConfigurationPolicy","DeviceManagementConfiguration.ReadWrite.All",id,"PolicyId",
                ["PolicyId","IntuneDeviceId","AadDeviceId","AssignmentStatus","PspdpuLastModifiedTimeUtc","UnifiedPolicyPlatformType","UnifiedPolicyType","AssignmentFilterIds","ReportStatus"]),
            _=>throw new ConfigurationException("This family has no registered modern Intune export. No report-name, beta or deprecated-endpoint fallback is performed.")
        };
    }
}

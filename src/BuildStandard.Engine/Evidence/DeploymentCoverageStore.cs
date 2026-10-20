using System.Security.Cryptography;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;

namespace BuildStandard.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private string CoverageFile(string tenantId,string reportId)
    {
        if(!ReportValues.IsCanonicalGuid(tenantId)||!ReportValues.IsCanonicalGuid(reportId))
            throw new ConfigurationException("Coverage evidence needs exact canonical tenant and report IDs.");
        return Path.Combine(TenantDirectory(tenantId),"coverage-reports",reportId+".json.zip");
    }
    public string SaveDeploymentCoverage(DeploymentCoverageEvidence capture)
    {
        var report=capture.Metadata;var bytes=DeploymentCoverageBundle.Write(capture);
        var file=CoverageFile(report.TenantId,report.Id);
        lock(_gate)
        {
            ReportEnvelopeDispatcher.RefuseLinks(file);
            if(File.Exists(file))throw new SafetyViolationException("A coverage report with this ID already exists. Original records are never replaced.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);ReportEnvelopeDispatcher.RefuseLinks(file);
            var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                {
                    stream.Write(bytes);stream.Flush(true);stream.Position=0;
                    if(!SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
                        throw new IntegrityException("The original coverage report could not be verified on disk. Retain the unsaved result.");
                }
                ReportEnvelopeDispatcher.RefuseLinks(file);
                try{File.Move(temp,file,overwrite:false);}
                catch(IOException)when(File.Exists(file)){throw new SafetyViolationException("A coverage report with this ID already exists. Original records are never replaced.");}
            }
            finally
            {
                try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        return file;
    }
    public DeploymentCoverageEvidence? LoadDeploymentCoverage(string tenantId,string reportId)
    {
        var file=CoverageFile(tenantId,reportId);ReportEnvelopeDispatcher.RefuseLinks(file);
        if(!File.Exists(file))return null;
        var capture=DeploymentCoverageBundle.ReadFile(file,tenantId);
        if(capture.Metadata.Id!=reportId)throw new IntegrityException("The coverage report file carries a different original report ID.");
        return capture;
    }
}

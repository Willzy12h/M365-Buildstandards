using System.Security.Cryptography;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;

namespace BuildStandard.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private string EvaluationFile(string tenantId,string reportId)
    {
        if(!ReportValues.IsCanonicalGuid(tenantId)||!ReportValues.IsCanonicalGuid(reportId))
            throw new ConfigurationException("Evaluation evidence needs exact canonical tenant and report IDs.");
        return Path.Combine(TenantDirectory(tenantId),"ca-evaluations",reportId+".json.zip");
    }
    public string SaveConditionalAccessEvaluation(ConditionalAccessEvaluationCapture capture)
    {
        var report=capture.Metadata;var bytes=ConditionalAccessEvaluationBundle.Write(capture);
        var file=EvaluationFile(report.TenantId,report.Id);
        lock(_gate)
        {
            ReportEnvelopeDispatcher.RefuseLinks(file);
            if(File.Exists(file))throw new SafetyViolationException("An evaluation with this ID already exists. Original records are never replaced.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);ReportEnvelopeDispatcher.RefuseLinks(file);
            var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                {
                    stream.Write(bytes);stream.Flush(true);stream.Position=0;
                    if(!SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
                        throw new IntegrityException("The original evaluation could not be verified on disk. Retain the unsaved result.");
                }
                ReportEnvelopeDispatcher.RefuseLinks(file);
                try{File.Move(temp,file,overwrite:false);}
                catch(IOException)when(File.Exists(file)){throw new SafetyViolationException("An evaluation with this ID already exists. Original records are never replaced.");}
            }
            finally
            {
                try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        return file;
    }
    public ConditionalAccessEvaluationCapture? LoadConditionalAccessEvaluation(string tenantId,string reportId)
    {
        var file=EvaluationFile(tenantId,reportId);ReportEnvelopeDispatcher.RefuseLinks(file);
        if(!File.Exists(file))return null;
        var capture=ConditionalAccessEvaluationBundle.ReadFile(file,tenantId);
        if(capture.Metadata.Id!=reportId)throw new IntegrityException("The evaluation file carries a different original report ID.");
        return capture;
    }
}

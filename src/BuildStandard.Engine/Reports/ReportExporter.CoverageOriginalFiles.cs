using System.Security.Cryptography;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public sealed partial class ReportExporter
{
    /// <summary>Manual original-file copies when a conforming bounded report cannot be saved. Not an import/evidence format.</summary>
    public string ExportCoverageOriginalFiles(DeploymentCoverageCapture capture)
    {
        if(!ReportValues.IsCanonicalGuid(capture.Id)||!ReportValues.IsCanonicalGuid(capture.Selection.TenantId))
            throw new ConfigurationException("Manual original recovery requires exact original record and tenant identities.");
        var files=new List<(string Name,byte[] Bytes)>();var text=new StringBuilder();var page=0;var modern=0;long total=0;
        text.AppendLine("RAW SOURCE COPIES — MANUAL / NOT SAVED AS COMPLETE REPORT EVIDENCE");
        text.AppendLine("These are original source copies, not complete report evidence, source authentication, approval or deployment before-evidence. No import path or job completion is granted. A separate stored record may already exist; do not overwrite or replay it.");
        text.AppendLine("This folder is incomplete unless COPY-VERIFIED.txt exists. SHA256SUMS.txt records each copied source/context file. Reports/recovery copies are excluded from workspace backups; retain them separately under the client-evidence policy.");
        text.AppendLine("Original report ID: "+capture.Id).AppendLine(DeploymentCoverageDocuments.OriginalContext(capture));
        foreach(var source in capture.Sources)
        {
            text.AppendLine().AppendLine("Source: "+source.Key+" · "+source.Capture.State+" · "+source.StartedAt+" to "+source.CompletedAt).AppendLine(source.Capture.Detail);
            foreach(var bytes in source.Capture.OriginalPages)
            {
                if(page>=100)throw new ConfigurationException("Manual originals exceed the bounded100 JSON-page recovery limit; no output was written.");
                Add($"page-{page++:D3}.json",bytes);
            }
            if(source.ModernExport is { } export)
            {
                export.RequireScope(capture.Selection);
                if(++modern>1)throw new ConfigurationException("Only one selected-control modern original archive is registered for recovery.");
                Add("export-000.zip",export.Archive.OriginalArchiveBytes);
                text.AppendLine("Original report job ID: "+export.JobId).AppendLine(export.Archive.Table.Limitations);
            }
        }
        var context=Encoding.UTF8.GetBytes(text.ToString());if(context.Length>64*1024)throw new ConfigurationException("Original recovery context exceeds its bounded64 KiB explanation; no output was written.");
        files.Add(("ORIGINAL-CONTEXT.txt",context));
        var folder=Path.Combine(_paths.ReportsDirectory,"coverage-originals-"+capture.Id+"-"+Guid.NewGuid().ToString("N"));
        ReportEnvelopeDispatcher.RefuseLinks(folder);
        if(Directory.Exists(folder)||File.Exists(folder))throw new SafetyViolationException("The fresh recovery path already exists; no original will be replaced.");
        Directory.CreateDirectory(folder);ReportEnvelopeDispatcher.RefuseLinks(folder);
        // Partial copies are deliberately left without a success marker. Never recursively delete or replace another engineer's files.
        foreach(var file in files)Write(file.Name,file.Bytes);
        var manifest=string.Join('\n',files.Select(f=>Hash(f.Bytes)+"  "+f.Name))+"\n";
        Write("SHA256SUMS.txt",Encoding.UTF8.GetBytes(manifest));
        Write("COPY-VERIFIED.txt",Encoding.UTF8.GetBytes("Each listed file was copied and read back against the retained original bytes. This verifies local copying only, not complete report evidence, live effect or Microsoft authenticity. No tenant operation was repeated.\nManifest SHA-256: "+Hash(Encoding.UTF8.GetBytes(manifest))+"\n"));
        return folder;

        void Add(string name,byte[] bytes)
        {
            total+=bytes.Length;
            if(bytes.Length==0||total>2L*DeploymentCoverageEvidence.MaximumBytes)
                throw new ConfigurationException("Original recovery material exceeds the fixed10 MiB combined JSON/archive bound; no output was written. The5 MiB saved-report limit is unchanged.");
            files.Add((name,bytes));text.AppendLine(name+" · "+bytes.Length+" bytes · SHA-256 "+Hash(bytes));
        }
        void Write(string name,byte[] bytes)
        {
            var file=Path.Combine(folder,name);ReportEnvelopeDispatcher.RefuseLinks(file);
            using var output=new FileStream(file,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
            output.Write(bytes);output.Flush(true);output.Position=0;
            if(!SHA256.HashData(output).AsSpan().SequenceEqual(SHA256.HashData(bytes)))throw new IntegrityException("Original-file copy readback failed. Retain the in-memory original; this folder has not been verified complete.");
        }
        static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

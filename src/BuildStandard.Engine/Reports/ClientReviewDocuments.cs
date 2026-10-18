using System.Net;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Workflow;

namespace BuildStandard.Engine.Reports;

public static class ClientReviewDocuments
{
    public sealed class Document
    {
        public ClientReviewResult Review { get; }
        public ClientReviewEvidence Evidence { get; }
        internal string ProfileJson { get; }
        internal Document(ClientReviewResult review, ClientReviewEvidence evidence, string profileJson)
        { Review=review;Evidence=evidence;ProfileJson=profileJson; }
    }
    public const string Notice = "Offline review of saved evidence, not a live assessment or permission to deploy. Unknown or incomplete history is not a clean result. Digests detect changed bytes; they do not prove source authenticity.";
    public static Document Create(TenantProfile profile, ClientReviewEvidence evidence, DateTimeOffset now)
    {
        var json=ToolkitJson.Serialize(profile);
        return new(ClientReviewProjection.Project(ToolkitJson.Deserialize<TenantProfile>(json),evidence,now),evidence,json);
    }
    public static string Html(Document document, bool clientSummary = false)
    {
        var html=new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Client review</title><style>body{font:15px system-ui;color:#183740;margin:24px;max-width:1200px}table{border-collapse:collapse;width:100%;margin-bottom:24px}td{border:1px solid #d6e3e2;padding:8px;overflow-wrap:anywhere}h1,h2{color:#006666}.table{overflow:auto}</style></head><body><h1>");
        html.Append(WebUtility.HtmlEncode(document.Review.ClientLabel)).Append(" — client review</h1><p>").Append(WebUtility.HtmlEncode(Notice)).Append("</p>");
        foreach(var sheet in Sheets(document).Where(s=>!clientSummary || s.Name is "Summary" or "Jobs" or "Deviations"))
        {
            html.Append("<h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2><div class=\"table\"><table>");
            foreach(var row in sheet.Rows)
            { html.Append("<tr>");foreach(var cell in row)html.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>");html.Append("</tr>"); }
            html.Append("</table></div>");
        }
        return html.Append("</body></html>").ToString();
    }
    public static IReadOnlyList<Sheet> Sheets(Document document)
    {
        var r=document.Review;
        var summary=new Sheet("Summary",["Item","Value"]).Add("Client",r.ClientLabel).Add("Tenant ID",r.TenantId)
            .Add("Reviewed at",Timestamps.Format(r.ReviewedAt)).Add("Source",Notice)
            .Add("Last readable assessment",r.LastAssessment is { } a ? Timestamps.Format(a.At) : "No readable assessment")
            .Add("Assessment ID",r.LastAssessment?.Id ?? "Unknown").Add("Standard release",r.LastAssessment?.Release ?? "Unknown")
            .Add("Standard digest",r.LastAssessment?.StandardDigest ?? "Unknown")
            .Add("Assessment capture",r.LastAssessment is null ? "Unknown" : r.LastAssessment.Complete ? "Complete; stored digest reported intact" : "Incomplete or integrity unconfirmed")
            .Add("Assessment limitations",r.LastAssessment is null ? "Unknown" : string.Join("; ",r.LastAssessment.Limitations))
            .Add("Assessment collection states",r.LastAssessment is null ? "Unknown" : string.Join("; ",r.LastAssessment.CollectionStatus.Select(c=>c.Key+": "+c.Value)))
            .Add("Write history coverage",r.WriteHistoryKnown ? "Known within captured history" : "Unknown — incomplete or unreadable")
            .Add("Recorded write results",r.Writes.Count).Add("Unresolved writes",r.UnresolvedWrites)
            .Add("Jobs",r.Jobs.Count).Add("Complete jobs",r.Jobs.Count(j=>j.Complete)).Add("Jobs with unknown completion",r.Jobs.Count(j=>!j.Known))
            .Add("Diagnostics",r.Diagnostics.Count);
        var writes=new Sheet("Write outcomes",["Kind","Run ID","Control","Acceptance","Unresolved"]);
        foreach(var w in r.Writes)writes.Add(w.Kind,w.RunId,w.ControlId,w.Acceptance,w.Unresolved?"Yes":"No recorded unresolved outcome");
        var jobs=new Sheet("Jobs",["Job ID","Owner","Purpose","Standard","Claim","Reasons"]);
        foreach(var j in r.Jobs)jobs.Add(j.Id,j.Owner,j.Intention,j.Release,j.Claim,string.Join("\n",j.Reasons));
        var deviations=new Sheet("Deviations",["ID","Control","Owner","Review by","Review state"]);
        foreach(var d in r.Deviations)deviations.Add(d.Id,d.ControlId,d.Owner,d.ReviewBy,d.State);
        var diagnostics=new Sheet("Diagnostics",["Section","Original relative path","Problem"]);
        foreach(var d in r.Diagnostics)diagnostics.Add(d.Section,d.File,d.Problem);
        var sources=new Sheet("Sources",["Section","Original relative path","Original byte SHA-256","Bytes"]);
        foreach(var s in document.Evidence.Sources)sources.Add(s.Section,s.RelativePath,s.Sha256,s.Length);
        return new[]{summary,jobs,deviations,writes,diagnostics,sources};
    }
    public static string Export(Document document, EvidenceStore store, string outputDirectory,
        Func<bool> contextCurrent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root=Path.GetFullPath(outputDirectory);
        if(Within(root,store.Paths.DataDirectory) || Within(root,store.Paths.StandardsDirectory))
            throw new ConfigurationException("Choose an output folder outside original client evidence and catalogues.");
        RequireCurrent();
        RequireRegularPath(root);
        Directory.CreateDirectory(root);
        RequireRegularPath(root);
        var name="client-review-"+document.Review.TenantId+"-"+Guid.NewGuid().ToString("N");
        var staging=Path.Combine(root,"."+name+".tmp");var target=Path.Combine(root,name);
        Directory.CreateDirectory(staging);
        var owned=new List<string>();
        try
        {
            var files=new Dictionary<string,string>(StringComparer.Ordinal);
            Write("engineer.html",Encoding.UTF8.GetBytes(Html(document)));
            Write("client.html",Encoding.UTF8.GetBytes(Html(document,true)));
            Write("review.csv.zip",CsvWriter.ZipSheets(Sheets(document)));
            var manifest=new { schemaVersion=1,kind="client-review-pack",document.Review.TenantId,document.Review.ClientLabel,
                reviewedAt=Timestamps.Format(document.Review.ReviewedAt),document.Review.UnresolvedWrites,document.Review.WriteHistoryKnown,
                notice=Notice,sources=document.Evidence.Sources,diagnostics=document.Review.Diagnostics,files };
            Write("manifest.json",Encoding.UTF8.GetBytes(ToolkitJson.Serialize(manifest)),includeDigest:false);
            cancellationToken.ThrowIfCancellationRequested();
            RequireCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging,target); // A new pack appears whole; no existing report is overwritten.
            return target;
            void Write(string file,byte[] bytes,bool includeDigest=true)
            {
                cancellationToken.ThrowIfCancellationRequested();var path=Path.Combine(staging,file);
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {owned.Add(path);stream.Write(bytes);stream.Flush(true);}
                if(includeDigest)files.Add(file,CanonicalJson.Sha256Hex(bytes));
            }
        }
        finally
        {
            if(Directory.Exists(staging))
            {
                foreach(var file in owned)File.Delete(file);
                Directory.Delete(staging,false); // Never recursively remove unrelated files.
            }
        }
        void RequireCurrent()
        {
            if(!contextCurrent())throw new ConfigurationException("The client selection or working context changed. Review it again before exporting.");
            store.RequireClientReviewCurrent(document.Evidence);
            var profile=ToolkitJson.Deserialize<TenantProfile>(document.ProfileJson);
            var saved=store.LoadClientProfiles().SingleOrDefault(p=>p.Id==profile.Id && string.Equals(p.TenantId,profile.TenantId,StringComparison.OrdinalIgnoreCase));
            if(saved is null || ToolkitJson.Serialize(saved)!=document.ProfileJson)
                throw new ConfigurationException("Saved client details changed after this review. Refresh and review the client before exporting.");
        }
    }
    private static bool Within(string path,string parent)
    {
        var relative=Path.GetRelativePath(Path.GetFullPath(parent),path);
        return relative=="." || (!Path.IsPathRooted(relative) && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
    }
    private static void RequireRegularPath(string path)
    {
        for(string? current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes=File.GetAttributes(current); }
            catch(FileNotFoundException) { continue; }
            catch(DirectoryNotFoundException) { continue; }
            if((attributes&FileAttributes.ReparsePoint)!=0)
                throw new ConfigurationException("Choose an output folder without symbolic links or junctions.");
        }
    }
}

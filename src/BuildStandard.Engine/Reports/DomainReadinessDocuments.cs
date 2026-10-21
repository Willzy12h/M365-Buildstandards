using System.Net;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;

namespace BuildStandard.Engine.Reports;

/// <summary>Immutable original bytes. An imported claim never becomes a current collected observation.</summary>
public sealed class DomainReadinessDocument
{
    private readonly byte[] _bytes;
    private readonly string? _file;
    private DomainReadinessDocument(byte[] bytes,bool historical,string? file=null)
    {
        _bytes=bytes.ToArray();DomainReadinessSchema.Read(_bytes);
        Historical=historical;_file=file;OriginalSha256=CanonicalJson.Sha256Hex(_bytes);
    }
    public static DomainReadinessDocument Collected(DomainReadinessReport report)=>new(Encoding.UTF8.GetBytes(ToolkitJson.Serialize(report)),false);
    public static DomainReadinessDocument Import(byte[] bytes)=>new(bytes,true);
    public static DomainReadinessDocument ReadFile(string file)=>new(ReadBounded(file),true,Path.GetFullPath(file));
    public DomainReadinessReport Report=>DomainReadinessSchema.Read(_bytes);
    public byte[] OriginalBytes=>_bytes.ToArray();
    public string OriginalSha256 {get;}
    public bool Historical {get;}
    public string SourceNotice=>Historical
        ? "Historical supplied report — original source and tenant binding are unverified. No current DNS query was made."
        : "Local DNS observations from the configured Windows resolver; no tenant sign-in or configuration change.";
    internal void RequireOriginalCurrent()
    {
        if(_file is not null&&CanonicalJson.Sha256Hex(ReadBounded(_file))!=OriginalSha256)
            throw new ConfigurationException("Original domain report changed. Import it again before exporting; the captured result has been retained.");
    }
    private static byte[] ReadBounded(string file)
    {
        DomainReadinessDocuments.RequireRegularPath(Path.GetFullPath(file));
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>DomainReadinessSchema.MaximumBytes)throw new ConfigurationException("Domain report exceeds 5 MiB.");
        using var reader=new BinaryReader(stream);var bytes=reader.ReadBytes(DomainReadinessSchema.MaximumBytes+1);
        if(bytes.Length>DomainReadinessSchema.MaximumBytes)throw new ConfigurationException("Domain report exceeds 5 MiB.");
        return bytes;
    }
}

public static class DomainReadinessDocuments
{
    public const string Notice="DNS observations are not deployment evidence, proof of mail delivery, effective SPF evaluation, tenant ownership or device enrolment. Complete describes read coverage, not readiness success. Digests detect changed bytes; they do not prove source authenticity. DNS-owner instructions are manual, not executable changes.";
    public static IReadOnlyList<Sheet> Sheets(DomainReadinessDocument document)
    {
        var r=document.Report;
        var summary=new Sheet("Summary",["Item","Value"]).Add("Domain",r.Domain).Add("Source",document.SourceNotice)
            .Add("Read coverage",r.Status).Add("Report ID",r.Id).Add("Run ID",r.RunId)
            .Add("Recorded tenant ID",r.TenantId??"Unbound — no tenant verified").Add("Recorded account object ID",r.AccountObjectId??"Unknown")
            .Add("Started UTC",r.StartedAt).Add("Completed UTC",r.CompletedAt).Add("Requested checks",string.Join(", ",r.RequestedChecks))
            .Add("Toolkit version",r.ToolkitVersion).Add("Adapter version",r.AdapterVersion).Add("Microsoft cloud",r.Cloud)
            .Add("Expected MX targets",r.ExpectedMx.Count==0?"Not configured":string.Join(", ",r.ExpectedMx))
            .Add("Expected-target source",r.ExpectedMxSource??"Not supplied").Add("Original byte SHA-256",document.OriginalSha256)
            .Add("Integrity digest",r.IntegrityDigest).Add("Interpretation",Notice);
        var worksheet=new Sheet("DNS-owner worksheet",["Check","Meaning","Observed result","Supporting details","Manual next step"]);
        foreach(var result in r.Results)worksheet.Add(result.Check,result.Meaning,result.Summary,string.Join("\n",result.Details),result.DnsOwnerNextStep);
        var observations=new Sheet("Observations",["Question","Type","State","Started UTC","Completed UTC","Resolver","Adapter","Native error","Negative owner","Reason"]);
        var answers=new Sheet("Answers",["Question","Type requested","Answer owner","Answer type","Section","TTL seconds","TXT chunks (JSON)","TXT joined per record","Exchange","MX preference","CNAME target"]);
        foreach(var o in r.Observations)
        {
            observations.Add(o.Name,o.Type,o.State,o.StartedAt,o.CompletedAt,o.Resolver,o.AdapterVersion,o.NativeErrorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"Not reported",o.NegativeOwner??"Unknown",o.Reason??"No diagnostic returned");
            foreach(var a in o.Answers)answers.Add(o.Name,o.Type,a.Owner,a.Type,a.Section,a.Ttl?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"Unknown",
                a.TextChunks is null?"Not applicable":System.Text.Json.JsonSerializer.Serialize(a.TextChunks,ToolkitJson.Compact),a.Text??"Not applicable",
                a.Exchange??"Not applicable",a.Preference?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"Not applicable",a.Target??"Not applicable");
        }
        var references=new Sheet("SPF references",["Source domain","Original term","Target domain","Depth","Observed UTC","State"]);
        foreach(var e in r.References)references.Add(e.SourceDomain,e.OriginalTerm,e.TargetDomain,e.Depth,e.ObservedAt,e.State);
        var limitations=new Sheet("Limitations",["Detail"]);foreach(var limitation in r.Limitations)limitations.Add(limitation);
        return [summary,worksheet,observations,answers,references,limitations];
    }
    public static string Html(DomainReadinessDocument document)
    {
        var html=new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Domain readiness</title><style>body{font:15px system-ui;color:#183740;margin:24px;max-width:1400px}table{border-collapse:collapse;width:100%;margin-bottom:24px}td,th{border:1px solid #d6e3e2;padding:8px;text-align:left;overflow-wrap:anywhere}h1,h2{color:#006666}.table{overflow:auto}</style></head><body><h1>Domain readiness — ");
        html.Append(WebUtility.HtmlEncode(document.Report.Domain)).Append("</h1><p>").Append(WebUtility.HtmlEncode(document.SourceNotice)).Append("</p><p>").Append(WebUtility.HtmlEncode(Notice)).Append("</p>");
        foreach(var sheet in Sheets(document))
        {
            html.Append("<h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2><div class=\"table\"><table>");
            for(var i=0;i<sheet.Rows.Count;i++)
            {
                var tag=i==0?"th":"td";html.Append("<tr>");
                foreach(var cell in sheet.Rows[i])html.Append('<').Append(tag).Append('>').Append(WebUtility.HtmlEncode(cell)).Append("</").Append(tag).Append('>');
                html.Append("</tr>");
            }
            html.Append("</table></div>");
        }
        return html.Append("</body></html>").ToString();
    }
    public static string Export(DomainReadinessDocument document,ToolkitPaths paths,string outputDirectory,
        Func<bool> contextCurrent,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();var root=Path.GetFullPath(outputDirectory);
        foreach(var protectedPath in new[]{paths.DataDirectory,paths.StandardsDirectory,paths.ConfigDirectory,paths.AppDirectory,Path.Combine(paths.Root,"app"),paths.TransfersDirectory})
            if(Within(root,protectedPath))throw new ConfigurationException("Choose an export folder outside original evidence, catalogues, configuration and application files.");
        RequireCurrent();RequireRegularPath(root);Directory.CreateDirectory(root);RequireRegularPath(root);
        // A full DNS name need not fit one Windows folder component.
        var name="domain-readiness-"+ReportExporter.SafeName(document.Report.Domain)+"-"+Guid.NewGuid().ToString("N");
        var staging=Path.Combine(root,"."+name+".tmp");var target=Path.Combine(root,name);
        Directory.CreateDirectory(staging);var owned=new List<string>();
        try
        {
            var files=new Dictionary<string,string>(StringComparer.Ordinal);
            Write("report.json",document.OriginalBytes);Write("readiness.html",Encoding.UTF8.GetBytes(Html(document)));
            Write("observations.csv.zip",CsvWriter.ZipSheets(Sheets(document)));
            var r=document.Report;
            Write("manifest.json",Encoding.UTF8.GetBytes(ToolkitJson.Serialize(new{kind="domainReadinessPack",schemaVersion=1,historical=document.Historical,
                r.Domain,r.TenantId,r.Id,r.Status,document.OriginalSha256,sourceNotice=document.SourceNotice,notice=Notice,files})),false);
            RequireCurrent();cancellationToken.ThrowIfCancellationRequested();
            RequireRegularPath(staging);RequireRegularPath(target);
            if(!contextCurrent())throw new ConfigurationException("Working context changed during final verification. Export the original retained report through recovery or review it again.");
            cancellationToken.ThrowIfCancellationRequested();Directory.Move(staging,target);return target;
            void Write(string file,byte[] bytes,bool includeDigest=true)
            {
                cancellationToken.ThrowIfCancellationRequested();RequireRegularPath(staging);var path=Path.Combine(staging,file);
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){owned.Add(path);stream.Write(bytes);stream.Flush(true);}
                if(includeDigest)files.Add(file,CanonicalJson.Sha256Hex(bytes));
            }
        }
        finally
        {
            if(Directory.Exists(staging))
            {
                RequireRegularPath(staging);
                foreach(var file in owned){RequireRegularPath(file);File.Delete(file);}
                Directory.Delete(staging,false); // Never delete unrelated output recursively.
            }
        }
        void RequireCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!contextCurrent())throw new ConfigurationException("Working context changed. Review the retained original report before exporting.");
            document.RequireOriginalCurrent();
        }
    }
    private static bool Within(string path,string parent)
    {
        var relative=Path.GetRelativePath(Path.GetFullPath(parent),path);
        return relative=="."||(!Path.IsPathRooted(relative)&&relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
    }
    internal static void RequireRegularPath(string path)
    {
        for(string? current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try{attributes=File.GetAttributes(current);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new ConfigurationException("Domain report paths must not use symbolic links or junctions.");
        }
    }
}

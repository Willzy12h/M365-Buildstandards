using System.Collections.ObjectModel;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Identity;
using Microsoft.VisualBasic.FileIO;
namespace BuildStandard.Engine.Reports;

/// <summary>Parsed source text, not authenticated tenant evidence, an identity join or a deployment conclusion.</summary>
public sealed class IntuneExportTable
{
    private readonly byte[] _bytes;
    public IReadOnlyList<string> Columns {get;}
    public IReadOnlyList<IReadOnlyDictionary<string,string>> Rows {get;}
    public byte[] OriginalBytes=>(byte[])_bytes.Clone();
    public string OriginalSha256 {get;}
    public string Limitations {get;}
    internal IntuneExportTable(byte[] bytes,string[] columns,List<IReadOnlyDictionary<string,string>> rows,bool application)
    {
        _bytes=(byte[])bytes.Clone();OriginalSha256=CanonicalJson.Sha256Hex(_bytes);Columns=Array.AsReadOnly((string[])columns.Clone());Rows=rows.AsReadOnly();
        Limitations="Original CSV text only, not deployment verification or source authentication. Blank raw cells have no usable reported value; they are not zero, success or not applicable. Zero/future codes and localised labels do not determine success. "
            +(application?"Application DeviceId has an unestablished report namespace; no managed-device/name/UPN join is made.":"IntuneDeviceId and AadDeviceId retain separate Intune/Entra namespaces; no name/UPN join or proof that every setting took effect.");
    }
}
public static class IntuneExportCsv
{
    private const int MaximumBytes=20*1024*1024,MaximumRows=5000,MaximumFieldBytes=64*1024;
    public static IntuneExportTable Read(byte[] bytes,PreparedIntuneExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(bytes);ArgumentNullException.ThrowIfNull(request);
        if(bytes.Length is 0 or >MaximumBytes)throw Bad("The source is empty or exceeds 20 MiB; no complete table is admitted.");
        string text;
        try{text=new UTF8Encoding(false,true).GetString(bytes);}
        catch(DecoderFallbackException){throw Bad("The export is not supported strict UTF-8; no workstation encoding is guessed.");}
        if(text.StartsWith('\uFEFF'))text=text[1..];
        try{PartnerCustomerImport.ValidateQuotePlacement(text);}
        catch(ConfigurationException){throw Bad("CSV quoting is malformed; no private cell text is echoed.");}
        ValidateRecords(text,request.Columns.Count*2);
        using var parser=new TextFieldParser(new StringReader(text)){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};parser.SetDelimiters(",");
        try
        {
            var headers=parser.ReadFields();
            var allowed=request.Columns.Concat(request.Columns.Select(c=>c+"_loc")).ToHashSet(StringComparer.Ordinal);
            if(headers is null||headers.Length<request.Columns.Count||headers.Length>request.Columns.Count*2
                ||headers.Distinct(StringComparer.Ordinal).Count()!=headers.Length||headers.Any(h=>!allowed.Contains(h))
                ||request.Columns.Any(c=>!headers.Contains(c,StringComparer.Ordinal)))
                throw Bad("Raw headings are missing, duplicated or outside the exact selected column set; no smaller successful table is inferred.");
            var selectedColumn=Array.IndexOf(headers,request.Columns[0]);var rows=new List<IReadOnlyDictionary<string,string>>();
            while(!parser.EndOfData)
            {
                if(rows.Count>=MaximumRows)throw Bad("The export exceeds 5,000 source rows; no smaller complete table is inferred.");
                var fields=parser.ReadFields();
                if(fields is null||fields.Length!=headers.Length||fields.Any(f=>Encoding.UTF8.GetByteCount(f)>MaximumFieldBytes))
                    throw Bad("A row has an inconsistent width or a field exceeds 64 KiB; this source was not admitted.");
                if(!ReportValues.TryGuid(fields[selectedColumn],out var selected)||selected!=request.SelectedObjectId)
                    throw Bad("A source row lacks the exact selected report-object identity; no foreign/missing row is silently associated or discarded.");
                var row=new Dictionary<string,string>(StringComparer.Ordinal);
                for(var i=0;i<headers.Length;i++)row.Add(headers[i],fields[i]);
                rows.Add(new ReadOnlyDictionary<string,string>(row));
            }
            return new(bytes,headers,rows,request.ReportName=="DeviceInstallStatusByApp");
        }
        catch(MalformedLineException){throw Bad("CSV quoting/records are malformed; no private source line is echoed.");}
    }
    private static void ValidateRecords(string text,int maximumColumns)
    {
        // TextFieldParser skips blank physical records; do not let it turn an inconsistent row into complete coverage.
        var quoted=false;var content=false;var columns=1;
        for(var i=0;i<text.Length;i++)
        {
            var c=text[i];
            if(c=='"')
            {content=true;if(quoted&&i+1<text.Length&&text[i+1]=='"')i++;else quoted=!quoted;continue;}
            if(quoted)continue;
            if(c==','){content=true;if(++columns>maximumColumns)throw Bad("A record exceeds the bounded selected/localised column count.");}
            else if(c is '\r' or '\n')
            {
                if(!content)throw Bad("A blank physical record cannot be silently skipped as a successful source row.");
                if(c=='\r'){if(i+1>=text.Length||text[i+1]!='\n')throw Bad("Only CRLF or LF record separators are supported.");i++;}
                content=false;columns=1;
            }
            else if(!char.IsWhiteSpace(c))content=true;
        }
        if(!content&&text.Length>0&&text[^1] is not ('\r' or '\n'))throw Bad("A whitespace-only physical record is not a complete data row.");
    }
    private static ConfigurationException Bad(string reason)=>new("Intune export CSV could not be read. "+reason);
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Microsoft.VisualBasic.FileIO;
namespace BuildStandard.Engine.Reports;
public sealed record UsageCell(string Kind,string State,string? Value);
public sealed record UsageCsvRow(int SourceRow,IReadOnlyDictionary<string,UsageCell> Cells);
public sealed record UsageCsvResult(string SourceId,string Period,string Sha256,IReadOnlyList<UsageCsvRow> Rows,IReadOnlyList<string> ExtraColumns)
{
    public string PrivacyProvenance => "Unknown";
    public bool CanJoinIdentities => false;
}
public static class UsageCsv
{
    public const int MaximumBytes=10*1024*1024, MaximumRows=20000, MaximumColumns=256;
    public static UsageCsvResult Parse(string source,string period,byte[] csv)
    {
        var definition=UsageReportRegistry.Find(source);
        if(!UsageReportRegistry.IsPeriod(period))throw Bad("Choose a registered usage period.");
        ArgumentNullException.ThrowIfNull(csv);
        if(csv.Length>MaximumBytes)throw Bad("The usage CSV exceeds10MiB. Nothing was admitted.");
        string text;
        try{text=new UTF8Encoding(false,true).GetString(csv);}
        catch(DecoderFallbackException error){throw new ConfigurationException("The usage CSV must be strict UTF-8.",error);}
        if(text.StartsWith('\uFEFF'))text=text[1..];
        Quotes(text);
        using var parser=new TextFieldParser(new StringReader(text)){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};
        parser.SetDelimiters(",");
        try
        {
            var headers=parser.ReadFields();
            if(headers is null||headers.Length>MaximumColumns||headers.Any(h=>string.IsNullOrWhiteSpace(h)||h.Length>256||h.Any(char.IsControl))
                ||headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=headers.Length)
                throw Bad("Usage CSV headings must be unique, non-empty and bounded.");
            var positions=headers.Select((name,index)=>(name,index)).ToDictionary(c=>c.name,c=>c.index,StringComparer.Ordinal);
            if(definition.Columns.Any(name=>!positions.ContainsKey(name)))throw Bad("Required documented usage columns are missing; this source could not be checked.");
            var extra=headers.Where(name=>!definition.Columns.Contains(name,StringComparer.Ordinal)).ToArray();
            var rows=new List<UsageCsvRow>();
            while(!parser.EndOfData)
            {
                var fields=parser.ReadFields();if(fields is null)break;
                if(rows.Count==MaximumRows)throw Bad("The usage CSV exceeds20,000rows. It was not admitted as an empty or complete result.");
                if(fields.Length!=headers.Length)throw Bad("Usage CSV row width differs from its headings.");
                var cells=new Dictionary<string,UsageCell>(StringComparer.Ordinal);
                foreach(var column in definition.Columns)cells.Add(column,Cell(column,fields[positions[column]]));
                if(cells["Report Refresh Date"].State!="Value")throw Bad("A returned usage row needs its actual report refresh date.");
                rows.Add(new(rows.Count+1,new ReadOnlyDictionary<string,UsageCell>(cells)));
            }
            return new(source,period,Convert.ToHexStringLower(SHA256.HashData(csv)),rows.AsReadOnly(),Array.AsReadOnly(extra));
        }
        catch(MalformedLineException error){throw new ConfigurationException("The usage CSV contains malformed quoting; nothing was admitted.",error);}
    }
    private static UsageCell Cell(string column,string value)
    {
        var kind=column.Contains("Date",StringComparison.Ordinal)?"Date":column.StartsWith("Is ",StringComparison.Ordinal)||column.StartsWith("Has ",StringComparison.Ordinal)?"Boolean":
            column.EndsWith(" Count",StringComparison.Ordinal)||column.EndsWith(" In Seconds",StringComparison.Ordinal)||column is "Post Messages" or "Reply Messages" or "Urgent Messages" or "Report Period"?"Count":"Text";
        if(value.Length>4096||value.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')))throw Bad("A usage value exceeds its supported text shape.");
        if(value.Length==0)return new(kind,"NotReported",null);
        if(kind=="Date")
        {
            if(!DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw Bad("A usage date is not an invariant reported date.");
            return new(kind,"Value",date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        }
        if(kind=="Count")
        {
            if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var count)||count<0)throw Bad("A reported usage count must be a non-negative invariant whole number.");
            return new(kind,"Value",count.ToString(CultureInfo.InvariantCulture));
        }
        if(kind=="Boolean")
        {
            if(!bool.TryParse(value,out var flag))throw Bad("A reported usage flag is not a supported boolean.");
            return new(kind,"Value",flag?"true":"false");
        }
        return new(kind,"Value",value);
    }
    private static void Quotes(string text)
    {
        var quoted=false;var closed=false;var start=true;
        for(var i=0;i<text.Length;i++)
        {
            var c=text[i];
            if(quoted){if(c=='"'){if(i+1<text.Length&&text[i+1]=='"')i++;else{quoted=false;closed=true;}}continue;}
            if(c is ',' or '\r' or '\n'){start=true;closed=false;continue;}
            if(closed||c=='"'&&!start)throw Bad("Usage CSV quotes must enclose a whole field.");
            if(c=='"')quoted=true;start=false;
        }
        if(quoted)throw Bad("The usage CSV has an unfinished quoted field.");
    }
    private static ConfigurationException Bad(string message)=>new(message);
}

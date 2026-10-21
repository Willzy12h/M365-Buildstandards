using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
namespace BuildStandard.Cli;
public static partial class Program
{
    private static int Lifecycle(IReadOnlyDictionary<string,string> options)
    {
        foreach(var option in options)
            if(option.Key is not ("root" or "tenant" or "record" or "family" or "format") || string.IsNullOrWhiteSpace(option.Value))
                throw new ConfigurationException("Lifecycle supports only --root, --tenant, --record, --family and --format, each once with a value. It cannot execute, approve or resume a write.");
        var raw=Require(options,"tenant");
        if(!Guid.TryParseExact(raw,"D",out var id)||id==Guid.Empty)throw new ConfigurationException("Use an exact non-empty tenant GUID.");
        var tenant=id.ToString("D");var context=Context.Open(options);
        if(!options.TryGetValue("record",out var record))
        {
            if(options.ContainsKey("family")||options.ContainsKey("format"))throw new ConfigurationException("Choose --record before its family or export format.");
            var rows=context.Evidence.ListLifecycleAttempts(tenant);
            Console.WriteLine(ToolkitJson.Serialize(new {kind="lifecycle-history",schemaVersion=1,tenantId=tenant,complete=true,
                assurance="Historical local records only; no live reads or writes, no execution approval and no replay. Unknown and intent-only attempts are not safe to repeat.",records=rows}));
            return 0;
        }
        var family=options.GetValueOrDefault("family","run");
        var format=options.GetValueOrDefault("format","json") switch {"json"=>ExportFormat.Json,"html"=>ExportFormat.Html,_=>throw new ConfigurationException("Lifecycle records export as exact original json or inert html.")};
        var source=context.Evidence.ReadLifecycleRecord(tenant,family,record);
        Console.WriteLine(context.Exporter.ExportLifecycleRecord(source,tenant,format));
        Console.WriteLine("Historical record; no live reads or writes, no execution approval and no replay. Original SHA-256 "+source.OriginalSha256);
        return 0;
    }
}

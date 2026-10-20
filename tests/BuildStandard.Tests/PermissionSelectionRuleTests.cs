using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PermissionSelectionRuleTests
{
    internal static IReadOnlyDictionary<string,byte[]> Library(Action<JsonObject>? change=null)=>ExchangeReportRunnerTests.Library(
        "param([string]$MailboxGuids,[bool]$FullAccess,[bool]$SendAs,[bool]$SendOnBehalf)\n$rows=@(Use-BditRead -Command 'Get-EXOMailboxPermission' -Parameters @{Identity=$MailboxGuids})\n@{Sections=@()}\n",
        manifest:manifest=>
        {
            var definition=ExchangeReportRegistry.Find("exo-mailbox-permissions");
            manifest["execution"]!["reportId"]=definition.Id;manifest["execution"]!["reportSchemaVersion"]=2;
            manifest["limits"]!["maximumRows"]=5000;manifest["limits"]!["timeoutSeconds"]=300;
            manifest["parameters"]=new JsonArray(new JsonObject{["name"]="MailboxGuids",["label"]="Exchange mailbox GUIDs",["type"]="string",["help"]="Select exact mailbox GUIDs",["required"]=true,["maxLength"]=1849});
            foreach(var kind in new[]{"FullAccess","SendAs","SendOnBehalf"})manifest["parameters"]!.AsArray().Add(new JsonObject{["name"]=kind,["label"]=kind,["type"]="boolean",["help"]="Explicitly select this permission source",["default"]="false"});
            manifest["rules"]=new JsonArray(new JsonObject{["kind"]="atLeastOneTrue",["parameters"]=new JsonArray("FullAccess","SendAs","SendOnBehalf")});
            manifest["outputSchemaVersion"]=2;manifest["outputSchema"]=new JsonObject{["columns"]=new JsonArray(),["sections"]=new JsonArray(definition.Sections.Select(section=>(JsonNode)new JsonObject{["id"]=section.Id,["columns"]=new JsonArray(ExchangeReportEvidenceSchema.Columns(section.RowType).Select(column=>(JsonNode?)column).ToArray())}).ToArray())};
            change?.Invoke(manifest);
        });
    [Theory][InlineData("FullAccess")][InlineData("SendAs")][InlineData("SendOnBehalf")]
    public void Exactly_selected_boolean_source_is_required_and_preserved_before_run_preparation(string selected)
    {
        var item=Assert.Single(ScriptCatalogue.Load(Library()).Entries);
        var inputs=new Dictionary<string,string?>{["MailboxGuids"]=TestData.Office};
        var empty=ScriptInputs.Bind(item.Manifest,inputs,DateTimeOffset.UtcNow);Assert.False(empty.IsValid);
        inputs[selected]="true";var bound=ScriptInputs.Bind(item.Manifest,inputs,DateTimeOffset.UtcNow);
        Assert.True(bound.IsValid,string.Join(" ",bound.Problems));
        Assert.True(Assert.IsType<ScriptFlag>(bound.Arguments.Single(argument=>argument.Name==selected).Value).Value);
        Assert.Equal(2,bound.Arguments.Count);
    }
    [Theory][InlineData("missing")][InlineData("duplicate")][InlineData("reordered")][InlineData("default-true")][InlineData("required-flag")][InlineData("extra-parameter")][InlineData("wrong-kind")]
    public void The_exact_permission_rule_cannot_be_omitted_repurposed_or_silently_select_a_source(string fault)
    {
        var library=Library(manifest=>
        {
            switch(fault)
            {
                case "missing":manifest["rules"]=new JsonArray();break;
                case "duplicate":manifest["rules"]!.AsArray().Add(manifest["rules"]![0]!.DeepClone());break;
                case "reordered":manifest["rules"]![0]!["parameters"]=new JsonArray("SendAs","FullAccess","SendOnBehalf");break;
                case "default-true":manifest["parameters"]![1]!["default"]="true";break;
                case "required-flag":manifest["parameters"]![1]!["required"]=true;break;
                case "extra-parameter":manifest["rules"]![0]!["parameters"]!.AsArray().Add("MailboxGuids");break;
                case "wrong-kind":manifest["rules"]![0]!["kind"]="atLeastOne";break;
            }
        });
        Assert.ThrowsAny<ToolkitException>(()=>ScriptCatalogue.Load(library));
    }
}

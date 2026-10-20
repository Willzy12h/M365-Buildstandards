using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Scripts;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExchangeTransportScopeTests
{
    [Fact] public void Explicit_inventory_has_no_selected_identity()
    {var scope=ExchangeTransportScope.Parse("",true);Assert.True(scope.OrganisationInventory);Assert.Empty(scope.RuleGuids);}
    [Fact] public void Exact_rule_selection_preserves_order_without_an_estate_scan()
    {var scope=ExchangeTransportScope.Parse(TestData.Office+","+TestData.Operator,false);Assert.False(scope.OrganisationInventory);Assert.Equal(new[]{TestData.Office,TestData.Operator},scope.RuleGuids);}
    [Theory][InlineData(null,false)][InlineData("",false)][InlineData(" ",true)][InlineData("not-a-guid",false)]
    public void Missing_or_invalid_selection_cannot_become_inventory(string? rules,bool inventory)
    {Assert.Throws<ConfigurationException>(()=>ExchangeTransportScope.Parse(rules,inventory));}
    [Fact] public void Ambiguous_scope_and_duplicate_ids_are_refused()
    {
        Assert.Throws<ConfigurationException>(()=>ExchangeTransportScope.Parse(TestData.Office,true));
        Assert.Throws<ConfigurationException>(()=>ExchangeTransportScope.Parse(TestData.Office+","+TestData.Office,false));
    }
    [Theory][InlineData(false,false,false)][InlineData(true,true,false)][InlineData(false,true,true)][InlineData(true,false,true)]
    public void Shared_form_binding_refuses_ambiguous_or_missing_scope_before_review(bool selected,bool inventory,bool valid)
    {
        var original=ScriptCatalogue.Shipped.Find("exo.distribution-group-observations").Manifest;
        var manifest=original with {Id="exo.transport-rule-observations",Execution=original.Execution! with {ReportId="exo-transport-rules"},
            Parameters=[new("RuleGuids","Rule GUIDs",ScriptParameterType.String,"Exact selected rule GUIDs",MaxLength:1849),new("OrganisationInventory","Organisation inventory",ScriptParameterType.Boolean,"Explicit inventory",Default:"false")]};
        var result=ScriptInputs.Bind(manifest,new Dictionary<string,string?>{["RuleGuids"]=selected?TestData.Office:"",["OrganisationInventory"]=inventory?"true":"false"},DateTimeOffset.UtcNow);
        Assert.Equal(valid,result.IsValid);
    }
}

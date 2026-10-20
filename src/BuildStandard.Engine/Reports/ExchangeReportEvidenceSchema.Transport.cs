using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
public static partial class ExchangeReportEvidenceSchema
{
    private static void ValidateTransportRow(ExchangeReportRow row)
    {
        switch(row)
        {
            case ExchangeTransportReadRow read:
                if(read.Id!=ExchangeObservationKey.TransportRead(read.Scope,read.RequestedRuleGuid))throw Bad("Transport diagnostic key differs from its exact scope tuple.");
                if(read.RequestedResultSize is not null and (<1 or >5000))throw Bad("Transport source limit is not bounded.");
                if(read.ReadStatus is ReportReadState.Collected or ReportReadState.Partial)
                {
                    if(read.RequestedResultSize is null||read.ReturnedEntries is null or <0||read.ExcludedEntries is null or <0||read.ExcludedEntries>read.ReturnedEntries||read.ReturnedEntries>read.RequestedResultSize)
                        throw Bad("Transport reads need actual bounded source/excluded counts.");
                    if(read.ReadStatus==ReportReadState.Collected&&(read.ExcludedEntries!=0||read.ReturnedEntries==read.RequestedResultSize))throw Bad("Saturated or excluded rule reads cannot claim completeness.");
                }
                else if(read.ReturnedEntries is not null||read.ExcludedEntries is not null)throw Bad("Failed/unattempted rule counts are unknown, never zero.");
                if(read.ReadStatus==ReportReadState.NotAttempted&&read.RequestedResultSize is not null)throw Bad("An unattempted read cannot claim a requested source limit.");
                break;
            case TransportRuleObservationRow rule:
                CanonicalGuid(rule.RuleGuid);if(rule.Id!=rule.RuleGuid)throw Bad("Transport policy row ID must equal its original rule GUID.");
                var known=FamilyValue(rule.Priority,value=>{if(value<0)throw Bad("Rule priority cannot be negative.");return true;});
                known&=FamilyValue(rule.State,value=>{FamilyIdentity(value);return true;});known&=FamilyValue(rule.Mode,value=>{FamilyIdentity(value);return true;});
                known&=FamilyValue(rule.ActivationDate,value=>{Text(value);return true;});known&=FamilyValue(rule.ExpiryDate,value=>{Text(value);return true;});
                if(rule.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)||(!known&&rule.ReadStatus==ReportReadState.Collected))throw Bad("Unknown rule metadata needs partial status.");
                break;
            default:throw Bad("Unregistered transport row type.");
        }
    }
    private static void ValidateTransportReport(ExchangeReportEvidence evidence)
    {
        var scope=ExchangeTransportScope.Parse(evidence.Parameters.Single(p=>p.Name=="RuleGuids").Value,evidence.Parameters.Single(p=>p.Name=="OrganisationInventory").Value=="true");
        var targetSection=evidence.Sections.Single(s=>s.Id=="target-reads");var policySection=evidence.Sections.Single(s=>s.Id=="policies");
        var reads=targetSection.Rows.Select(row=>JsonSerializer.Deserialize<ExchangeTransportReadRow>(row.ToJsonString(),Strict)!).ToArray();
        var policies=policySection.Rows.Select(row=>JsonSerializer.Deserialize<TransportRuleObservationRow>(row.ToJsonString(),Strict)!).ToArray();
        if(scope.OrganisationInventory)
        {if(reads.Length!=1||reads[0].Scope!="OrganisationInventory"||reads[0].RequestedRuleGuid is not null)throw Bad("Inventory requires exactly one explicit inventory diagnostic.");}
        else if(reads.Length!=scope.RuleGuids.Count||reads.Any(r=>r.Scope!="SelectedRule")||!reads.Select(r=>r.RequestedRuleGuid).ToHashSet(StringComparer.Ordinal).SetEquals(scope.RuleGuids))
            throw Bad("Every selected rule needs its own exact scope diagnostic.");
        if(reads.Length+reads.Sum(r=>r.ReturnedEntries??0)>5000)throw Bad("Transport observations and diagnostics exceed their total5,000-row budget.");
        if(!scope.OrganisationInventory&&policies.Any(p=>!scope.RuleGuids.Contains(p.RuleGuid,StringComparer.Ordinal)))throw Bad("A broad or foreign rule return cannot satisfy a selected read.");
        foreach(var read in reads)
        {
            var children=scope.OrganisationInventory?policies:policies.Where(p=>p.RuleGuid==read.RequestedRuleGuid).ToArray();
            if(read.ReadStatus is ReportReadState.Collected or ReportReadState.Partial)
            {
                if(read.ReturnedEntries!=children.Length+read.ExcludedEntries)throw Bad("Rule source counts must account for retained/excluded entries.");
                if(!scope.OrganisationInventory&&(children.Length!=1||read.ExcludedEntries!=0))throw Bad("Selected rule admission requires one exact rule, never broad inventory or a fabricated empty result.");
                if(read.ReadStatus==ReportReadState.Collected&&children.Any(p=>p.ReadStatus!=ReportReadState.Collected))throw Bad("A successful rule read cannot conceal unknown metadata.");
            }
            else if(children.Length!=0)throw Bad("Failed/unattempted rule reads cannot claim policies.");
        }
        if((targetSection.Status==ReportReadState.Collected||policySection.Status==ReportReadState.Collected)&&reads.Any(r=>r.ReadStatus!=ReportReadState.Collected))throw Bad("Successful rule sections cannot conceal incomplete reads.");
        if(policySection.Status==ReportReadState.NotAttempted&&reads.Any(r=>r.ReadStatus!=ReportReadState.NotAttempted))throw Bad("Attempted policy reads cannot become unattempted.");
    }
}

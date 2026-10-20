using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public static partial class ExchangeReportEvidenceSchema
{
    /// <summary>
    /// INT-131 exact child-normalisation boundary, after actor/envelope admission. Source fields remain unchanged;
    /// generated keys are local tuple identities, not raw child IDs, Microsoft identities or execution authority.
    /// </summary>
    internal static void AdmitFamilyRows(ExchangeReportRegistry.Definition definition, List<ReportSection> sections,
        IReadOnlyList<ExchangeReportParameter> parameters)
    {
        definition = ExchangeReportRegistry.Find(definition.Id, 2, ExchangeReportRegistry.FamilyAdapterVersion);
        var candidate = new ExchangeReportEvidence
        {
            ReportId = definition.Id, ReportSchemaVersion = 2, AdapterVersion = definition.AdapterVersion,
            Parameters = [.. parameters], Sections = sections
        };
        ValidateParameters(definition, candidate.Parameters);
        ValidateRowsAndSections(definition, candidate, normaliseChildKeys: true);
        // Reserve bounded fixed actor/tool/command provenance for the final evidence envelope. Include the actual
        // normalised row encoding and keys now: PowerShell and .NET string escaping need not have equal byte lengths.
        RequireEncodedSize(candidate, MaximumBytes - 64 * 1024);
    }

    private static void FamilyIdentity(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>1024||value.Any(char.IsControl))
            throw Bad("A family source identity is missing, unsupported or exceeds1,024 characters.");
    }
    private static bool FamilyValue<T>(ExchangeObserved<T>? observed,Func<T,bool>? validate=null)
    {
        if(observed is null)throw Bad("A required reported-value state is missing.");
        if(observed.State=="Value")
        {
            if(observed.Value is null||observed.Reason is not null)throw Bad("A returned value needs its exact typed value and cannot hide an error.");
            return validate?.Invoke(observed.Value)??true;
        }
        if(observed.State is not ("ReturnedNull" or "Missing" or "Failed" or "NotAttempted" or "Unsupported")
            ||observed.Value is not null||string.IsNullOrWhiteSpace(observed.Reason))
            throw Bad("Unknown, null or unsuccessful values cannot become false/zero/empty or omit their reason.");
        Text(observed.Reason);return false;
    }
    private static bool FamilyReferences(List<ExchangeReferenceValue> values)
    {
        if(values.Count>100||values.Any(v=>v is null))throw Bad("Raw references exceed their100-value bound or contain missing entries.");
        var known=true;
        foreach(var item in values)
        {
            if(item.Namespace is not ("ReturnedText" or "RawIdentity" or "DistinguishedName" or "SpecialPrincipal" or "UnresolvedDisplayLabel"))throw Bad("Unregistered raw reference namespace.");
            FamilyIdentity(item.Value);
            if(item.ExternalDirectoryObjectId is not null)CanonicalGuid(item.ExternalDirectoryObjectId);
            if(item.Namespace=="SpecialPrincipal"&&item.Value is not ("Default" or "Anonymous"))throw Bad("Unknown special principal cannot be inferred from a label.");
            if(item.Namespace=="UnresolvedDisplayLabel")known=false;
        }
        return known;
    }
    private static void ValidateFamilyRow(ExchangeReportRow row,string reportId)
    {
        if(reportId!="exo-inbox-rules")throw Bad("Unregistered report2 family projection.");
        switch(row)
        {
            case ExchangeTargetReadRow target:
                CanonicalGuid(target.RequestedExchangeGuid);
                if(target.Id!=ExchangeObservationKey.TargetRead(reportId,target.RequestedExchangeGuid))throw Bad("The target-read key differs from its exact requested Exchange GUID.");
                State(target.LookupReadStatus);State(target.ChildReadStatus);
                if(target.ObservedExchangeGuid is not null)CanonicalGuid(target.ObservedExchangeGuid);
                if(target.ExternalDirectoryObjectId is not null)CanonicalGuid(target.ExternalDirectoryObjectId);
                if(target.PrimarySmtpAddress is not null)FamilyIdentity(target.PrimarySmtpAddress);
                if(target.LookupReadStatus==ReportReadState.Collected)
                {
                    if(target.ObservedExchangeGuid!=target.RequestedExchangeGuid)throw Bad("An observed mailbox must repeat the requested Exchange GUID before child reads.");
                }
                else if(target.ObservedExchangeGuid is not null||target.ExternalDirectoryObjectId is not null||target.PrimarySmtpAddress is not null||target.ChildReadStatus!=ReportReadState.NotAttempted)
                    throw Bad("An unverified mailbox lookup cannot claim identity fields or child reads.");
                if(target.ChildReadStatus is ReportReadState.Collected or ReportReadState.Partial)
                {
                    if(target.ReturnedEntries is null or <0 or >5000||target.ExcludedEntries is null or <0 or >5000||target.ExcludedEntries>target.ReturnedEntries)
                        throw Bad("A collected/partial target needs bounded original/excluded counts.");
                }
                else if(target.ReturnedEntries is not null||target.ExcludedEntries is not null)
                    throw Bad("An unsuccessful/unattempted read is unknown, not a known zero-entry inventory.");
                if(target.ReadStatus==ReportReadState.Collected&&(target.LookupReadStatus!=ReportReadState.Collected||target.ChildReadStatus!=ReportReadState.Collected||target.ExcludedEntries!=0))
                    throw Bad("A successful target cannot hide failed lookup/child reads or excluded identities.");
                break;
            case InboxRuleObservationRow rule:
                CanonicalGuid(rule.MailboxExchangeGuid);FamilyIdentity(rule.RuleIdentity);
                if(rule.Id!=ExchangeObservationKey.InboxRule(rule.MailboxExchangeGuid,rule.RuleIdentity))throw Bad("The rule observation key differs from its original opaque identity.");
                if(rule.ActualIdentityRaw is not null)FamilyIdentity(rule.ActualIdentityRaw);
                var all=FamilyValue(rule.Enabled);
                all&=FamilyValue(rule.Priority,value=>{if(value<0)throw Bad("A reported rule priority cannot be negative.");return true;});
                all&=FamilyValue(rule.ForwardTo,FamilyReferences);all&=FamilyValue(rule.ForwardAsAttachmentTo,FamilyReferences);all&=FamilyValue(rule.RedirectTo,FamilyReferences);
                all&=FamilyValue(rule.DeleteMessage);all&=FamilyValue(rule.MarkAsRead);all&=FamilyValue(rule.StopProcessingRules);
                if(rule.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)||(!all&&rule.ReadStatus==ReportReadState.Collected))
                    throw Bad("Unknown rule values/unresolved labels require an explicit partial row, never a successful or invented identity.");
                break;
            default:throw Bad("Unregistered family row type.");
        }
    }
    private static void ValidateFamilyReport(ExchangeReportEvidence evidence)
    {
        if(evidence.ReportId!="exo-inbox-rules")throw Bad("Unregistered report2 family.");
        var raw=evidence.Parameters.Single(p=>p.Name=="MailboxGuids").Value;
        var selected=ExchangeTargetSelection.Parse(raw);
        var targetSection=evidence.Sections.Single(s=>s.Id=="target-reads");var ruleSection=evidence.Sections.Single(s=>s.Id=="rules");
        var targets=targetSection.Rows.Select(row=>JsonSerializer.Deserialize<ExchangeTargetReadRow>(row.ToJsonString(),Strict)!).ToArray();
        var rules=ruleSection.Rows.Select(row=>JsonSerializer.Deserialize<InboxRuleObservationRow>(row.ToJsonString(),Strict)!).ToArray();
        if(targets.Length!=selected.Count||targets.Select(t=>t.RequestedExchangeGuid).Distinct(StringComparer.Ordinal).Count()!=targets.Length
            ||!selected.ToHashSet(StringComparer.Ordinal).SetEquals(targets.Select(t=>t.RequestedExchangeGuid)))
            throw Bad("Every selected target needs exactly one read-state row; omission is not a successful empty source.");
        if(rules.Any(r=>!selected.Contains(r.MailboxExchangeGuid,StringComparer.Ordinal)))throw Bad("A rule belongs to an unselected mailbox.");
        if(targets.Length + targets.Sum(t=>t.ReturnedEntries??0)>5000)throw Bad("Target diagnostics and original rule rows exceed the shared 5,000-row report budget.");
        foreach(var target in targets)
        {
            var children=rules.Where(r=>r.MailboxExchangeGuid==target.RequestedExchangeGuid).ToArray();
            if(target.LookupReadStatus!=ReportReadState.Collected||target.ChildReadStatus is not (ReportReadState.Collected or ReportReadState.Partial))
            {if(children.Length!=0)throw Bad("A failed/unattempted target cannot claim child observations.");}
            else if(target.ReturnedEntries!=children.Length+target.ExcludedEntries)
                throw Bad("Original entry counts must account exactly for retained and explicitly excluded identities.");
            if(target.ReadStatus==ReportReadState.Collected&&children.Any(c=>c.ReadStatus!=ReportReadState.Collected))
                throw Bad("A successful target cannot hide unknown child fields.");
        }
        if(ruleSection.Status==ReportReadState.Collected&&targets.Any(t=>t.ReadStatus!=ReportReadState.Collected||t.ChildReadStatus!=ReportReadState.Collected))
            throw Bad("A successful empty/child section cannot conceal an unsuccessful parent inventory.");
        if((ruleSection.Status is ReportReadState.Failed or ReportReadState.NotAttempted)&&rules.Length!=0)
            throw Bad("Failed/unattempted rule sections cannot claim collected rows.");
        if(ruleSection.Status==ReportReadState.NotAttempted&&targets.Any(t=>t.ChildReadStatus!=ReportReadState.NotAttempted))
            throw Bad("The report cannot relabel an attempted child read as unattempted.");
        if(targetSection.Status==ReportReadState.Failed&&targets.Any(t=>t.ReadStatus!=ReportReadState.Failed))
            throw Bad("A failed target section cannot conceal retained successful/partial target observations.");
    }
}

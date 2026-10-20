using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
public static partial class OperationalContextSchema
{
    internal static readonly HashSet<string> RelationshipStates=new(StringComparer.Ordinal){"activating","active","approvalPending","approved","created","expired","expiring","terminated","terminating","terminationRequested"};
    internal static readonly HashSet<string> AssignmentStates=new(StringComparer.Ordinal){"pending","active","deleting","deleted","error"};
    internal static bool BaseRelationship(string? type)=>type is "#microsoft.graph.delegatedAdminRelationship" or "microsoft.graph.delegatedAdminRelationship";
    internal static bool KnownDuration(string? value)
    {
        if(value is null)return false;
        try{var span=System.Xml.XmlConvert.ToTimeSpan(value);return span>=TimeSpan.FromDays(1)&&span<=System.Xml.XmlConvert.ToTimeSpan("P2Y");}
        catch(Exception error)when(error is FormatException or OverflowException){return false;}
    }
    internal static bool KnownExtension(string? value)=>value is "P0D" or "PT0S" or "P180D";
    public static string PartnerAssignmentIdentity(string parent,string child)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{parent,child}))));
    internal static string ExpiryAt(string? endedAt,DateTimeOffset asOf)=>endedAt is null?"Unknown":Utc(endedAt,out var end)?end<=asOf?"EndedAtOrBeforeAsOf":"AfterAsOf":"Unknown";
    private static void ValidatePartner(OperationalContextReport report,DateTimeOffset asOf)
    {
        if(!ReportValues.IsCanonicalGuid(report.CustomerTenantId)||report.CustomerTenantId==report.TenantId)throw Bad("Partner evidence requires a distinct selected-customer identity.");
        var coverage=report.PartnerCoverage??throw Bad("Partner scan coverage is missing.");
        if(coverage.ParentRowsRead<0||coverage.ParentRowsRead>OperationalReportRegistry.MaximumRows||coverage.SelectedCustomerRows<0||coverage.ExcludedOtherCustomerRows<0||coverage.UnattributableCustomerRows<0
            ||(long)coverage.SelectedCustomerRows+coverage.ExcludedOtherCustomerRows+coverage.UnattributableCustomerRows!=coverage.ParentRowsRead)throw Bad("Partner scan counters are inconsistent.");
        var expected=SourceDeclarations(report.ReportId);
        if(report.Sources is null||report.Sources.Count!=2||report.Sources.Where((s,i)=>s is null||s.Api!=expected[i].Api||s.RegisteredRoute!=expected[i].RegisteredRoute||s.Reference!=expected[i].Reference).Any())throw Bad("Partner source declarations differ from the fixed routes.");
        if(report.Sections is null||report.Sections.Count!=2||report.Sections[0]?.Id!="partner-relationships"||report.Sections[1]?.Id!="partner-assignments")throw Bad("Unsupported partner sections/order.");
        foreach(var section in report.Sections)
        {
            RequireState(section.Status,section.Error);
            if(section.Rows is null||section.Limitations is null||section.Status==ReportReadState.NotAttempted&&section.Rows.Count!=0)throw Bad("Unsupported partner section shape.");
            if(section.Rows.Count>0&&section.Status is ReportReadState.Failed or ReportReadState.Cancelled)throw Bad("Retained observations require a partial source state after failure/stop.");
            foreach(var limitation in section.Limitations)Text(limitation);
        }
        if((long)report.Sections[0].Rows.Count+report.Sections[1].Rows.Count>OperationalReportRegistry.MaximumRows||report.Sections[0].Rows.Count>coverage.SelectedCustomerRows)throw Bad("Partner selected/combined row budget is inconsistent.");
        var parents=new List<PartnerRelationshipRow>();var children=new List<PartnerAccessAssignmentRow>();
        foreach(var raw in report.Sections[0].Rows)
        {
            var p=Typed<PartnerRelationshipRow>(raw);Observed(p);
            if(p.Id is not null)SafeParent(p.Id);
            if(p.CustomerTenantId!=report.CustomerTenantId)throw Bad("An unrelated customer identity cannot enter selected-customer evidence.");
            foreach(var value in new[]{p.ResourceType,p.Status,p.Duration,p.AutoExtendDuration})Text(value);
            foreach(var value in new[]{p.CreatedAt,p.ActivatedAt,p.ModifiedAt,p.EndedAt})if(value is not null&&!Utc(value,out _))throw Bad("Malformed original partner time.");
            if(p.ExpiryAtAsOf!=ExpiryAt(p.EndedAt,asOf))throw Bad("Partner expiry does not match the frozen as-of and returned end time.");
            Roles(p.RoleDefinitionIds);
            var known=BaseRelationship(p.ResourceType)&&p.Status is not null&&RelationshipStates.Contains(p.Status)&&p.CreatedAt is not null&&p.ActivatedAt is not null&&p.ModifiedAt is not null&&p.EndedAt is not null
                &&KnownDuration(p.Duration)&&KnownExtension(p.AutoExtendDuration)&&p.RoleDefinitionIds is not null;
            if(!known&&p.ReadStatus!=ReportReadState.Partial)throw Bad("Missing, subtype or future relationship fields must remain partial.");
            RequireState(p.AssignmentReadStatus,p.AssignmentReadError);
            if(p.ReturnedAssignments is <0 or >OperationalReportRegistry.MaximumRows||p.AssignmentReadStatus==ReportReadState.Collected&&p.ReturnedAssignments is null
                ||p.AssignmentReadStatus==ReportReadState.NotAttempted&&p.ReturnedAssignments is not null)throw Bad("Unsupported per-parent child read/count claim.");
            parents.Add(p);
        }
        var uniqueParents=parents.Where(p=>p.Id is not null).GroupBy(p=>p.Id!,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.Ordinal);
        foreach(var group in uniqueParents.Values.Where(g=>g.Count>1))
            if(group.Any(p=>p.ReadStatus!=ReportReadState.Partial||p.AssignmentReadStatus!=ReportReadState.NotAttempted))throw Bad("Ambiguous parents cannot authorise child reads or complete observations.");
        foreach(var raw in report.Sections[1].Rows)
        {
            var c=Typed<PartnerAccessAssignmentRow>(raw);Observed(c);
            if(c.RelationshipId is null||!uniqueParents.TryGetValue(c.RelationshipId,out var parent)||parent.Count!=1||!BaseRelationship(parent[0].ResourceType))throw Bad("Assignment has no unique admitted selected-customer base parent.");
            if(c.AssignmentId is not null&&!Identity(c.AssignmentId))throw Bad("Malformed original assignment ID.");
            if(c.Id!=PartnerAssignmentIdentity(c.RelationshipId,c.AssignmentId??""))throw Bad("Assignment composite identity differs from its exact original tuple.");
            if(!coverage.ParentInventoryComplete||parent[0].AssignmentReadStatus is ReportReadState.NotAttempted or ReportReadState.Failed)throw Bad("Assignment rows contradict parent scan/read admission.");
            foreach(var value in new[]{c.Status,c.ContainerType,c.ContainerId})Text(value);Roles(c.RoleDefinitionIds);
            if((c.AssignmentId is null||c.Status is null||!AssignmentStates.Contains(c.Status)||c.ContainerType!="securityGroup"||!ReportValues.IsCanonicalGuid(c.ContainerId)||c.RoleDefinitionIds is null)&&c.ReadStatus!=ReportReadState.Partial)
                throw Bad("Unknown assignment fields cannot become complete/effective-access claims.");
            children.Add(c);
        }
        foreach(var duplicate in children.GroupBy(c=>c.Id,StringComparer.Ordinal).Where(g=>g.Count()>1))
            if(duplicate.Any(c=>c.ReadStatus!=ReportReadState.Partial))throw Bad("Repeated parent/child tuples must remain partial.");
        foreach(var p in parents)
        {
            var count=children.Count(c=>c.RelationshipId==p.Id);
            if(p.ReturnedAssignments is not null&&p.ReturnedAssignments!=count||p.AssignmentReadStatus==ReportReadState.Collected&&children.Any(c=>c.RelationshipId==p.Id&&c.ReadStatus!=ReportReadState.Collected))throw Bad("Parent child-count/status conceals recorded observations.");
        }
        if(parents.Count(p=>p.AssignmentReadStatus!=ReportReadState.NotAttempted)>OperationalReportRegistry.MaximumRelationships)throw Bad("Too many selected child inventories.");
        if(report.Sections[0].Status==ReportReadState.Collected&&(!coverage.ParentInventoryComplete||coverage.UnattributableCustomerRows!=0||parents.Count!=coverage.SelectedCustomerRows||parents.Any(p=>p.ReadStatus!=ReportReadState.Collected)))throw Bad("Complete parent section conceals bounded/incomplete attribution.");
        if(report.Sections[1].Status==ReportReadState.Collected&&(!coverage.ParentInventoryComplete||coverage.UnattributableCustomerRows!=0||parents.Any(p=>p.AssignmentReadStatus!=ReportReadState.Collected)||children.Any(c=>c.ReadStatus!=ReportReadState.Collected)))throw Bad("Complete child section conceals unknown/unattempted inventories.");
    }
    private static T Typed<T>(System.Text.Json.Nodes.JsonObject? raw)
    {
        if(raw is null)throw Bad("Missing typed partner observation.");
        try{return raw.Deserialize<T>(RegisteredReportJson.Strict)??throw Bad("Missing typed partner observation.");}
        catch(JsonException){throw Bad("Partner row does not match its registered type.");}
    }
    private static void Observed(GraphReportRow row)
    {
        RequireState(row.ReadStatus,row.Error);Text(row.Name);Text(row.Error);
        if(row.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial))throw Bad("Returned partner objects must be observed complete or partial.");
        if(row.Id is not null&&!Identity(row.Id))throw Bad("Malformed partner identity.");
        if((row.Id is null||string.IsNullOrWhiteSpace(row.Name))&&row.ReadStatus!=ReportReadState.Partial)throw Bad("Missing partner name/identity cannot become complete.");
    }
    private static void SafeParent(string id){_=OperationalReportRegistry.Path("partner-relationships",OperationalReadKind.PartnerAssignments,id);}
    private static void Roles(List<string>? roles)
    {
        if(roles is not null&&(roles.Count>OperationalReportRegistry.MaximumRoles||roles.Any(id=>!ReportValues.IsCanonicalGuid(id))||roles.Distinct(StringComparer.Ordinal).Count()!=roles.Count))throw Bad("Unsupported direct-role identity list.");
    }
}

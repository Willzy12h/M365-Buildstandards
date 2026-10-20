using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
/// <summary>Bounded direct relationship observations, never effective customer access.</summary>
public sealed class PartnerRelationshipReportService(IClock clock,string toolkitVersion)
{
    private const string Unknown="Returned relationship fields are missing, malformed or unsupported. Unknown is not absence or effective access.";
    private const string Duplicate="Repeated original identity; all observations remain partial and no ambiguous parent authorises child reads.";
    public async Task<OperationalContextReport> CaptureAsync(IOperationalReportSource source,TenantSession session,string customerTenantId,
        bool partnerDeclared,Action requireCurrent,CancellationToken cancellation)
    {
        var tenant=session.TenantId;var account=session.AccountObjectId;var client=session.ClientId;var mode=session.Mode;
        var scopes=string.Join('\n',session.Scopes.OrderBy(v=>v,StringComparer.Ordinal));
        void Current()
        {
            requireCurrent();
            if(!session.TenantVerified||!session.OperatorVerified||!ReportValues.IsCanonicalGuid(tenant)||!ReportValues.IsCanonicalGuid(account)||!ReportValues.IsCanonicalGuid(client)
                ||source.CustomerTenantId!=customerTenantId||!Enum.IsDefined(session.Mode)||session.OperatorObjectId!=account||session.TenantId!=tenant||source.TenantId!=tenant||session.AccountObjectId!=account||session.ClientId!=client||session.Mode!=mode
                ||scopes!=string.Join('\n',session.Scopes.OrderBy(v=>v,StringComparer.Ordinal)))throw new TenantMismatchException("The verified source partner account, tenant, registration or access changed. No current-customer result was admitted.");
        }
        Current();
        if(source.ReportId!="partner-relationships"||!partnerDeclared||!ReportValues.IsCanonicalGuid(customerTenantId)||customerTenantId==tenant
            ||!session.Scopes.Any(v=>v is "DelegatedAdminRelationship.Read.All" or "https://graph.microsoft.com/DelegatedAdminRelationship.Read.All"))
            throw new ConfigurationException("Declare the verified connection as partner source and select a distinct exact customer with deliberately requested partner read access. This does not prove customer access.");
        if(string.IsNullOrWhiteSpace(toolkitVersion)||toolkitVersion.Length>128||toolkitVersion.Any(char.IsControl))throw new ConfigurationException("Bounded toolkit provenance is required.");
        cancellation.ThrowIfCancellationRequested();var asOf=clock.UtcNow;var stamp=Timestamps.Format(asOf);
        var report=new OperationalContextReport{ReportId="partner-relationships",AccessIntent="partner-relationships",TenantId=tenant,CustomerTenantId=customerTenantId,AccountObjectId=account,ClientId=client,
            PermissionMode=mode.ToString(),SourceMode="live",AsOfUtc=stamp,StartedAt=stamp,EndedAt=stamp,ToolkitVersion=toolkitVersion,
            Sources=OperationalContextSchema.SourceDeclarations("partner-relationships").ToList(),PartnerCoverage=new(),Sections=[Section("partner-relationships"),Section("partner-assignments")]};
        var coverage=report.PartnerCoverage;var parents=report.Sections[0];var children=report.Sections[1];
        var projected=new List<PartnerRelationshipRow>();var childRows=new List<PartnerAccessAssignmentRow>();
        var parentIdentities=new HashSet<string>(StringComparer.Ordinal);var duplicateParents=new HashSet<string>(StringComparer.Ordinal);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var calls=0;var bytes=0;var stop=false;
        bool Reserve(GraphReportRow row)
        {
            if(projected.Count+childRows.Count>=OperationalReportRegistry.MaximumRows)return false;
            // Diagnostics below are fixed, bounded strings. Reserve their later duplicate/child-state shapes up front.
            var size=JsonSerializer.SerializeToUtf8Bytes(row,row.GetType(),OperationalContextSchema.CompactOptions).Length+2048;
            if(size>OperationalReportRegistry.MaximumBytes-128*1024-bytes)return false;bytes+=size;return true;
        }
        async Task<OperationalReportPage> Read(OperationalReadKind kind,string? parent)
        {
            Current();deadline.Token.ThrowIfCancellationRequested();
            if(calls>=OperationalReportRegistry.MaximumCalls)throw new OperationalReadLimitException("The shared request budget ended.");
            calls++;var page=await source.ReadNextAsync(kind,parent,deadline.Token).ConfigureAwait(false);
            Current();deadline.Token.ThrowIfCancellationRequested();
            if(page is null||page.Items is null)throw new ConfigurationException("A registered source did not return an identifiable page.");return page;
        }
        void Failure(ReportSection section,Exception error)
        {
            Current();var observed=section==parents?projected.Count:childRows.Count;
            if(error is OperationCanceledException)
            {
                section.Status=observed>0?ReportReadState.Partial:cancellation.IsCancellationRequested?ReportReadState.Cancelled:ReportReadState.Failed;
                section.Error=cancellation.IsCancellationRequested?"The engineer stopped the read. Earlier observations remain; later reads were not attempted.":"The shared five-minute read budget ended. Earlier observations remain.";stop=true;
            }
            else if(error is OperationalReadLimitException){Incomplete(section,"The shared request, row, byte or time budget ended. Earlier observations remain; later reads were not attempted.");stop=true;}
            else{section.Status=observed>0?ReportReadState.Partial:ReportReadState.Failed;section.Error=error is AuthenticationRequiredException?"Reconnect deliberately. No interactive retry occurred; later sources were not attempted.":"This registered source could not be read completely. No raw failure or empty-success substitute was retained.";if(error is AuthenticationRequiredException)stop=true;}
        }
        parents.Status=ReportReadState.Collected;parents.Error=null;
        try
        {
            var more=true;
            while(more&&!stop)
            {
                var page=await Read(OperationalReadKind.PartnerRelationships,null).ConfigureAwait(false);
                await Task.Run(()=>
                {
                    foreach(var raw in page.Items)
                    {
                        if(raw is null)throw new ConfigurationException("Malformed returned parent object.");
                        if(coverage.ParentRowsRead>=OperationalReportRegistry.MaximumRows)throw new OperationalReadLimitException("The parent scan row budget ended.");
                        coverage.ParentRowsRead++;
                        var sourceIdentity=Identity(raw,"id");
                        if(sourceIdentity is not null&&!parentIdentities.Add(sourceIdentity))duplicateParents.Add(sourceIdentity);
                        var customer=raw["customer"] is JsonObject c?String(c,"tenantId"):null;
                        if(!ReportValues.IsCanonicalGuid(customer)){coverage.UnattributableCustomerRows++;Incomplete(parents,"Some parent objects could not be attributed to a canonical customer. No complete absence is established.");continue;}
                        if(customer!=customerTenantId){coverage.ExcludedOtherCustomerRows++;continue;}
                        coverage.SelectedCustomerRows++;
                        var row=ProjectParent(raw,customerTenantId,asOf);
                        if(!Reserve(row))throw new OperationalReadLimitException("The combined evidence byte/row budget ended.");
                        projected.Add(row);if(row.ReadStatus!=ReportReadState.Collected)Incomplete(parents,Unknown);
                    }
                },deadline.Token).ConfigureAwait(false);
                Current();more=page.HasMore;
                if(!more)coverage.ParentInventoryComplete=true;
            }
        }
        catch(TenantMismatchException){throw;}
        catch(Exception error)when(error is ToolkitException or OperationCanceledException){Failure(parents,error);}
        foreach(var group in projected.Where(p=>p.Id is not null).GroupBy(p=>p.Id,StringComparer.Ordinal).Where(g=>g.Count()>1||duplicateParents.Contains(g.Key!)))
        {foreach(var row in group){row.ReadStatus=ReportReadState.Partial;row.Error=Duplicate;}Incomplete(parents,Duplicate);}
        if(coverage.ParentInventoryComplete&&!stop)
        {
            children.Status=ReportReadState.Collected;children.Error=null;var inventories=0;
            foreach(var parent in projected)
            {
                if(stop)break;
                if(parent.Id is null||!OperationalContextSchema.BaseRelationship(parent.ResourceType)||duplicateParents.Contains(parent.Id)||projected.Count(p=>p.Id==parent.Id)!=1)
                {Incomplete(children,"An ambiguous, unidentified or unsupported-subtype parent did not authorise child reads.");continue;}
                if(inventories>=OperationalReportRegistry.MaximumRelationships){Incomplete(children,"Only 50 selected relationship child inventories are admitted. Later parents were not attempted.");break;}
                inventories++;parent.AssignmentReadStatus=ReportReadState.Collected;parent.AssignmentReadError=null;parent.ReturnedAssignments=0;
                var before=childRows.Count;
                try
                {
                    var more=true;
                    while(more&&!stop)
                    {
                        var page=await Read(OperationalReadKind.PartnerAssignments,parent.Id).ConfigureAwait(false);
                        await Task.Run(()=>
                        {
                            foreach(var raw in page.Items)
                            {
                                if(raw is null)throw new ConfigurationException("Malformed returned child object.");
                                var row=ProjectChild(raw,parent.Id);
                                if(!Reserve(row))throw new OperationalReadLimitException("The combined evidence byte/row budget ended.");
                                childRows.Add(row);parent.ReturnedAssignments++;
                                if(row.ReadStatus!=ReportReadState.Collected){parent.AssignmentReadStatus=ReportReadState.Partial;parent.AssignmentReadError=Unknown;Incomplete(children,Unknown);}
                            }
                        },deadline.Token).ConfigureAwait(false);
                        Current();more=page.HasMore;
                    }
                }
                catch(TenantMismatchException){throw;}
                catch(Exception error)when(error is ToolkitException or OperationCanceledException)
                {
                    Failure(children,error);parent.AssignmentReadStatus=childRows.Count>before?ReportReadState.Partial:error is OperationCanceledException&&cancellation.IsCancellationRequested?ReportReadState.Cancelled:ReportReadState.Failed;
                    parent.AssignmentReadError="This relationship's assignment read did not complete. No zero, absence or effective-access result was inferred.";
                    if(childRows.Count==before)parent.ReturnedAssignments=null;
                }
                foreach(var group in childRows.Where(c=>c.RelationshipId==parent.Id).GroupBy(c=>c.Id,StringComparer.Ordinal).Where(g=>g.Count()>1))
                {
                    foreach(var row in group){row.ReadStatus=ReportReadState.Partial;row.Error=Duplicate;}
                    parent.AssignmentReadStatus=ReportReadState.Partial;parent.AssignmentReadError=Duplicate;Incomplete(children,Duplicate);
                }
            }
            if(coverage.UnattributableCustomerRows>0||projected.Any(p=>p.AssignmentReadStatus!=ReportReadState.Collected)||childRows.Any(c=>c.ReadStatus!=ReportReadState.Collected))
            {if(children.Status==ReportReadState.Collected)Incomplete(children,"Some selected or unattributable parent/child observations remain unknown or unattempted. This does not establish effective access.");}
        }
        if((childRows.Count>0||projected.Any(p=>p.AssignmentReadStatus==ReportReadState.Collected))&&children.Status is ReportReadState.Failed or ReportReadState.Cancelled)
            Incomplete(children,"Some assignment reads failed or stopped; earlier admitted observations and each parent's exact read state remain.");
        Current();report.EndedAt=Timestamps.Format(clock.UtcNow);
        await Task.Run(()=>
        {
            parents.Rows=projected.Select(p=>JsonSerializer.SerializeToNode(p,RegisteredReportJson.Strict)!.AsObject()).ToList();
            children.Rows=childRows.Select(c=>JsonSerializer.SerializeToNode(c,RegisteredReportJson.Strict)!.AsObject()).ToList();
            report.Status=ReportEvidenceSchema.Overall(report.Sections.Select(s=>s.Status));OperationalContextSchema.Seal(report);
        },CancellationToken.None).ConfigureAwait(false);
        Current();return report;
    }
    private static ReportSection Section(string id)=>new(){Id=id,Error="This selected source was not attempted.",Limitations=["Direct partner relationship and container-role observations are not engineer effective access, membership expansion, customer sign-in or full partner eligibility. Source partner and selected customer remain separate."]};
    private static void Incomplete(ReportSection section,string reason){section.Status=ReportReadState.Partial;section.Error=reason;}
    private static string? String(JsonObject raw,string key)
    {
        if(raw[key] is not JsonValue value||!value.TryGetValue<string>(out var text)||text.Length>4096||text.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')))return null;return string.IsNullOrWhiteSpace(text)?null:text;
    }
    private static string? Identity(JsonObject raw,string key)
    {var text=String(raw,key);return text is null||text.Length>512||text.Any(char.IsControl)?null:text;}
    private static string? Time(JsonObject raw,string key)
    {
        var text=String(raw,key);return DateTimeOffset.TryParseExact(text,["yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'","yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var value)?Timestamps.Format(value):null;
    }
    private static List<string>? Roles(JsonObject raw)
    {
        if(raw["accessDetails"] is not JsonObject access||access["unifiedRoles"] is not JsonArray roles||roles.Count>OperationalReportRegistry.MaximumRoles)return null;
        var result=new List<string>();
        foreach(var rawRole in roles){if(rawRole is not JsonObject role||!ReportValues.IsCanonicalGuid(String(role,"roleDefinitionId")))return null;var id=String(role,"roleDefinitionId")!;if(result.Contains(id,StringComparer.Ordinal))return null;result.Add(id);}return result;
    }
    private static PartnerRelationshipRow ProjectParent(JsonObject raw,string customer,DateTimeOffset asOf)
    {
        var id=Identity(raw,"id");if(id is not null)try{_=OperationalReportRegistry.Path("partner-relationships",OperationalReadKind.PartnerAssignments,id);}catch(ConfigurationException){id=null;}
        var p=new PartnerRelationshipRow{Id=id,Name=String(raw,"displayName"),CustomerTenantId=customer,ResourceType=String(raw,"@odata.type"),Status=String(raw,"status"),CreatedAt=Time(raw,"createdDateTime"),ActivatedAt=Time(raw,"activatedDateTime"),ModifiedAt=Time(raw,"lastModifiedDateTime"),EndedAt=Time(raw,"endDateTime"),Duration=String(raw,"duration"),AutoExtendDuration=String(raw,"autoExtendDuration"),RoleDefinitionIds=Roles(raw)};
        p.ExpiryAtAsOf=OperationalContextSchema.ExpiryAt(p.EndedAt,asOf);
        if(id is null||p.Name is null||!OperationalContextSchema.BaseRelationship(p.ResourceType)||p.Status is null||!OperationalContextSchema.RelationshipStates.Contains(p.Status)||p.CreatedAt is null||p.ActivatedAt is null||p.ModifiedAt is null||p.EndedAt is null||!OperationalContextSchema.KnownDuration(p.Duration)||!OperationalContextSchema.KnownExtension(p.AutoExtendDuration)||p.RoleDefinitionIds is null){p.ReadStatus=ReportReadState.Partial;p.Error=Unknown;}
        else p.ReadStatus=ReportReadState.Collected;return p;
    }
    private static PartnerAccessAssignmentRow ProjectChild(JsonObject raw,string parent)
    {
        var id=Identity(raw,"id");var container=raw["accessContainer"] as JsonObject;
        var type=container is null?null:String(container,"accessContainerType");var containerId=container is null?null:String(container,"accessContainerId");if(!ReportValues.IsCanonicalGuid(containerId))containerId=null;
        var c=new PartnerAccessAssignmentRow{Id=OperationalContextSchema.PartnerAssignmentIdentity(parent,id??""),Name=id,AssignmentId=id,RelationshipId=parent,Status=String(raw,"status"),ContainerType=type,ContainerId=containerId,RoleDefinitionIds=Roles(raw)};
        if(id is null||c.Status is null||!OperationalContextSchema.AssignmentStates.Contains(c.Status)||type!="securityGroup"||containerId is null||c.RoleDefinitionIds is null){c.ReadStatus=ReportReadState.Partial;c.Error=Unknown;}else c.ReadStatus=ReportReadState.Collected;return c;
    }
}

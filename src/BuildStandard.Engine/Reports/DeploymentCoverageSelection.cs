using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Naming;
namespace BuildStandard.Engine.Reports;

/// <summary>Immutable report scope, not an assignment proposal, deployment plan or approval.</summary>
public sealed class DeploymentCoverageSelection
{
    private DeploymentCoverageSelection(){}
    public string ControlId {get;private init;}="";
    public string ControlName {get;private init;}="";
    public string ControlDigest {get;private init;}="";
    public string ObjectId {get;private init;}="";
    public string Collection {get;private init;}="";
    public CoverageFamily Family {get;private init;}
    public string TenantId {get;private init;}="";
    public string AccountObjectId {get;private init;}="";
    public string ClientId {get;private init;}="";
    public SessionMode PermissionMode {get;private init;}
    public IReadOnlyList<string> Scopes {get;private init;}=Array.Empty<string>();
    public string SourceSnapshotId {get;private init;}="";
    public string SourceSnapshotDigest {get;private init;}="";
    public string SourceCapturedAt {get;private init;}="";
    public string SourceRunId {get;private init;}="";
    public string SourceRunDigest {get;private init;}="";
    public string StandardRelease {get;private init;}="";
    public string StandardDigest {get;private init;}="";
    public IReadOnlyList<string> PilotUserObjectIds {get;private init;}=Array.Empty<string>();
    public IReadOnlyList<string> PilotDirectoryDeviceObjectIds {get;private init;}=Array.Empty<string>();
    public string Limitations=>"Pilot IDs are report scope, not assignment approval. Source ownership is historical corroboration, not current live ownership. Scoped report evidence is not complete deployment before-evidence and never clears unresolved writes.";
    public static DeploymentCoverageSelection Prepare(StandardCatalogue standard,TenantSnapshot snapshot,ManagedObjectMappings mappings,IReadOnlyList<DeploymentRun> runs,TenantSession session,string controlId,IEnumerable<string>? pilotUsers=null,IEnumerable<string>? pilotDevices=null)
    {
        ArgumentNullException.ThrowIfNull(standard);ArgumentNullException.ThrowIfNull(snapshot);ArgumentNullException.ThrowIfNull(mappings);ArgumentNullException.ThrowIfNull(runs);ArgumentNullException.ThrowIfNull(session);
        if(!ReportValues.TryGuid(session.TenantId,out var tenant)||!ReportValues.TryGuid(session.AccountObjectId,out var account)||!ReportValues.TryGuid(session.ClientId,out var client)
            ||!session.TenantVerified||!session.OperatorVerified||!string.Equals(session.OperatorObjectId,account,StringComparison.OrdinalIgnoreCase)
            ||session.Mode is not (SessionMode.Assessment or SessionMode.Deployment)||session.Scopes is null)
            throw new ConfigurationException("Use a verified tenant/account connection before preparing a coverage scope.");
        if(!string.Equals(snapshot.TenantId,tenant,StringComparison.OrdinalIgnoreCase)||!string.Equals(mappings.TenantId,tenant,StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Coverage source evidence or ownership belongs to another tenant.");
        if(!ReportValues.TryGuid(snapshot.Id,out var snapshotId)||!EvidenceIntegrity.Verify(snapshot,snapshot.IntegrityDigest))
            throw new IntegrityException("Coverage requires the original intact source capture identity/digest; it is not replaced or resealed.");
        if(standard.IntegrityDigest is not {Length:64}||!standard.IntegrityDigest.All(Uri.IsHexDigit)||string.IsNullOrWhiteSpace(standard.Release)||standard.Release!=snapshot.StandardRelease)
            throw new IntegrityException("Select the verified original catalogue release matching the source evidence. No historical mapping is silently rebound.");
        var controls=standard.Controls.Where(c=>c.Id==controlId).ToArray();
        if(controls.Length!=1)throw new ConfigurationException("Select one exact registered control; missing or ambiguous control identities cannot form a coverage request.");
        var control=controls[0];var collection=control.Collection??"";
        var family=collection switch
        {
            "conditionalAccess"=>CoverageFamily.ConditionalAccess,"apps"=>CoverageFamily.Application,
            "compliance"=>CoverageFamily.ClassicCompliance,"configuration"=>CoverageFamily.ClassicConfiguration,
            _=>throw new ConfigurationException("Effective coverage is not registered for this collection. Existing assessment remains available; use the documented manual portal check.")
        };
        var mapping=mappings.Find(controlId);
        if(mapping is null||mapping.ControlId!=controlId||mapping.Collection!=collection||!ReportValues.TryGuid(mapping.ObjectId,out var objectId))
            throw new ConfigurationException("An exact recorded control/collection/object ownership mapping is required; names do not establish ownership.");
        var prepared=DeploymentCoverageReads.Prepare(family,objectId,CoverageReadKind.Object);
        var definition=standard.FindCollection(collection);
        if(definition is null||definition.Api!="v1.0"||definition.BasePath!=prepared.Path[..^(objectId.Length+1)]||definition.Scope!=prepared.Scope
            ||!snapshot.Collections.TryGetValue(collection,out var captured)||!captured.Usable||!string.IsNullOrEmpty(captured.Error)
            ||captured.Api!="v1.0"||captured.Path.Split('?')[0]!=definition.BasePath)
            throw new ConfigurationException("Coverage source API/path/scope or collection completeness does not match the registered source. No beta or permission fallback is attempted.");
        var ownership=NamingAudit.Review(snapshot,mappings,runs).Objects.Where(o=>o.Collection==collection&&string.Equals(o.ObjectId,objectId,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(ownership.Length!=1||ownership[0].Ownership!=NamingAudit.Managed)
            throw new SafetyViolationException("Toolkit ownership could not be corroborated by the existing intact creation/run/readback evidence. Mapping or matching names alone are insufficient.");
        var users=Pilot(pilotUsers);var devices=Pilot(pilotDevices);
        if(users.Count+devices.Count>5000)throw new ConfigurationException("The selected pilot exceeds the 5,000-row report scope.");
        var sourceRun=runs.Single(r=>r.Id==mapping.RunId);
        return new(){ControlId=control.Id,ControlName=control.Name,ControlDigest=CanonicalJson.Sha256(ToolkitJson.ToNode(control)),ObjectId=objectId,Collection=collection,Family=family,
            TenantId=tenant,AccountObjectId=account,ClientId=client,PermissionMode=session.Mode,Scopes=Array.AsReadOnly(session.Scopes.ToArray()),
            SourceSnapshotId=snapshotId,SourceSnapshotDigest=snapshot.IntegrityDigest,SourceCapturedAt=snapshot.CapturedAt,SourceRunId=sourceRun.Id,SourceRunDigest=sourceRun.IntegrityDigest,
            StandardRelease=standard.Release,StandardDigest=standard.IntegrityDigest,PilotUserObjectIds=users,PilotDirectoryDeviceObjectIds=devices};
    }
    private static IReadOnlyList<string> Pilot(IEnumerable<string>? values)
    {
        var result=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in values??[])
        {
            if(result.Count>=5000||!ReportValues.TryGuid(value,out var id)||!seen.Add(id))throw new ConfigurationException("Pilot identities must be bounded, unique directory GUIDs in their explicit user/device namespace; names and zero IDs are refused.");
            result.Add(id);
        }
        return Array.AsReadOnly(result.ToArray());
    }
}

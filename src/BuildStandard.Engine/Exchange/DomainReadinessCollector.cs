using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;

namespace BuildStandard.Engine.Exchange;

public sealed record DomainReadinessRequest(string Domain,IReadOnlyList<DomainReadinessCheck> Checks,
    IReadOnlyList<string>? ExpectedMx=null,string? ExpectedMxSource=null,string Cloud="Public",string? TenantId=null,string? AccountObjectId=null);
public sealed class DomainReadinessCollector
{
    private readonly IDomainReadinessResolver _resolver;
    private readonly IClock _clock;
    public DomainReadinessCollector():this(new WindowsDomainReadinessResolver(),SystemClock.Instance) { }
    internal DomainReadinessCollector(IDomainReadinessResolver resolver,IClock clock){_resolver=resolver;_clock=clock;}
    public async Task<DomainReadinessReport> CollectAsync(DomainReadinessRequest request,CancellationToken ct)
    {
        var domain=MailDomain.Validate(request.Domain);var checks=request.Checks?.ToArray()??[];
        if(checks.Length==0||checks.Length>3||checks.Distinct().Count()!=checks.Length||checks.Any(c=>!Enum.IsDefined(c)))throw new ConfigurationException("Select supported domain readiness checks once each.");
        if(request.Cloud!="Public")throw new ConfigurationException("This domain-readiness adapter supports Microsoft public cloud only; no queries were made.");
        if(checks.Contains(DomainReadinessCheck.WindowsEnrolment))
        { _=new DomainDnsQuestion("enterpriseenrollment."+domain,DomainDnsType.CNAME);_=new DomainDnsQuestion("enterpriseregistration."+domain,DomainDnsType.CNAME); }
        var expected=(request.ExpectedMx??[]).Select(s=>DomainDnsName.Reference(s).Canonical).ToArray();
        if(expected.Length>32||expected.Distinct(StringComparer.Ordinal).Count()!=expected.Length
            ||expected.Length>0&&string.IsNullOrWhiteSpace(request.ExpectedMxSource))throw new ConfigurationException("Expected gateways must be distinct and have an explicit source. No queries were made.");
        if(request.ExpectedMxSource is { } source&&(source.Length>1024||source.Any(char.IsControl)))throw new ConfigurationException("Invalid gateway provenance.");
        foreach(var id in new[]{request.TenantId,request.AccountObjectId})
            if(id is not null&&(!Guid.TryParseExact(id,"D",out var guid)||guid==Guid.Empty))throw new ConfigurationException("Optional client/account bindings must be exact GUIDs.");
        if(request.AccountObjectId is not null&&request.TenantId is null)throw new ConfigurationException("An initiating account needs its verified tenant binding.");
        var start=_clock.UtcNow;var report=new DomainReadinessReport
        {
            Id=Guid.NewGuid().ToString("D"),RunId=Guid.NewGuid().ToString("D"),Domain=domain,RequestedChecks=checks.ToList(),
            TenantId=request.TenantId,AccountObjectId=request.AccountObjectId,ExpectedMx=expected.ToList(),ExpectedMxSource=request.ExpectedMxSource,
            StartedAt=Timestamps.Format(start),ToolkitVersion=ToolkitVersion.Current
        };
        report.Limitations.Add("DNS observations are not proof of delivery, domain ownership, licence, MDM scope, device registration or enrolment. This report is not deployment evidence.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(120));
        var cache=new Dictionary<DomainDnsQuestion,DomainDnsObservation>();var incomplete=false;var observedRows=0;var terms=0;
        void Limited(string reason){incomplete=true;if(!report.Limitations.Contains(reason,StringComparer.Ordinal))report.Limitations.Add(reason);}
        bool Stop()
        {
            if(ct.IsCancellationRequested)return true;
            if(deadline.IsCancellationRequested||_clock.UtcNow-start>=TimeSpan.FromSeconds(120)){Limited("The 120-second report deadline was reached; no further questions were started.");return true;}
            return false;
        }
        DomainReadinessResult Unable(DomainReadinessCheck check,string reason)=>new(check,DomainReadinessMeaning.UnableToCheck,reason,[],"Review the recorded read state and resolver access before a fresh explicit check.");
        async Task<DomainDnsObservation?> Query(string name,DomainDnsType type)
        {
            if(Stop())return null;
            var question=new DomainDnsQuestion(name,type);
            if(cache.TryGetValue(question,out var cached))return cached;
            if(cache.Count>=32||observedRows+report.References.Count>=500){Limited("The question/observation budget was reached; later reads were not made.");return null;}
            DomainDnsObservation observation;
            try
            {
                var response=await _resolver.QueryAsync(question,deadline.Token).ConfigureAwait(false);
                // A substituted/malformed resolver cannot bypass the production typed response contract.
                observation=DomainDnsResponse.Parse(System.Text.Json.JsonSerializer.Serialize(response,ToolkitJson.Compact),question);
            }
            catch(OperationCanceledException) when(deadline.IsCancellationRequested)
            { Stop();return null; }
            catch(Exception ex) when(ex is ToolkitException or IOException)
            { observation=new(question.Name,question.Type,DomainDnsState.Failed,Timestamps.Format(_clock.UtcNow),Timestamps.Format(_clock.UtcNow),DomainDnsResponse.Resolver,"1",[],null,null,"Response could not be admitted; absence is unknown."); }
            if(observedRows+report.References.Count+1+observation.Answers.Count>500){Limited("The 500-observation/reference budget was reached; the last response was not admitted.");return null;}
            cache.Add(question,observation);report.Observations.Add(observation);observedRows+=1+observation.Answers.Count;
            if(observation.State is DomainDnsState.Failed or DomainDnsState.Limited or DomainDnsState.Cancelled)Limited(observation.Reason??"A DNS read was incomplete.");
            return observation;
        }
        foreach(var check in checks)
        {
            if(Stop()){report.Results.Add(Unable(check,"Stopped or report deadline reached; this check did not finish."));continue;}
            if(check==DomainReadinessCheck.Mx)
            {
                var read=await Query(domain,DomainDnsType.MX).ConfigureAwait(false);
                if(read?.State!=DomainDnsState.Observed){report.Results.Add(Unable(check,read?.Reason??"MX could not be read; absence is unknown."));continue;}
                var mx=read.Answers.Where(a=>a.Type==DomainDnsType.MX).ToArray();
                if(mx.Any(a=>a.Exchange=="."))
                {
                    var nullOnly=mx.Length==1&&mx[0].Preference==0;
                    report.Results.Add(new(check,nullOnly?DomainReadinessMeaning.NotConfigured:DomainReadinessMeaning.ReviewFinding,
                        nullOnly?"Null MX observed: the domain advertises no mail exchanger.":"Invalid or mixed Null MX: review the original MX records.",
                        mx.Select(a=>$"Preference {a.Preference}; exchange {a.Exchange}").ToArray(),
                        "DNS owner: confirm whether this domain should receive mail. RFC7505 Null MX uses only preference 0 and exchange '.', with no other MX records."));
                    continue;
                }
                var match=expected.Length>0&&mx.All(a=>expected.Contains(DomainDnsName.Reference(a.Exchange!).Canonical,StringComparer.Ordinal))
                    &&expected.All(target=>mx.Any(a=>DomainDnsName.Reference(a.Exchange!).Canonical==target));
                report.Results.Add(new(check,expected.Length==0?DomainReadinessMeaning.Observed:match?DomainReadinessMeaning.ObservedMatch:DomainReadinessMeaning.ReviewFinding,
                    expected.Length==0?"MX observed; expected gateway not configured.":match?"Observed MX targets match the explicitly selected gateway set.":"Observed MX differs from the selected gateway set; review routing.",
                    mx.Select(a=>$"Preference {a.Preference}; exchange {a.Exchange}; TTL {(a.Ttl?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"Unknown")}").ToArray(),
                    "Confirm the intended mail gateway with the DNS owner. DNS alone does not verify delivery or connectors."));
            }
            else if(check==DomainReadinessCheck.WindowsEnrolment)
            {
                foreach(var item in new[]{("enterpriseenrollment.","enterpriseenrollment-s.manage.microsoft.com"),("enterpriseregistration.","enterpriseregistration.windows.net")})
                {
                    var name=item.Item1+domain;var read=await Query(name,DomainDnsType.CNAME).ConfigureAwait(false);
                    if(read?.State!=DomainDnsState.Observed){report.Results.Add(Unable(check,name+": "+(read?.Reason??"Could not check; absence is unknown.")));continue;}
                    var aliases=read.Answers.Where(a=>a.Type==DomainDnsType.CNAME).ToArray();
                    var match=aliases.Any(a=>DomainDnsName.Reference(a.Owner).Canonical==name&&DomainDnsName.Reference(a.Target!).Canonical==item.Item2);
                    report.Results.Add(new(check,match?DomainReadinessMeaning.ObservedMatch:DomainReadinessMeaning.ReviewFinding,
                        name+(match?": matches the public-cloud DNS reference.":": differs from the public-cloud DNS reference."),aliases.Select(a=>a.Owner+" → "+a.Target).ToArray(),
                        "DNS owner: review CNAME "+name+" → "+item.Item2+" against https://learn.microsoft.com/en-us/intune/device-enrollment/windows/create-cname-autodiscovery . Do not change it without reviewing the existing enrolment method."));
                }
            }
            else
            {
                var details=new List<string>();var finding=false;var unknown=false;var noPolicy=false;
                async Task Visit(string owner,int depth,HashSet<string> path)
                {
                    if(depth>10){Limited("SPF traversal depth limit reached.");unknown=true;return;}
                    if(!path.Add(owner)){Limited("SPF reference loop detected; no repeated query was made.");unknown=true;return;}
                    try
                    {
                        var read=await Query(owner,DomainDnsType.TXT).ConfigureAwait(false);
                        if(read?.State!=DomainDnsState.Observed){unknown=true;details.Add(owner+": "+(read?.Reason??"Not read within the report limits."));return;}
                        var policies=read.Answers.Where(a=>a.Type==DomainDnsType.TXT).Select(a=>a.Text!).Where(t=>t.Equals("v=spf1",StringComparison.OrdinalIgnoreCase)||t.StartsWith("v=spf1 ",StringComparison.OrdinalIgnoreCase)).ToArray();
                        if(policies.Length==0){if(depth==0)noPolicy=true;else finding=true;details.Add(owner+": no SPF version 1 policy in the returned TXT records.");return;}
                        if(policies.Length!=1){finding=true;details.Add(owner+": multiple SPF policies; records were not joined and references were not followed.");return;}
                        var policy=SpfPolicyAnalysis.Parse(policies[0]);details.Add(owner+": "+policy.Original);
                        if(policy.Problems.Count>0){finding=true;details.AddRange(policy.Problems);return;}
                        foreach(var term in policy.Terms.Where(t=>t.Reachable))
                        {
                            if(Stop()){unknown=true;return;}
                            if(term.DnsDependent&&++terms>10){Limited("SPF ten DNS-dependent-term analysis budget reached; a full path count cannot be established.");unknown=true;return;}
                            if(term.NeedsContext){unknown=true;details.Add(term.Original+": requires sender/message context or macro expansion; not evaluated.");}
                            if(term.Reference is null)continue;
                            if(observedRows+report.References.Count>=500){Limited("The 500-observation/reference budget was reached.");unknown=true;return;}
                            report.References.Add(new(owner,term.Original,term.Reference,depth+1,read.CompletedAt,path.Contains(term.Reference)?"Loop":"Reference"));
                            await Visit(term.Reference,depth+1,path).ConfigureAwait(false);
                        }
                    }
                    finally{path.Remove(owner);}
                }
                await Visit(domain,0,new HashSet<string>(StringComparer.Ordinal)).ConfigureAwait(false);
                report.Results.Add(new(check,unknown?DomainReadinessMeaning.UnableToCheck:finding?DomainReadinessMeaning.ReviewFinding:noPolicy?DomainReadinessMeaning.NotConfigured:DomainReadinessMeaning.Observed,
                    unknown?"SPF structural review is incomplete; unknown paths remain.":finding?"SPF structural findings need review.":noPolicy?"No SPF policy was found in the returned TXT records.":"SPF structure observed; this is not sender authentication.",
                    details.AsReadOnly(),"DNS owner: review the original policies and findings with authorised mail senders. Do not flatten or replace records automatically."));
                report.Limitations.Add("SPF analysis is structural only, not RFC7208 check_host. Deduplicated questions do not erase term occurrences; a budget limit does not establish a sender permerror.");
                if(unknown)incomplete=true;
            }
        }
        Stop();report.CompletedAt=Timestamps.Format(_clock.UtcNow);
        report.Status=ct.IsCancellationRequested||report.Observations.Any(o=>o.State==DomainDnsState.Cancelled)?DomainReadinessState.Cancelled
            :incomplete?report.Observations.All(o=>o.State==DomainDnsState.Failed)&&report.Observations.Count>0?DomainReadinessState.Failed:DomainReadinessState.Partial:DomainReadinessState.Complete;
        report.IntegrityDigest=EvidenceIntegrity.Compute(report);
        return report;
    }
}

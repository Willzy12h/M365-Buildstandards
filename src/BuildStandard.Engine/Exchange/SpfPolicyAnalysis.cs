using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using BuildStandard.Core;

namespace BuildStandard.Engine.Exchange;

public sealed record SpfTerm(string Original,string Name,string Qualifier,string Argument,bool IsModifier,bool Reachable,
    bool DnsDependent,string? Reference,bool NeedsContext,string? Problem);
public sealed record SpfPolicyAnalysis(string Original,IReadOnlyList<SpfTerm> Terms,IReadOnlyList<string> Problems)
{
    /// <summary>Structural analysis only; never RFC7208 check_host or a sender authentication result.</summary>
    public static SpfPolicyAnalysis Parse(string policy)
    {
        var terms=new List<SpfTerm>();var problems=new List<string>();
        if(policy.Length>65536 || policy.Any(c=>c<32 || c>126))
            return new(policy,terms.AsReadOnly(),new[]{"SPF policy exceeds the supported text bound or contains non-printable/non-ASCII characters."});
        var tokens=policy.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(tokens.Length==0 || !tokens[0].Equals("v=spf1",StringComparison.OrdinalIgnoreCase))
            return new(policy,terms.AsReadOnly(),new[]{"Expected one SPF version 1 policy."});
        var terminal=false;var modifiers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var original in tokens.Skip(1))
        {
            var body=original;var qualifier="+";var explicitQualifier=body.Length>0&&body[0] is '+' or '-' or '~' or '?';
            if(explicitQualifier){qualifier=body[..1];body=body[1..];}
            var separator=body.IndexOfAny([':', '/', '=']);var modifier=separator>=0&&body[separator]=='=';
            var name=(separator<0?body:body[..separator]).ToLowerInvariant();var suffix=separator<0?"":body[separator..];
            var argument=suffix.Length>0&&suffix[0] is ':' or '='?suffix[1..]:suffix;
            var reachable=!terminal;var dns=false;var context=false;string? reference=null;string? problem=null;
            if(!Regex.IsMatch(name,@"\A[a-z][a-z0-9_.-]*\z",RegexOptions.CultureInvariant))problem="Malformed SPF term name.";
            else if(modifier)
            {
                if(suffix.Length==0||suffix[0]!='='||explicitQualifier)problem="A modifier cannot have a mechanism qualifier or argument separator.";
                else if(name is "redirect" or "exp")
                {
                    if(!modifiers.Add(name))problem="Duplicate SPF "+name+" modifier.";
                    else if(argument.Length==0)problem="Missing SPF modifier domain.";
                    else if(argument.Contains('%'))context=true;
                    else try { var domain=DomainDnsName.Reference(argument).Canonical;if(name=="redirect")reference=domain;else context=true; }
                        catch(ConfigurationException){problem="Invalid literal SPF modifier domain.";}
                    dns=name=="redirect";
                    // Modifiers are not positional mechanisms. A later pass handles the all/redirect rule.
                    reachable=true;
                }
                else reachable=false; // RFC7208 section 6: unrecognised modifiers MUST be ignored.
            }
            else switch(name)
            {
                case "all":
                    if(suffix.Length!=0)problem="The all mechanism takes no argument.";
                    else terminal=true;
                    break;
                case "ip4":case "ip6":
                    if(!suffix.StartsWith(':')||!Address(argument,name=="ip4"))problem="Invalid SPF address or prefix length.";
                    break;
                case "include":
                    dns=true;
                    if(!suffix.StartsWith(':')||argument.Length==0)problem="The include mechanism needs a domain.";
                    else if(argument.Contains('%'))context=true;
                    else try { reference=DomainDnsName.Reference(argument).Canonical; }
                        catch(ConfigurationException){problem="Invalid literal SPF include domain.";}
                    break;
                case "a":case "mx":
                    dns=true;context=true;
                    if(!suffix.Contains('%'))
                    {
                        var match=Regex.Match(suffix,@"\A(?::([^/]+))?(?:/([0-9]{1,3}))?(?://([0-9]{1,3}))?\z",RegexOptions.CultureInvariant);
                        if(!match.Success || match.Groups[2].Success&&int.Parse(match.Groups[2].Value)>32 || match.Groups[3].Success&&int.Parse(match.Groups[3].Value)>128)
                            problem="Invalid SPF address/MX domain or prefix length.";
                        else if(match.Groups[1].Success)try{_=DomainDnsName.Reference(match.Groups[1].Value);}catch(ConfigurationException){problem="Invalid SPF address/MX literal domain.";}
                    }
                    break;
                case "exists":case "ptr":
                    dns=true;context=true;
                    if(suffix.Length==0&&name=="ptr")break;
                    if(!suffix.StartsWith(':')||argument.Length==0)problem="Missing SPF mechanism domain.";
                    else if(!argument.Contains('%'))try{_=DomainDnsName.Reference(argument);}catch(ConfigurationException){problem="Invalid SPF mechanism literal domain.";}
                    break;
                default:problem="Unrecognised SPF mechanism.";break;
            }
            if(problem is not null){problems.Add(original+": "+problem);reference=null;}
            terms.Add(new(original,name,qualifier,argument,modifier,reachable,dns,reference,context,problem));
        }
        // RFC7208 6.1: any all mechanism causes redirect to be ignored, regardless of modifier position.
        if(terms.Any(t=>t.Name=="all"&&!t.IsModifier&&t.Problem is null))
            for(var i=0;i<terms.Count;i++)if(terms[i].Name=="redirect")terms[i]=terms[i] with{Reachable=false};
        return new(policy,terms.AsReadOnly(),problems.AsReadOnly());
    }
    private static bool Address(string argument,bool ipv4)
    {
        var parts=argument.Split('/');
        if(parts.Length is <1 or >2 || argument.Contains('%') || !IPAddress.TryParse(parts[0],out var address)
            || address.AddressFamily!=(ipv4?AddressFamily.InterNetwork:AddressFamily.InterNetworkV6))return false;
        if(ipv4&&(parts[0].Split('.').Length!=4||parts[0].Split('.').Any(p=>p.Length==0||p.Any(c=>!char.IsAsciiDigit(c)))))return false;
        return parts.Length==1 || parts[1].Length>0&&parts[1].All(char.IsAsciiDigit)&&int.TryParse(parts[1],out var prefix)&&prefix<=(ipv4?32:128);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Core.Json;

namespace BuildStandard.Core.Reporting;

/// <summary>INT-127's closed typed request; it has no HTTP, permission, approval or tenant-operation authority.</summary>
public static class ConditionalAccessScenarioCodec
{
    public static PreparedConditionalAccessScenario Prepare(ConditionalAccessScenario scenario)
    {
        if(scenario is null)throw Bad("Supply the explicit hypothetical sign-in first.");
        var user=Identity(scenario.UserObjectId);var application=Identity(scenario.ApplicationId);
        if(scenario.DevicePlatform is not ("windows" or "iOS" or "android"))throw Bad("Choose Windows, iOS or Android. macOS and unspecified platforms are unsupported.");
        if(scenario.ClientAppType is not ("browser" or "mobileAppsAndDesktopClients" or "exchangeActiveSync" or "easSupported" or "other"))
            throw Bad("Choose an explicit supported client application type.");
        Risk(scenario.SignInRisk);Risk(scenario.UserRisk);
        var country=scenario.Country;
        if(country is not null)
        {
            if(country.Length!=2||country.Any(c=>c is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')))throw Bad("Country must be an explicit two-letter code or not supplied.");
            country=country.ToUpperInvariant();
        }
        var ip=scenario.IpAddress;
        if(ip is not null)
        {
            if(ip.Length is 0 or >45||ip.Any(char.IsWhiteSpace)||ip.Contains('%')||!IPAddress.TryParse(ip,out var parsed))throw Bad("Supply a literal IPv4/IPv6 address without a URL, port or scope identifier.");
            if(parsed.AddressFamily==AddressFamily.InterNetwork)
            {
                var parts=ip.Split('.');
                if(parts.Length!=4||parts.Any(p=>p.Length is 0 or >3||p.Length>1&&p[0]=='0'||p.Any(c=>c is < '0' or > '9')))
                    throw Bad("IPv4 must have four decimal octets without leading zeroes.");
            }
            ip=parsed.ToString();
        }
        var frozen=scenario with{UserObjectId=user,ApplicationId=application,Country=country,IpAddress=ip};
        var conditions=new JsonObject{{"devicePlatform",frozen.DevicePlatform},{"clientAppType",frozen.ClientAppType}};
        var omitted=new List<string>();
        Optional("ipAddress",frozen.IpAddress);Optional("country",frozen.Country);Optional("signInRiskLevel",frozen.SignInRisk);Optional("userRiskLevel",frozen.UserRisk);
        if(frozen.DeviceCompliant is bool compliant)conditions["deviceInfo"]=new JsonObject{{"isCompliant",compliant}};
        else omitted.Add("deviceInfo.isCompliant");
        var body=new JsonObject
        {
            ["signInIdentity"]=new JsonObject{{"@odata.type","#microsoft.graph.userSignIn"},{"userId",user}},
            ["signInContext"]=new JsonObject{{"@odata.type","#microsoft.graph.applicationContext"},{"includeApplications",new JsonArray(JsonValue.Create(application))}},
            ["signInConditions"]=conditions,["appliedPoliciesOnly"]=false
        };
        var bytes=Encoding.UTF8.GetBytes(body.ToJsonString(ToolkitJson.Compact));
        if(bytes.Length>64*1024)throw Bad("The selected scenario exceeds the 64 KiB request bound.");
        return new PreparedConditionalAccessScenario(frozen,bytes,omitted);
        void Optional(string name,string? value){if(value is null)omitted.Add(name);else conditions[name]=value;}
    }
    private static string Identity(string value)
    {
        if(value is not {Length:36}||!Guid.TryParseExact(value,"D",out var id)||id==Guid.Empty)throw Bad("Select an exact non-empty user and application GUID; names and guest substitutions are not admitted.");
        return id.ToString("D");
    }
    private static void Risk(string? value)
    {if(value is not (null or "none" or "low" or "medium" or "high"))throw Bad("Risk must be an explicit supported value or not supplied.");}
    private static ConfigurationException Bad(string text)=>new(text);
}

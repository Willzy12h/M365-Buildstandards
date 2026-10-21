using BuildStandard.Core.Json;

namespace BuildStandard.Core.Models;

/// <summary>One explicitly supplied hypothetical sign-in. Null optional conditions are never encoded as false/none.</summary>
public sealed record ConditionalAccessScenario(string UserObjectId,string ApplicationId,string DevicePlatform,string ClientAppType,
    string? IpAddress=null,string? Country=null,string? SignInRisk=null,string? UserRisk=null,bool? DeviceCompliant=null);

/// <summary>Fixed request bytes, not execution authority. Only the package-owned typed codec can create one.</summary>
public sealed class PreparedConditionalAccessScenario
{
    private readonly byte[] _bytes;
    public byte[] Bytes=>(byte[])_bytes.Clone();
    public string Sha256 {get;}
    public IReadOnlyList<string> OmittedConditions {get;}
    public ConditionalAccessScenario Scenario {get;}
    internal PreparedConditionalAccessScenario(ConditionalAccessScenario scenario,byte[] bytes,IEnumerable<string> omitted)
    {
        Scenario=scenario;_bytes=(byte[])bytes.Clone();Sha256=CanonicalJson.Sha256Hex(_bytes);
        OmittedConditions=Array.AsReadOnly(omitted.ToArray());
    }
}

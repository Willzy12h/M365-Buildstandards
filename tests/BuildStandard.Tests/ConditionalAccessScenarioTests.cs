using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessScenarioTests
{
    private static ConditionalAccessScenario Valid()=>new(TestData.Operator,TestData.TenantA,"windows","browser");
    private static JsonObject Body(PreparedConditionalAccessScenario p)=>ToolkitJson.ParseObject(Encoding.UTF8.GetString(p.Bytes));
    [Fact] public void Request_has_only_registered_user_application_and_explicit_platform_client_conditions()
    {
        var p=ConditionalAccessScenarioCodec.Prepare(Valid());var b=Body(p);
        Assert.Equal(4,b.Count);Assert.Equal("#microsoft.graph.userSignIn",b["signInIdentity"]!["@odata.type"]!.GetValue<string>());
        Assert.Equal(TestData.Operator,b["signInIdentity"]!["userId"]!.GetValue<string>());
        Assert.Equal("#microsoft.graph.applicationContext",b["signInContext"]!["@odata.type"]!.GetValue<string>());
        Assert.Single(b["signInContext"]!["includeApplications"]!.AsArray());Assert.Equal(TestData.TenantA,b["signInContext"]!["includeApplications"]![0]!.GetValue<string>());
        Assert.False(b["appliedPoliciesOnly"]!.GetValue<bool>());var c=b["signInConditions"]!.AsObject();Assert.Equal(2,c.Count);
        Assert.Equal("windows",c["devicePlatform"]!.GetValue<string>());Assert.Equal("browser",c["clientAppType"]!.GetValue<string>());
        Assert.Equal(CanonicalJson.Sha256Hex(p.Bytes),p.Sha256);Assert.Contains("deviceInfo.isCompliant",p.OmittedConditions);
    }
    [Fact] public void Explicit_false_and_none_are_preserved_while_absent_values_are_omitted()
    {
        var p=ConditionalAccessScenarioCodec.Prepare(Valid() with{DeviceCompliant=false,SignInRisk="none",Country="gb"});var c=Body(p)["signInConditions"]!.AsObject();
        Assert.False(c["deviceInfo"]!["isCompliant"]!.GetValue<bool>());Assert.Equal("none",c["signInRiskLevel"]!.GetValue<string>());Assert.Equal("GB",c["country"]!.GetValue<string>());
        Assert.False(c.ContainsKey("userRiskLevel"));Assert.False(c.ContainsKey("ipAddress"));Assert.DoesNotContain("deviceInfo.isCompliant",p.OmittedConditions);
    }
    [Theory] [InlineData("windows")][InlineData("iOS")][InlineData("android")]
    public void Only_supported_platforms_can_be_expressed(string platform)=>Assert.Equal(platform,Body(ConditionalAccessScenarioCodec.Prepare(Valid() with{DevicePlatform=platform}))["signInConditions"]!["devicePlatform"]!.GetValue<string>());
    [Theory] [InlineData("browser")][InlineData("mobileAppsAndDesktopClients")][InlineData("exchangeActiveSync")][InlineData("easSupported")][InlineData("other")]
    public void Supported_explicit_client_types_are_preserved(string client)=>Assert.Equal(client,Body(ConditionalAccessScenarioCodec.Prepare(Valid() with{ClientAppType=client}))["signInConditions"]!["clientAppType"]!.GetValue<string>());
    [Theory]
    [InlineData("null")][InlineData("user")][InlineData("app")][InlineData("braced-id")][InlineData("newline-id")]
    [InlineData("mac")][InlineData("all-platforms")][InlineData("unknown-platform")][InlineData("all-clients")][InlineData("unknown-client")]
    [InlineData("unknown-risk")][InlineData("blank-risk")][InlineData("bad-country")][InlineData("blank-country")]
    [InlineData("ip-url")][InlineData("ip-host")][InlineData("ip-short")][InlineData("ip-octal")][InlineData("ip-scope")]
    public void Unsupported_or_ambiguous_input_is_refused_without_a_request(string defect)
    {
        ConditionalAccessScenario r=Valid();r=defect switch
        {
            "null"=>null!,"user"=>r with{UserObjectId=""},"app"=>r with{ApplicationId="invalid"},"braced-id"=>r with{UserObjectId="{"+TestData.Operator+"}"},
            "newline-id"=>r with{UserObjectId=TestData.Operator+"\n"},"mac"=>r with{DevicePlatform="macOS"},"all-platforms"=>r with{DevicePlatform="all"},
            "unknown-platform"=>r with{DevicePlatform="future"},"all-clients"=>r with{ClientAppType="all"},"unknown-client"=>r with{ClientAppType="unknownFutureValue"},
            "unknown-risk"=>r with{UserRisk="unknownFutureValue"},"blank-risk"=>r with{SignInRisk=""},"bad-country"=>r with{Country="G1"},"blank-country"=>r with{Country=""},
            "ip-url"=>r with{IpAddress="https://192.0.2.1"},"ip-host"=>r with{IpAddress="example.invalid"},"ip-short"=>r with{IpAddress="127.1"},
            "ip-octal"=>r with{IpAddress="192.0.2.010"},"ip-scope"=>r with{IpAddress="fe80::1%3"},_=>throw new InvalidOperationException()
        };
        Assert.Throws<ConfigurationException>(()=>ConditionalAccessScenarioCodec.Prepare(r));
    }
    [Theory] [InlineData("192.0.2.1")][InlineData("2001:db8::1")]
    public void Literal_addresses_are_encoded_as_values_not_endpoints(string address)=>Assert.Equal(address,Body(ConditionalAccessScenarioCodec.Prepare(Valid() with{IpAddress=address}))["signInConditions"]!["ipAddress"]!.GetValue<string>());
    [Fact] public void Request_bytes_and_omissions_are_immutable_and_identical_requests_have_identical_digests()
    {
        var a=ConditionalAccessScenarioCodec.Prepare(Valid());var b=ConditionalAccessScenarioCodec.Prepare(Valid());Assert.Equal(a.Sha256,b.Sha256);
        var bytes=a.Bytes;bytes[0]=0;Assert.NotEqual(0,a.Bytes[0]);Assert.Equal(b.Bytes,a.Bytes);
        Assert.Throws<NotSupportedException>(()=>((IList<string>)a.OmittedConditions).Clear());
    }
}

using System.Net;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;
namespace BDIT.TenantToolkit.Tests;

public class ReviewFixSafetyTests
{
    private sealed class Tokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => GetAccessTokenAsync(false, ct);
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => Task.FromResult("synthetic-token");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") }); }
    }
    private static JsonObject Location(string cidr) => new()
    {
        ["@odata.type"] = "#microsoft.graph.ipNamedLocation", ["displayName"] = "Office", ["isTrusted"] = false,
        ["ipRanges"] = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.iPv4CidrRange", ["cidrAddress"] = cidr })
    };

    [Theory]
    [InlineData("/groups")]
    [InlineData("/identity/conditionalAccess/namedLocations")]
    public async Task Directory_prerequisite_PATCH_never_reaches_HTTP(string path)
    {
        var standard = TestData.Standard();
        var def = standard.Collections.Values.Single(d => d.BasePath == path);
        def.Write = path == "/groups" ? "Group.ReadWrite.All" : "Policy.ReadWrite.ConditionalAccess";
        var routes = GraphRouteAllowList.FromStandard(standard);
        Assert.NotNull(routes.MatchWrite(GraphApi.V1, path, out var existing)); Assert.False(existing);
        Assert.Null(routes.MatchWrite(GraphApi.V1, path + "/" + TestData.Office, out _));
        var handler = new Handler();
        var graph = new GraphClient(new HttpClient(handler), new Tokens(), TestData.TenantA, SessionMode.Deployment,
            routes, new GraphClientOptions { Sleep = false }, NullLog.Instance);
        await Assert.ThrowsAsync<WriteDeniedException>(() => graph.WriteAsync(GraphApi.V1, GraphWriteMethod.Patch,
            path + "/" + TestData.Office, Location("203.0.113.0/24"), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Changed_office_ranges_are_manual_even_when_an_enabled_policy_uses_them()
    {
        var standard = TestData.Standard();
        standard.Collections["namedLocations"].Write = "Policy.ReadWrite.ConditionalAccess";
        standard.Controls.Add(new ControlDefinition { Id = "PRE-008", Name = "Office", Collection = "namedLocations",
            Assessment = new AssessmentRule { Mode = AssessmentMode.Settings }, Payload = Location("198.51.100.0/24"),
            SafeDeployment = new SafeDeployment { State = "untrusted" } });
        var before = Location("203.0.113.0/24"); var current = (JsonObject)before.DeepClone(); current["id"] = TestData.Office;
        var snapshot = TestData.Snapshot(standard); snapshot.Collections["namedLocations"].Items = new() { current };
        snapshot.Collections["conditionalAccess"].Items.Add(new JsonObject { ["id"] = TestData.Mam, ["state"] = "enabled",
            ["conditions"] = new JsonObject { ["locations"] = new JsonObject { ["excludeLocations"] = new JsonArray(TestData.Office) } } });
        var mappings = TestData.Mappings(); mappings.ByControl["PRE-008"] = new ManagedObjectMapping
        { ControlId = "PRE-008", Collection = "namedLocations", ObjectId = TestData.Office, LastApplied = before };
        var plan = new DeploymentPlanner(new FixedClock(), "test").Build(new PlanRequest
        { Standard = standard, Profile = TestData.Profile(), Session = TestData.Session(), Snapshot = snapshot,
            Mappings = mappings, Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { "PRE-008" } });
        Assert.Equal(PlanAction.Manual, Assert.Single(plan.Rows).Action);
    }

    [Theory]
    [InlineData(AssignmentPopulation.AllUsers, "allLicensedUsersAssignmentTarget")]
    [InlineData(AssignmentPopulation.AllDevices, "allDevicesAssignmentTarget")]
    public void Built_in_population_is_exactly_previewed_and_cannot_be_tampered(AssignmentPopulation population, string type)
    {
        const string root = "/deviceManagement/deviceConfigurations";
        var plan = new ReviewedChangePlan { Kind = ReviewedChangeKind.AssignGroups, Population = population,
            Api = GraphApi.V1, Method = "POST", ObjectId = TestData.Office, Path = root + "/" + TestData.Office + "/assign",
            RequiredScope = "DeviceManagementConfiguration.ReadWrite.All", ExcludeGroups = { TestData.Mam },
            Payload = ReviewedChangeSafety.AssignmentPayload(root, Array.Empty<string>(), new[] { TestData.Mam }, population) };
        ReviewedChangeSafety.Assert(plan);
        var targets = plan.Payload["assignments"]!.AsArray(); Assert.Equal(2, targets.Count);
        Assert.Equal("#microsoft.graph." + type, targets[0]!["target"]!["@odata.type"]!.ToString());
        plan.Population = population == AssignmentPopulation.AllUsers ? AssignmentPopulation.AllDevices : AssignmentPopulation.AllUsers;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.AssignmentPayload(root, new[] { TestData.Office }, Array.Empty<string>(), population));
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.AssignmentPayload("/deviceManagement/windowsAutopilotDeploymentProfiles", Array.Empty<string>(), Array.Empty<string>(), population));
    }

    [Theory]
    [InlineData("guid", "officeLocationId")]
    [InlineData("guidList", "emergencyAccountIds")]
    [InlineData("jsonArray", "officeIpRanges")]
    public void Targeting_defaults_cannot_be_invented_by_a_catalogue(string type, string key)
    {
        Assert.Throws<ConfigurationException>(() => PolicyInputDefaults.AssertReviewable(new ParameterDefinition
        { Key = key, Type = type, Default = type == "guid" ? JsonValue.Create(TestData.Office) : new JsonArray(TestData.Office) }));
    }

    [Fact]
    public void Corrected_release_uses_new_exclusion_IDs_and_preserves_existing_meanings()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards"));
        var standard = StandardsLoader.Parse(File.ReadAllText(Path.Combine(root, "2026.09.11.json")), "2026.09.11.json");
        Assert.Equal(4, standard.SchemaVersion);
        Assert.Equal("users", standard.FindControl("PRE-009")!.ExclusionRole);
        Assert.Equal("devices", standard.FindControl("PRE-010")!.ExclusionRole);
        Assert.Null(standard.FindControl("PRE-001")); Assert.Null(standard.FindControl("PRE-002"));
        Assert.Contains("MAM", standard.FindControl("PRE-004")!.Name);
        Assert.Contains("Pilot", standard.FindControl("PRE-005")!.Name);
        Assert.Equal("namedLocations", standard.FindControl("PRE-008")!.Collection);
        Assert.NotEmpty(standard.FindControl("CFG-WIN-003")!.Prerequisites!);
    }
}

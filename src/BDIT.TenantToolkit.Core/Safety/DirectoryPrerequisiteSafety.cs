using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>
/// Guards the two directory objects every other control depends on: the security groups that policies are assigned to,
/// and the named location that Conditional Access candidates exclude.
///
/// Writing to <c>/groups</c> is the widest permission the toolkit asks for, so the payload is constrained rather than
/// trusted. A created group is an empty, assigned-membership security group: no member, no owner, no dynamic rule and
/// no role-assignable flag, because each of those grants access rather than describing a population. A created named
/// location carries only client-confirmed CIDR ranges and is never created trusted, since trust changes how every
/// Conditional Access policy evaluates a sign-in.
///
/// Neither guard can adopt or modify an existing object: the planner refuses a name that is already in use, and the
/// route allow list permits creation at the collection root only.
/// </summary>
public static class DirectoryPrerequisiteSafety
{
    public const string GroupsPath = "/groups";
    public const string NamedLocationsPath = "/identity/conditionalAccess/namedLocations";

    public static bool IsCreationOnlyPath(string path) =>
        string.Equals(path.TrimEnd('/'), GroupsPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.TrimEnd('/'), NamedLocationsPath, StringComparison.OrdinalIgnoreCase);

    public static bool IsGroup(CollectionDefinition def) =>
        string.Equals(def.BasePath.TrimEnd('/'), GroupsPath, StringComparison.OrdinalIgnoreCase);

    public static bool IsNamedLocation(CollectionDefinition def) =>
        string.Equals(def.BasePath.TrimEnd('/'), NamedLocationsPath, StringComparison.OrdinalIgnoreCase);

    public static void Assert(CollectionDefinition def, JsonObject payload)
    {
        if (IsGroup(def)) AssertGroup(payload);
        else if (IsNamedLocation(def)) AssertNamedLocation(payload, def.PublicIpRangesOnly == true);
    }

    private static void AssertGroup(JsonObject payload)
    {
        foreach (var key in payload.Select(p => p.Key))
        {
            if (key.Contains("@odata.bind", StringComparison.OrdinalIgnoreCase))
                throw new SafetyViolationException("A group candidate cannot bind members or owners. Groups are created empty and populated deliberately.");
            if (key is "members" or "owners" or "membershipRule" or "membershipRuleProcessingState" or "isAssignableToRole"
                or "resourceBehaviorOptions" or "resourceProvisioningOptions")
                throw new SafetyViolationException($"A group candidate cannot set '{key}'. Membership, ownership, dynamic rules and role-assignable groups are separate reviewed decisions.");
        }
        if (payload["securityEnabled"] is not JsonValue security || !security.TryGetValue<bool>(out var securityEnabled) || !securityEnabled)
            throw new SafetyViolationException("Only security groups are created.");
        if (payload["mailEnabled"] is not JsonValue mail || !mail.TryGetValue<bool>(out var mailEnabled) || mailEnabled)
            throw new SafetyViolationException("Mail-enabled groups are not created; they carry a mailbox and distribution behaviour that this workflow does not review.");
        if (payload["groupTypes"] is not JsonArray types || types.Count > 0)
            throw new SafetyViolationException("A group candidate must declare an empty groupTypes list: unified and dynamic groups are not created.");
        var name = payload["displayName"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(name)) throw new SafetyViolationException("A group candidate needs a display name.");
        var nickname = payload["mailNickname"]?.GetValue<string>() ?? "";
        if (nickname.Length is 0 or > 64 || !nickname.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new SafetyViolationException("A group candidate needs a simple mail nickname of at most 64 letters, digits, hyphens, underscores or full stops.");
    }

    private static void AssertNamedLocation(JsonObject payload, bool publicOnly)
    {
        if (payload["@odata.type"]?.ToString() != "#microsoft.graph.ipNamedLocation")
            throw new SafetyViolationException("Only IP named locations are created. Country locations depend on Microsoft's address attribution and are a separate reviewed decision.");
        if (payload["isTrusted"] is not JsonValue trusted || !trusted.TryGetValue<bool>(out var isTrusted) || isTrusted)
            throw new SafetyViolationException("A named location is never created trusted. Trust changes risk evaluation for every Conditional Access policy and is marked deliberately after review.");
        if (string.IsNullOrWhiteSpace(payload["displayName"]?.GetValue<string>()))
            throw new SafetyViolationException("A named location candidate needs a display name.");
        if (payload["ipRanges"] is not JsonArray ranges || ranges.Count == 0)
            throw new SafetyViolationException("Provide the client-confirmed office IP ranges before creating the named location.");
        foreach (var range in ranges)
        {
            if (range is not JsonObject entry) throw new SafetyViolationException("Each IP range must be a Graph ipRange object.");
            var type = entry["@odata.type"]?.ToString() ?? "";
            if (type is not ("#microsoft.graph.iPv4CidrRange" or "#microsoft.graph.iPv6CidrRange"))
                throw new SafetyViolationException("Each IP range must declare the IPv4 or IPv6 CIDR range type.");
            var cidr = entry["cidrAddress"]?.ToString() ?? "";
            if (!IsCidr(cidr, type.Contains("iPv6", StringComparison.Ordinal)))
                throw new SafetyViolationException($"'{cidr}' is not a CIDR range of the declared type.");
            if (publicOnly && !PublicIpRange.IsPublic(cidr))
                throw new SafetyViolationException("Office ranges must be public, globally routable CIDR ranges.");
            foreach (var key in entry.Select(p => p.Key))
                if (key is not ("@odata.type" or "cidrAddress"))
                    throw new SafetyViolationException($"An IP range carries only its type and address; '{key}' is not written.");
        }
    }

    /// <summary>A whole range is required: a bare address, a missing prefix or a prefix outside the family is refused.</summary>
    private static bool IsCidr(string value, bool ipv6)
    {
        var parts = value.Split('/');
        if (parts.Length != 2) return false;
        if (!System.Net.IPAddress.TryParse(parts[0], out var address)) return false;
        var family = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        if (family != ipv6) return false;
        if (!int.TryParse(parts[1], out var prefix)) return false;
        return prefix >= 0 && prefix <= (ipv6 ? 128 : 32);
    }
}

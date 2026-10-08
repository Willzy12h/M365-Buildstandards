using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Naming;

/// <summary>Internal naming policy, not a claim that Microsoft enforces these prefixes or characters.</summary>
public static class NamingConvention
{
    public sealed record Rule(string Prefix, int? MicrosoftMaximum, string MicrosoftReference);
    public sealed record Check(string State, string Reason, int? MicrosoftMaximum, string MicrosoftReference);
    public const string Conforming = "Conforming";
    public const string NonConforming = "Non-conforming";
    public const string Unknown = "Unable to check";
    private static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.Ordinal)
    {
        ["groups"] = new("GRP", 256, "https://learn.microsoft.com/en-us/graph/api/resources/group?view=graph-rest-1.0"),
        ["namedLocations"] = new("LOC", null, "https://learn.microsoft.com/en-us/graph/api/resources/namedlocation?view=graph-rest-1.0"),
        ["conditionalAccess"] = new("CA", null, "https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy?view=graph-rest-1.0"),
        ["compliance"] = new("CMP", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicecompliancepolicy?view=graph-rest-1.0"),
        ["extendedCompliance"] = new("CMP", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicecompliancepolicy?view=graph-rest-beta"),
        ["configuration"] = new("CFG", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-deviceconfiguration?view=graph-rest-1.0"),
        ["settingsCatalogue"] = new("CFG", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfigv2-devicemanagementconfigurationpolicy?view=graph-rest-beta"),
        ["endpointProtection"] = new("CFG", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfigv2-devicemanagementconfigurationpolicy?view=graph-rest-beta"),
        ["appProtection"] = new("MAM", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-mam-managedapppolicy?view=graph-rest-1.0"),
        ["iosProtection"] = new("MAM", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-mam-managedapppolicy?view=graph-rest-1.0"),
        ["androidProtection"] = new("MAM", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-mam-managedapppolicy?view=graph-rest-1.0"),
        ["applications"] = new("APP", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileapp?view=graph-rest-1.0")
    };
    public static IReadOnlyCollection<string> Collections => Rules.Keys.ToArray();
    public static Rule? ForCollection(string collection) => Rules.GetValueOrDefault(collection);

    public static Check CheckName(string? name, string collection, string? platformSuffix = null)
    {
        var rule = ForCollection(collection);
        if (rule is null) return new(Unknown, "This object type has no reviewed naming rule.", null, "");
        if (name is null) return new(Unknown, "The capture did not return a string name.", rule.MicrosoftMaximum, rule.MicrosoftReference);
        var prefix = rule.Prefix + " - ";
        var faults = new List<string>();
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length <= prefix.Length)
            faults.Add("Use " + rule.Prefix + " - <description>, with an uppercase type and a non-empty description.");
        if (name != name.Trim() || name.Contains("  ", StringComparison.Ordinal)) faults.Add("Remove leading, trailing or repeated spaces.");
        if (name.Any(c => !char.IsLetterOrDigit(c) && !" -_().,&/'".Contains(c)))
            faults.Add("Internal policy permits letters, digits, spaces and - _ ( ) . , & / '. Control characters are not permitted.");
        if (rule.MicrosoftMaximum is { } maximum && name.Length > maximum) faults.Add("The documented maximum is " + maximum + " characters.");
        if (!string.IsNullOrEmpty(platformSuffix) && !name.EndsWith(" - " + platformSuffix, StringComparison.Ordinal))
            faults.Add("Use the explicit platform suffix - " + platformSuffix + ".");
        var limitation = rule.MicrosoftMaximum is null
            ? " Microsoft does not state a name limit in the inspected resource reference; service-limit validation remains unverified."
            : " The documented group limit is checked conservatively in UTF-16 code units.";
        return new(faults.Count == 0 ? Conforming : NonConforming,
            (faults.Count == 0 ? "Matches the internal naming policy." : string.Join(" ", faults)) + limitation,
            rule.MicrosoftMaximum, rule.MicrosoftReference);
    }

    /// <summary>Explicit new-authoring check. Never called to rewrite or reject a historical catalogue.</summary>
    public static Check CheckAuthored(ControlDefinition control, string? resolvedName)
    {
        var platform = control.Id.Split('-').Skip(1).FirstOrDefault() switch
        { "WIN" => "Windows", "IOS" => "iOS", "AND" => "Android", "MAC" => "macOS", _ => null };
        return CheckName(resolvedName, control.Collection ?? "", platform);
    }

    public static void RequireAuthored(ControlDefinition control, string? resolvedName)
    {
        var check = CheckAuthored(control, resolvedName);
        if (check.State != Conforming) throw new ConfigurationException("New authored name for " + control.Id + ": " + check.Reason);
    }
}

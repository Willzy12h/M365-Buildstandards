using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core;
using System.Globalization;
using System.Text;

namespace BDIT.TenantToolkit.Engine.Naming;

/// <summary>Internal naming policy, not a claim that Microsoft enforces these prefixes or characters.</summary>
public static class NamingConvention
{
    /// <summary>
    /// <paramref name="GraphFamily"/> names the Graph object set a collection reads. Collections sharing a family return the
    /// same objects (for example the v1.0 and beta reads of one path, or the iOS/Android protection subtypes of
    /// managedAppPolicies), so one object can be captured under several collection keys. Reviewed against the published
    /// catalogue routes by test.
    /// </summary>
    public sealed record Rule(string Prefix, int? MicrosoftMaximum, string MicrosoftReference, string GraphFamily);
    public sealed record Check(string State, string Reason, int? MicrosoftMaximum, string MicrosoftReference);
    public const string Conforming = "Conforming";
    public const string NonConforming = "Non-conforming";
    public const string Unknown = "Unable to check";
    private const string DeviceConfigurations = "/deviceManagement/deviceConfigurations";
    private const string CompliancePolicies = "/deviceManagement/deviceCompliancePolicies";
    private const string ManagedAppPolicies = "/deviceAppManagement/managedAppPolicies";
    private const string MobileApps = "/deviceAppManagement/mobileApps";
    private const string DeviceConfigurationReference = "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-deviceconfiguration?view=graph-rest-1.0";
    private const string ManagedAppPolicyReference = "https://learn.microsoft.com/en-us/graph/api/resources/intune-mam-managedapppolicy?view=graph-rest-1.0";
    private const string MobileAppReference = "https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileapp?view=graph-rest-1.0";
    private static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.Ordinal)
    {
        ["groups"] = new("GRP", 256, "https://learn.microsoft.com/en-us/graph/api/resources/group?view=graph-rest-1.0", "/groups"),
        ["namedLocations"] = new("LOC", null, "https://learn.microsoft.com/en-us/graph/api/resources/namedlocation?view=graph-rest-1.0", "/identity/conditionalAccess/namedLocations"),
        ["conditionalAccess"] = new("CA", null, "https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy?view=graph-rest-1.0", "/identity/conditionalAccess/policies"),
        ["compliance"] = new("CMP", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicecompliancepolicy?view=graph-rest-1.0", CompliancePolicies),
        ["extendedCompliance"] = new("CMP", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicecompliancepolicy?view=graph-rest-beta", CompliancePolicies),
        ["configuration"] = new("CFG", null, DeviceConfigurationReference, DeviceConfigurations),
        ["settingsCatalogue"] = new("CFG", null, "https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfigv2-devicemanagementconfigurationpolicy?view=graph-rest-beta", "/deviceManagement/configurationPolicies"),
        ["endpointProtection"] = new("CFG", null, DeviceConfigurationReference, DeviceConfigurations),
        ["appProtection"] = new("MAM", null, ManagedAppPolicyReference, ManagedAppPolicies),
        ["iosProtection"] = new("MAM", null, ManagedAppPolicyReference, ManagedAppPolicies),
        ["androidProtection"] = new("MAM", null, ManagedAppPolicyReference, ManagedAppPolicies),
        ["apps"] = new("APP", null, MobileAppReference, MobileApps),
        ["applications"] = new("APP", null, MobileAppReference, MobileApps)
    };
    public static IReadOnlyCollection<string> Collections => Rules.Keys.ToArray();
    public static Rule? ForCollection(string collection) => Rules.GetValueOrDefault(collection);

    /// <summary>Other reviewed collections that read the same Graph objects as <paramref name="collection"/>.</summary>
    public static IReadOnlyCollection<string> OverlappingCollections(string collection) =>
        ForCollection(collection) is { } rule
            ? Rules.Where(r => r.Key != collection && r.Value.GraphFamily == rule.GraphFamily).Select(r => r.Key).Order(StringComparer.Ordinal).ToArray()
            : [];

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
        if (HasDisallowedCharacter(name))
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

    /// <summary>
    /// Checks the internal character set by Unicode scalar after NFC normalisation, so decomposed accents and
    /// supplementary-plane letters count as letters. A combining mark is accepted only when it follows a letter (or another
    /// mark attached to one); unpaired surrogates are refused.
    /// </summary>
    private static bool HasDisallowedCharacter(string name)
    {
        string text;
        try { text = name.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return true; }
        var attachedToLetter = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune == Rune.ReplacementChar) return true;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            { if (!attachedToLetter) return true; continue; }
            attachedToLetter = Rune.IsLetter(rune);
            if (!Rune.IsLetterOrDigit(rune) && !(rune.IsAscii && " -_().,&/'".Contains((char)rune.Value))) return true;
        }
        return false;
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

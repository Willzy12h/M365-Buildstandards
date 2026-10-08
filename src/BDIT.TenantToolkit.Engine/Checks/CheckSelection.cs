using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;

namespace BDIT.TenantToolkit.Engine.Checks;

/// <summary>A package/catalogue-owned dependency selection, never an engineer-supplied Graph route.</summary>
public sealed class CheckSelection
{
    private CheckSelection(StandardCatalogue standard, TenantProfile profile, string selectorKind, string selector,
        IReadOnlyList<ControlDefinition> controls)
    {
        SelectorKind = selectorKind;
        Selector = selector;
        StandardRelease = standard.Release;
        StandardDigest = standard.IntegrityDigest;
        ClientScopeDigest = ReviewedClientScope.Digest(profile);
        ControlIds = Array.AsReadOnly(controls.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray());
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controls)
        {
            if (control.Collection is { Length: > 0 } primary) dependencies.Add(primary);
            if (control.Equivalence?.Collection is { Length: > 0 } equivalent) dependencies.Add(equivalent);
            if (control.Licence.ServicePlans.Count > 0) dependencies.Add(AssessmentEngine.LicenceCollectionKey);
            // These existing assessors read beyond the control's primary collection.
            if (standard.SchemaVersion >= 5 && control.Id == "UPD-001") { dependencies.Add("configuration"); dependencies.Add("featureUpdates"); }
            if (standard.SchemaVersion >= 5 && control.Id == "ID-004") dependencies.Add("passkeyProfiles");
        }
        foreach (var key in dependencies)
            if (!standard.Collections.ContainsKey(key))
                throw new ConfigurationException($"The check requires an unregistered collection: {key}.");
        CollectionKeys = Array.AsReadOnly(dependencies.Order(StringComparer.Ordinal).ToArray());
    }

    public string SelectorKind { get; }
    public string Selector { get; }
    public string StandardRelease { get; }
    public string StandardDigest { get; }
    public string ClientScopeDigest { get; }
    public IReadOnlyList<string> ControlIds { get; }
    public IReadOnlyList<string> CollectionKeys { get; }

    // Area names are the reviewed shared modules. Their controls and routes come from the original catalogue.
    public static IReadOnlyList<string> Areas { get; } = Array.AsReadOnly(new[] { "Entra", "Intune", "Exchange", "Purview" });

    public static CheckSelection ForArea(StandardCatalogue standard, TenantProfile profile, string area)
    {
        var name = Areas.SingleOrDefault(a => string.Equals(a, area, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConfigurationException($"Unknown check area '{area}'. Choose {string.Join(", ", Areas)}.");
        var controls = ControlInstances.All(standard, profile).Where(c => c.Area == name).ToList();
        if (controls.Count == 0) throw new ConfigurationException($"The loaded standard has no controls in {name}.");
        return new CheckSelection(standard, profile, "area", name, controls);
    }

    public static CheckSelection ForControl(StandardCatalogue standard, TenantProfile profile, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ConfigurationException("Choose a registered control to check.");
        var instances = ControlInstances.All(standard, profile);
        var definition = standard.FindControl(id);
        var controls = instances.Where(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)
            || (definition?.RepeatFor == "officeLocations" && c.Id.StartsWith(definition.Id + "-", StringComparison.Ordinal))).ToList();
        if (controls.Count == 0) throw new ConfigurationException($"Unknown check control '{id}' in the loaded standard/client scope.");
        return new CheckSelection(standard, profile, "control", definition?.Id ?? controls[0].Id, controls);
    }

    public void ValidateFor(StandardCatalogue standard, TenantProfile profile)
    {
        var current = SelectorKind == "area" ? ForArea(standard, profile, Selector) : ForControl(standard, profile, Selector);
        if (StandardRelease != current.StandardRelease || StandardDigest != current.StandardDigest
            || ClientScopeDigest != current.ClientScopeDigest || !ControlIds.SequenceEqual(current.ControlIds)
            || !CollectionKeys.SequenceEqual(current.CollectionKeys))
            throw new ConfigurationException("The standard or reviewed client scope changed. Choose the check again.");
    }
}

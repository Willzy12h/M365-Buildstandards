namespace BuildStandard.Core.Reporting;
/// <summary>Frozen explicit metadata selection, never an arbitrary URL or execution capability.</summary>
public sealed class CollaborationReadSelection
{
    public string? Search { get; }
    public IReadOnlyList<string> Sites { get; }
    public IReadOnlyList<string> Groups { get; }
    public bool Libraries { get; }
    public bool Owners { get; }
    public bool Members { get; }
    public CollaborationReadSelection(string? search, IEnumerable<string> sites, IEnumerable<string> groups, bool libraries, bool owners, bool members)
    {
        ArgumentNullException.ThrowIfNull(sites); ArgumentNullException.ThrowIfNull(groups);
        var selectedSites = sites.Take(51).ToArray(); var selectedGroups = groups.Take(51).ToArray();
        if (selectedSites.Length + selectedGroups.Length > 50
            || selectedSites.Distinct(StringComparer.Ordinal).Count() != selectedSites.Length
            || selectedGroups.Distinct(StringComparer.Ordinal).Count() != selectedGroups.Length)
            throw new ConfigurationException("Select at most 50 unique sites and groups combined. Ambiguous repeated identities are not silently removed.");
        if (search is null && selectedSites.Length == 0 && selectedGroups.Length == 0)
            throw new ConfigurationException("Select a bounded search, site or group before acknowledgement.");
        if (libraries && selectedSites.Length == 0 || (owners || members) && selectedGroups.Length == 0)
            throw new ConfigurationException("Child sections require an exact selected parent.");
        if (search is not null) _ = CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteSearch, search);
        foreach (var site in selectedSites) _ = CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteMetadata, site);
        foreach (var group in selectedGroups) _ = CollaborationInventoryRegistry.Path(CollaborationReadKind.GroupMetadata, group);
        Search = search; Sites = Array.AsReadOnly(selectedSites); Groups = Array.AsReadOnly(selectedGroups);
        Libraries = libraries; Owners = owners; Members = members;
    }
    public IReadOnlyList<string> Scopes()
    {
        var result = new List<string>();
        if (Search is not null || Sites.Count != 0) result.Add("Sites.Read.All");
        if (Groups.Count != 0) result.Add("Group.Read.All");
        if (Members) result.Add("TeamMember.Read.All");
        return result.AsReadOnly();
    }
    public string RequirePath(CollaborationReadKind kind, string value)
    {
        var allowed = kind switch
        {
            CollaborationReadKind.SiteSearch => Search is not null && value == Search,
            CollaborationReadKind.SiteMetadata => Sites.Contains(value, StringComparer.Ordinal),
            CollaborationReadKind.SiteLibraries => Libraries && Sites.Contains(value, StringComparer.Ordinal),
            CollaborationReadKind.GroupMetadata => Groups.Contains(value, StringComparer.Ordinal),
            CollaborationReadKind.GroupOwners => Owners && Groups.Contains(value, StringComparer.Ordinal),
            CollaborationReadKind.TeamMembers => Members && Groups.Contains(value, StringComparer.Ordinal),
            _ => false
        };
        if (!allowed) throw new ConfigurationException("The read is outside the frozen collaboration selection. No request is admitted.");
        return CollaborationInventoryRegistry.Path(kind, value);
    }
}

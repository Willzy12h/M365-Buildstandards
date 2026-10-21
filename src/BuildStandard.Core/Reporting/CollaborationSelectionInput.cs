namespace BuildStandard.Core.Reporting;
/// <summary>Native text input only; registry validation remains authoritative and performs no reads.</summary>
public static class CollaborationSelectionInput
{
    public static CollaborationReadSelection Parse(string search, string sites, string groups, bool libraries, bool owners, bool members)
    {
        if (search is null || sites is null || groups is null || search.Length > 256 || sites.Length > 32768 || groups.Length > 32768)
            throw new ConfigurationException("Keep search within 256 characters and each selection within 32,768 characters; nothing is silently clipped.");
        static string[] Lines(string value) => value.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var query = search.Trim();
        return new(query.Length == 0 ? null : query, Lines(sites), Lines(groups), libraries, owners, members);
    }
}

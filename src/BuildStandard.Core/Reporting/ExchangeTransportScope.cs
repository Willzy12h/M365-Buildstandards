namespace BuildStandard.Core.Reporting;
/// <summary>INT-132 exact selected rule GUIDs or an explicitly requested organisation inventory, never an implicit scan.</summary>
public sealed record ExchangeTransportScope(bool OrganisationInventory,IReadOnlyList<string> RuleGuids)
{
    public static ExchangeTransportScope Parse(string? ruleGuids,bool organisationInventory)
    {
        var selected=ruleGuids is not (null or "");
        if(selected==organisationInventory)throw new ConfigurationException("Choose exact rule GUIDs or explicitly request organisation inventory, never both/neither.");
        return selected ? new(false,ExchangeTargetSelection.Parse(ruleGuids!)) : new(true,Array.Empty<string>());
    }
}

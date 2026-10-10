namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>
/// The one rule for a typed tenant confirmation, used by every gate before a tenant write: the engineer types the
/// tenant ID in full. Surrounding spaces are ignored and so is letter case, because a GUID's letters carry no meaning
/// and an engineer copying one from a portal in capitals is not confirming a different tenant. Nothing shorter, longer
/// or different matches, and an empty expected value matches nothing.
/// </summary>
public static class TenantConfirmation
{
    public static bool Matches(string? typed, string? expectedTenantId) =>
        !string.IsNullOrWhiteSpace(expectedTenantId)
        && string.Equals(typed?.Trim(), expectedTenantId.Trim(), StringComparison.OrdinalIgnoreCase);
}

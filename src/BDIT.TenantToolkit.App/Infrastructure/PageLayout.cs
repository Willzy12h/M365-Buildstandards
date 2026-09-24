namespace BDIT.TenantToolkit.App.Infrastructure;

/// <summary>Layout rules shared by the pages whose tables fill the window.</summary>
public static class PageLayout
{
    /// <summary>
    /// The height below which a filling page stops shrinking and scrolls instead. Those pages - Assessment,
    /// Configuration, Deviations, Manual checks, Plan and Build Standard - give their tables whatever height is left
    /// under the page heading, so a short window squeezed the tables, and the guidance beside them, out of reach with no
    /// way to scroll to it. At the default window size the page is well above this and nothing changes; on a 1920x1080
    /// laptop at 150% scaling it scrolls.
    /// </summary>
    public const double MinimumHeight = 600;
}

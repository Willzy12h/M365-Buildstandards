namespace BuildStandard.App.ViewModels;

/// <summary>Native navigation metadata only. Routes preserve their existing page/service instances.</summary>
public sealed record NativeRoute(string Group, string Key, string Step, string Title);
public sealed record NativeNavigationGroup(string Name, bool IsActive)
{ public string AccessibilityStatus => IsActive ? "Current navigation group" : "Navigation group"; }
public static class RouteCatalog
{
    public static IReadOnlyList<NativeRoute> Items { get; } = Array.AsReadOnly(new NativeRoute[]
    {
        new("Workspace", "home", "", "Command centre"),
        new("Workspace", "connect", "", "Client and connection"),
        new("Workspace", "overview", "", "Overview and licences"),
        new("Build standard", "configuration", "1", "Configuration"),
        new("Build standard", "assessment", "2", "Assessment"),
        new("Build standard", "deviations", "3", "Deviations"),
        new("Build standard", "plan", "4", "Plan changes"),
        new("Build standard", "deploy", "5", "Deploy and outcomes"),
        new("Build standard", "automation", "6", "Policy automation"),
        new("Build standard", "checks", "", "Manual checks"),
        new("Build standard", "jobs", "", "Jobs and completion"),
        new("Build standard", "history", "", "Evidence and drift"),
        new("Build standard", "recovery", "", "Undo and recovery"),
        new("Build standard", "standard", "", "Catalogue reference"),
        new("Reports", "reports", "", "Tenant reports"),
        new("Reports", "naming", "", "Naming and ownership"),
        new("Reports", "mailbox-capacity", "", "Mailbox capacity"),
        new("Scripts", "scripts", "", "Script library"),
        new("Scripts", "run-reports", "", "Registered read-only runs"),
        new("Settings", "setup", "", "Application setup"),
        new("Settings", "settings", "", "Settings and diagnostics")
    });
    public static IReadOnlyList<string> Groups { get; } = Array.AsReadOnly(Items.Select(r => r.Group).Distinct(StringComparer.Ordinal).ToArray());
}

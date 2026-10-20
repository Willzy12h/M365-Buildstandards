namespace BuildStandard.Core.Models;
/// <summary>Ephemeral, deliberately selected access; never persisted into a standard or tenant profile.</summary>
public enum OptionalReportAccess { None, UsageReports }
public static class OptionalReportAccessPolicy
{
    public const string UsageScope="Reports.Read.All";
    public static void Validate(OptionalReportAccess access)
    {if(access is not OptionalReportAccess.None and not OptionalReportAccess.UsageReports)throw new ConfigurationException("Choose a supported optional report access intent.");}
    public static IEnumerable<string> Scopes(OptionalReportAccess access)
    {Validate(access);return access==OptionalReportAccess.UsageReports?[UsageScope]:[];}
    public static bool IsSatisfied(OptionalReportAccess access,IEnumerable<string> returnedScopes)
    {Validate(access);return access==OptionalReportAccess.None||returnedScopes.Contains(UsageScope,StringComparer.OrdinalIgnoreCase)||returnedScopes.Contains("https://graph.microsoft.com/"+UsageScope,StringComparer.OrdinalIgnoreCase);}
}

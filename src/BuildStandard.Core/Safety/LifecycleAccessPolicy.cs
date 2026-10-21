using BuildStandard.Core.Models;
namespace BuildStandard.Core.Safety;

/// <summary>Ephemeral selected-action purpose, separate from optional read reports. Never modifies a catalogue or grants access.</summary>
public static class LifecycleAccessPolicy
{
    public static IReadOnlyList<string> WriteScopes {get;}=Array.AsReadOnly(new[]{"GroupMember.ReadWrite.All","LicenseAssignment.ReadWrite.All","User.EnableDisableAccount.All","User.RevokeSessions.All"});
    public static string? Normalise(string? actionId)
    {
        if(string.IsNullOrEmpty(actionId))return null;
        if(!LifecycleActionRegistry.ActionIds.Contains(actionId,StringComparer.Ordinal))throw new ConfigurationException("Choose one registered lifecycle access purpose, or none. Arbitrary scopes and actions are not accepted.");
        return actionId;
    }
    public static IReadOnlyList<string> Scopes(SessionMode mode,string? actionId)
    {
        if(!Enum.IsDefined(mode))throw new ConfigurationException("Choose assessment or deployment access.");
        var selected=Normalise(actionId);if(selected is null)return Array.Empty<string>();
        // Only enumerate the fixed registry's scope metadata. No target or request description escapes this method.
        const string placeholder="00000000-0000-4000-8000-000000000001";
        var operation=LifecycleActionRegistry.Create(selected,placeholder,selected.EndsWith("member",StringComparison.Ordinal)?placeholder:null,selected.EndsWith("license",StringComparison.Ordinal)?placeholder:null);
        var read=LifecycleReadRegistry.For(operation).Select(source=>source.Scope);
        return read.Concat(mode==SessionMode.Deployment?new[]{operation.WriteScope}:Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    /// <summary>May retain only previously configured reviewed purposes. Never adds them to a selected token or consent request.</summary>
    public static IEnumerable<string> RetainedRegistrationScopes(StandardCatalogue standard,SessionMode mode)=>
        OptionalReportAccessPolicy.PreservedOptionalScopes.Concat(new[]{"User.Read.All","Group.Read.All","Organization.Read.All","RoleManagement.Read.Directory"})
            .Concat(mode==SessionMode.Deployment?WriteScopes.Concat(standard.WriteScopes()):Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
}

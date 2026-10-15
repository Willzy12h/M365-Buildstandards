using BuildStandard.Core.Models;
using BuildStandard.Core.Json;
namespace BuildStandard.Engine.Planning;
public static class PolicyInputScope
{
    public static IReadOnlySet<string> Keys(IEnumerable<ParameterDefinition> parameters, ControlDefinition? control, bool showAll)
    {
        var used = ParameterUsage.Keys(control?.Payload);
        return parameters.Where(p => showAll || control is not null && (used.Contains(p.Key) || p.RequiredForControls?.Contains(control.Id, StringComparer.OrdinalIgnoreCase) == true))
            .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
    }
}

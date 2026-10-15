using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Planning;
public static class PolicyInputScope
{
    public static IReadOnlySet<string> Keys(IEnumerable<ParameterDefinition> parameters, ControlDefinition? control, bool showAll) =>
        throw new NotSupportedException("Focused policy input presentation not implemented.");
}

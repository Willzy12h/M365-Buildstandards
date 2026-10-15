using BuildStandard.Core.Graph;
namespace BuildStandard.Engine.Identity;
public enum ObjectLookupKind { Group, NamedLocation }
public sealed record ResolvedTenantObject(string TenantId, string ObjectId, string DisplayName, ObjectLookupKind Kind)
{
    public string Label => DisplayName + " · " + ObjectId;
}
public static class ObjectResolver
{
    public static Task<IReadOnlyList<ResolvedTenantObject>> SearchAsync(IGraphClient graph, string tenantId, ObjectLookupKind kind, string query, CancellationToken ct) =>
        throw new NotSupportedException("Object choice lookup not implemented.");
}

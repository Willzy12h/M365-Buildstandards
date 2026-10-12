using BuildStandard.App.Services;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

/// <summary>Local editor identity; labels and edit timestamps do not discard an unfinished review.</summary>
internal static class EvidenceReviewContext
{
    public static string For(Workspace workspace) => workspace.Profile is { } profile && workspace.Standard is { } standard
        ? profile.TenantId.Trim().ToLowerInvariant() + "|" + standard.IntegrityDigest + "|" + ReviewedClientScope.Digest(profile)
        : "";
}

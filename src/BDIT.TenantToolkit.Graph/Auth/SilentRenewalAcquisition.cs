using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Graph.Auth;

/// <summary>The existing provider's silent-renewal policy seam. Owns no token/cache and has no interactive route.</summary>
internal static class SilentRenewalAcquisition
{
    internal static async Task<TResult> AcquireAsync<TResult>(Func<Task<TResult>> silent,
        Func<TResult, bool> sameIdentity, Func<TResult, IEnumerable<string>> scopes,
        IReadOnlyList<string> expectedScopes, Action invalidate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await silent();
        ct.ThrowIfCancellationRequested();
        if (!sameIdentity(result) || !MsalAuthenticator.SameAuthorisationScopes(expectedScopes, scopes(result)))
        {
            invalidate();
            throw new AuthenticationRequiredException("Returned identity or permissions changed during silent renewal. Disconnect and reconnect, then review access and experimental authorisation again. No interactive retry was performed.");
        }
        return result;
    }
}
